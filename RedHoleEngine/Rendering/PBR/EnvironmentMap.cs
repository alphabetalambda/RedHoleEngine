using System;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RedHoleEngine.Rendering.PBR;

/// <summary>
/// Represents an HDR environment map for Image-Based Lighting (IBL).
/// Stores equirectangular HDR image data for sky rendering and specular reflections.
/// </summary>
public class EnvironmentMap : IDisposable
{
    private float[] _hdrData;
    private int _width;
    private int _height;
    private float _intensity = 1.0f;
    private float _rotation;
    
    /// <summary>
    /// Width of the environment map in pixels
    /// </summary>
    public int Width => _width;
    
    /// <summary>
    /// Height of the environment map in pixels
    /// </summary>
    public int Height => _height;
    
    /// <summary>
    /// HDR pixel data (RGB float, 3 floats per pixel)
    /// </summary>
    public float[] HdrData => _hdrData;
    
    /// <summary>
    /// Intensity multiplier for the environment
    /// </summary>
    public float Intensity
    {
        get => _intensity;
        set => _intensity = Math.Max(0, value);
    }
    
    /// <summary>
    /// Rotation around Y axis in radians
    /// </summary>
    public float Rotation
    {
        get => _rotation;
        set => _rotation = value;
    }
    
    /// <summary>
    /// Path to the loaded file
    /// </summary>
    public string? FilePath { get; private set; }
    
    /// <summary>
    /// Whether the environment map is loaded and valid
    /// </summary>
    public bool IsLoaded => _hdrData != null && _width > 0 && _height > 0;

    public EnvironmentMap()
    {
        _hdrData = Array.Empty<float>();
        _width = 0;
        _height = 0;
    }

    /// <summary>
    /// Load an HDR environment map from file.
    /// Supports .hdr (Radiance) and standard image formats (converted to HDR).
    /// </summary>
    public bool Load(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Console.WriteLine($"Environment map not found: {path}");
            return false;
        }

        try
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            
            if (extension == ".hdr")
            {
                return LoadRadianceHdr(path);
            }
            else
            {
                // Load as standard image and convert to HDR
                return LoadStandardImage(path);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load environment map {path}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load Radiance HDR format (.hdr)
    /// </summary>
    private bool LoadRadianceHdr(string path)
    {
        // Use ImageSharp's HDR support
        using var image = Image.Load<RgbaVector>(path);
        
        _width = image.Width;
        _height = image.Height;
        _hdrData = new float[_width * _height * 3];
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    int idx = (y * _width + x) * 3;
                    _hdrData[idx] = row[x].R;
                    _hdrData[idx + 1] = row[x].G;
                    _hdrData[idx + 2] = row[x].B;
                }
            }
        });
        
        FilePath = path;
        Console.WriteLine($"Loaded HDR environment: {path} ({_width}x{_height})");
        return true;
    }

    /// <summary>
    /// Load standard image format and convert to linear HDR
    /// </summary>
    private bool LoadStandardImage(string path)
    {
        using var image = Image.Load<Rgba32>(path);
        
        _width = image.Width;
        _height = image.Height;
        _hdrData = new float[_width * _height * 3];
        
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < accessor.Width; x++)
                {
                    int idx = (y * _width + x) * 3;
                    // Convert sRGB to linear
                    _hdrData[idx] = SrgbToLinear(row[x].R / 255f);
                    _hdrData[idx + 1] = SrgbToLinear(row[x].G / 255f);
                    _hdrData[idx + 2] = SrgbToLinear(row[x].B / 255f);
                }
            }
        });
        
        FilePath = path;
        Console.WriteLine($"Loaded environment (converted to HDR): {path} ({_width}x{_height})");
        return true;
    }

    /// <summary>
    /// Sample the environment map using a direction vector.
    /// Uses equirectangular mapping.
    /// </summary>
    public Vector3 Sample(Vector3 direction)
    {
        if (!IsLoaded)
            return Vector3.Zero;
        
        // Apply rotation
        if (MathF.Abs(_rotation) > 0.0001f)
        {
            float cos = MathF.Cos(_rotation);
            float sin = MathF.Sin(_rotation);
            float x = direction.X * cos - direction.Z * sin;
            float z = direction.X * sin + direction.Z * cos;
            direction = new Vector3(x, direction.Y, z);
        }
        
        // Convert direction to equirectangular UV
        direction = Vector3.Normalize(direction);
        float u = MathF.Atan2(direction.Z, direction.X) / (2f * MathF.PI) + 0.5f;
        float v = MathF.Asin(Math.Clamp(direction.Y, -1f, 1f)) / MathF.PI + 0.5f;
        
        // Bilinear sample
        return SampleBilinear(u, v) * _intensity;
    }

    /// <summary>
    /// Sample with bilinear filtering
    /// </summary>
    private Vector3 SampleBilinear(float u, float v)
    {
        // Wrap U, clamp V
        u = u - MathF.Floor(u);
        v = Math.Clamp(v, 0f, 1f);
        
        float fx = u * (_width - 1);
        float fy = v * (_height - 1);
        
        int x0 = (int)fx;
        int y0 = (int)fy;
        int x1 = (x0 + 1) % _width;
        int y1 = Math.Min(y0 + 1, _height - 1);
        
        float tx = fx - x0;
        float ty = fy - y0;
        
        var c00 = GetPixel(x0, y0);
        var c10 = GetPixel(x1, y0);
        var c01 = GetPixel(x0, y1);
        var c11 = GetPixel(x1, y1);
        
        var c0 = Vector3.Lerp(c00, c10, tx);
        var c1 = Vector3.Lerp(c01, c11, tx);
        
        return Vector3.Lerp(c0, c1, ty);
    }

    /// <summary>
    /// Get pixel at integer coordinates
    /// </summary>
    private Vector3 GetPixel(int x, int y)
    {
        int idx = (y * _width + x) * 3;
        return new Vector3(_hdrData[idx], _hdrData[idx + 1], _hdrData[idx + 2]);
    }

    /// <summary>
    /// Convert sRGB to linear color space
    /// </summary>
    private static float SrgbToLinear(float c)
    {
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    /// <summary>
    /// Create a default procedural sky environment
    /// </summary>
    public static EnvironmentMap CreateProceduralSky(int width = 512, int height = 256)
    {
        var env = new EnvironmentMap();
        env._width = width;
        env._height = height;
        env._hdrData = new float[width * height * 3];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Convert to direction
                float u = (float)x / (width - 1);
                float v = (float)y / (height - 1);
                
                float theta = u * 2f * MathF.PI;
                float phi = v * MathF.PI;
                
                Vector3 dir = new Vector3(
                    MathF.Sin(phi) * MathF.Cos(theta),
                    MathF.Cos(phi),
                    MathF.Sin(phi) * MathF.Sin(theta)
                );
                
                // Simple sky gradient
                float skyFactor = dir.Y * 0.5f + 0.5f;
                
                // Ground color
                Vector3 groundColor = new Vector3(0.1f, 0.08f, 0.06f);
                // Horizon color
                Vector3 horizonColor = new Vector3(0.6f, 0.7f, 0.9f);
                // Sky color
                Vector3 skyColor = new Vector3(0.2f, 0.4f, 0.8f);
                
                Vector3 color;
                if (dir.Y < 0)
                {
                    // Below horizon
                    float t = -dir.Y;
                    color = Vector3.Lerp(horizonColor, groundColor, t);
                }
                else
                {
                    // Above horizon
                    color = Vector3.Lerp(horizonColor, skyColor, skyFactor);
                }
                
                // Add sun
                Vector3 sunDir = Vector3.Normalize(new Vector3(0.5f, 0.5f, 0.5f));
                float sunDot = Vector3.Dot(dir, sunDir);
                if (sunDot > 0.995f)
                {
                    // Sun disc
                    color += new Vector3(50f, 45f, 40f);
                }
                else if (sunDot > 0.9f)
                {
                    // Sun glow
                    float glow = (sunDot - 0.9f) / 0.095f;
                    color += new Vector3(2f, 1.5f, 1f) * glow * glow;
                }
                
                int idx = (y * width + x) * 3;
                env._hdrData[idx] = color.X;
                env._hdrData[idx + 1] = color.Y;
                env._hdrData[idx + 2] = color.Z;
            }
        }
        
        env.FilePath = "[Procedural Sky]";
        return env;
    }

    /// <summary>
    /// Get RGB16F data for GPU upload (6 bytes per pixel)
    /// </summary>
    public ushort[] GetRgb16fData()
    {
        var data = new ushort[_width * _height * 3];
        
        for (int i = 0; i < _hdrData.Length; i++)
        {
            data[i] = FloatToHalf(_hdrData[i]);
        }
        
        return data;
    }

    /// <summary>
    /// Get RGBA16F data for GPU upload (8 bytes per pixel)
    /// </summary>
    public ushort[] GetRgba16fData()
    {
        var data = new ushort[_width * _height * 4];
        
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                int srcIdx = (y * _width + x) * 3;
                int dstIdx = (y * _width + x) * 4;
                
                data[dstIdx] = FloatToHalf(_hdrData[srcIdx]);
                data[dstIdx + 1] = FloatToHalf(_hdrData[srcIdx + 1]);
                data[dstIdx + 2] = FloatToHalf(_hdrData[srcIdx + 2]);
                data[dstIdx + 3] = FloatToHalf(1f); // Alpha
            }
        }
        
        return data;
    }

    /// <summary>
    /// Convert float to half-precision (IEEE 754 binary16)
    /// </summary>
    private static ushort FloatToHalf(float value)
    {
        // Handle special cases
        if (float.IsNaN(value)) return 0x7E00;
        if (float.IsPositiveInfinity(value)) return 0x7C00;
        if (float.IsNegativeInfinity(value)) return 0xFC00;
        
        uint bits = BitConverter.SingleToUInt32Bits(value);
        uint sign = (bits >> 16) & 0x8000;
        int exp = (int)((bits >> 23) & 0xFF) - 127 + 15;
        uint mantissa = bits & 0x7FFFFF;
        
        if (exp <= 0)
        {
            // Denormalized or zero
            if (exp < -10) return (ushort)sign;
            mantissa |= 0x800000;
            int shift = 14 - exp;
            mantissa >>= shift;
            return (ushort)(sign | mantissa);
        }
        else if (exp >= 31)
        {
            // Overflow to infinity
            return (ushort)(sign | 0x7C00);
        }
        
        return (ushort)(sign | ((uint)exp << 10) | (mantissa >> 13));
    }

    public void Dispose()
    {
        _hdrData = Array.Empty<float>();
        _width = 0;
        _height = 0;
    }
}
