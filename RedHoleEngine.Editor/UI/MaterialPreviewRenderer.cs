using System;
using System.Numerics;
using System.Threading.Tasks;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Editor.UI;

/// <summary>
/// CPU-based material preview renderer that renders a sphere with PBR shading.
/// Used for real-time material preview in the editor.
/// </summary>
public class MaterialPreviewRenderer
{
    private readonly int _width;
    private readonly int _height;
    private readonly byte[] _buffer;
    private readonly float[] _depthBuffer;
    
    // Camera setup
    private Vector3 _cameraPosition = new(0, 0, 3f);
    private float _sphereRadius = 1f;
    private Vector3 _sphereCenter = Vector3.Zero;
    
    // Rotation for interactive preview
    private float _rotationY;
    private float _rotationX;
    
    // Environment map for IBL
    private EnvironmentMap? _environmentMap;
    
    // Lighting setup (3-point lighting)
    private static readonly LightInfo[] Lights = {
        new(new Vector3(2f, 2f, 3f), new Vector3(1f, 0.98f, 0.95f), 3f),   // Key light (warm)
        new(new Vector3(-2f, 1f, 2f), new Vector3(0.7f, 0.8f, 1f), 1.5f),  // Fill light (cool)
        new(new Vector3(0f, -1f, -2f), new Vector3(1f, 1f, 1f), 0.8f),     // Rim/back light
    };
    
    // Fallback colors when no environment map
    private static readonly Vector3 AmbientColor = new(0.03f, 0.03f, 0.04f);
    private static readonly Vector3 GroundColor = new(0.15f, 0.12f, 0.1f);
    private static readonly Vector3 SkyColor = new(0.4f, 0.5f, 0.7f);
    
    public MaterialPreviewRenderer(int width, int height)
    {
        _width = width;
        _height = height;
        _buffer = new byte[width * height * 4];
        _depthBuffer = new float[width * height];
    }
    
    public int Width => _width;
    public int Height => _height;
    public byte[] Buffer => _buffer;
    
    /// <summary>
    /// Set the rotation for interactive preview
    /// </summary>
    public void SetRotation(float rotationX, float rotationY)
    {
        _rotationX = rotationX;
        _rotationY = rotationY;
    }
    
    /// <summary>
    /// Set the environment map for IBL (optional)
    /// </summary>
    public void SetEnvironmentMap(EnvironmentMap? envMap)
    {
        _environmentMap = envMap;
    }
    
    /// <summary>
    /// Render the material preview sphere
    /// </summary>
    public void Render(PbrMaterial material)
    {
        // Clear buffers
        Array.Fill(_depthBuffer, float.MaxValue);
        
        float aspectRatio = (float)_width / _height;
        float fov = 45f * MathF.PI / 180f;
        float tanHalfFov = MathF.Tan(fov * 0.5f);
        
        // Apply rotation to camera
        var rotMatrix = Matrix4x4.CreateRotationY(_rotationY) * Matrix4x4.CreateRotationX(_rotationX);
        var rotatedCameraPos = Vector3.Transform(_cameraPosition, rotMatrix);
        
        // Parallel rendering for performance
        Parallel.For(0, _height, y =>
        {
            for (int x = 0; x < _width; x++)
            {
                // Calculate ray direction (simple perspective camera)
                float u = (2f * (x + 0.5f) / _width - 1f) * aspectRatio * tanHalfFov;
                float v = (1f - 2f * (y + 0.5f) / _height) * tanHalfFov;
                
                var rayDir = Vector3.Normalize(new Vector3(u, v, -1f));
                rayDir = Vector3.TransformNormal(rayDir, rotMatrix);
                
                var color = TraceRay(rotatedCameraPos, rayDir, material);
                
                // Tone mapping (simple Reinhard)
                color = color / (color + Vector3.One);
                
                // Gamma correction
                color = new Vector3(
                    MathF.Pow(color.X, 1f / 2.2f),
                    MathF.Pow(color.Y, 1f / 2.2f),
                    MathF.Pow(color.Z, 1f / 2.2f));
                
                // Write to buffer
                int idx = (y * _width + x) * 4;
                _buffer[idx] = (byte)(Math.Clamp(color.X, 0f, 1f) * 255);
                _buffer[idx + 1] = (byte)(Math.Clamp(color.Y, 0f, 1f) * 255);
                _buffer[idx + 2] = (byte)(Math.Clamp(color.Z, 0f, 1f) * 255);
                _buffer[idx + 3] = 255;
            }
        });
    }
    
    private Vector3 TraceRay(Vector3 rayOrigin, Vector3 rayDir, PbrMaterial material)
    {
        // Ray-sphere intersection
        if (IntersectSphere(rayOrigin, rayDir, _sphereCenter, _sphereRadius, out float t))
        {
            var hitPos = rayOrigin + rayDir * t;
            var normal = Vector3.Normalize(hitPos - _sphereCenter);
            var viewDir = Vector3.Normalize(rayOrigin - hitPos);
            
            return ShadePBR(hitPos, normal, viewDir, material);
        }
        
        // Background - use environment map or gradient
        return SampleEnvironment(rayDir);
    }
    
    /// <summary>
    /// Sample the environment for a direction (IBL or fallback gradient)
    /// </summary>
    private Vector3 SampleEnvironment(Vector3 direction)
    {
        if (_environmentMap != null && _environmentMap.IsLoaded)
        {
            return _environmentMap.Sample(direction);
        }
        
        // Fallback gradient
        float skyFactor = direction.Y * 0.5f + 0.5f;
        return Vector3.Lerp(GroundColor, SkyColor, skyFactor);
    }
    
    /// <summary>
    /// Sample environment with roughness-based blur for specular IBL
    /// </summary>
    private Vector3 SampleEnvironmentRough(Vector3 direction, float roughness)
    {
        if (_environmentMap == null || !_environmentMap.IsLoaded || roughness < 0.1f)
        {
            return SampleEnvironment(direction);
        }
        
        // Simple blur approximation by sampling multiple directions
        Vector3 N = Vector3.Normalize(direction);
        Vector3 up = MathF.Abs(N.Y) < 0.999f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 T = Vector3.Normalize(Vector3.Cross(up, N));
        Vector3 B = Vector3.Cross(N, T);
        
        Vector3 result = Vector3.Zero;
        float coneAngle = roughness * 0.5f;
        float weights = 0f;
        
        const int NUM_SAMPLES = 4;
        for (int i = 0; i < NUM_SAMPLES; i++)
        {
            float angle = i / (float)NUM_SAMPLES * 2f * MathF.PI;
            float r = coneAngle * (0.5f + 0.5f * (i % 2));
            
            Vector3 offset = T * MathF.Cos(angle) * r + B * MathF.Sin(angle) * r;
            Vector3 sampleDir = Vector3.Normalize(N + offset);
            
            float w = 1f - offset.Length();
            result += SampleEnvironment(sampleDir) * w;
            weights += w;
        }
        
        result /= weights;
        return Vector3.Lerp(SampleEnvironment(direction), result, roughness);
    }
    
    private Vector3 ShadePBR(Vector3 position, Vector3 normal, Vector3 viewDir, PbrMaterial material)
    {
        var baseColor = new Vector3(material.BaseColorFactor.X, material.BaseColorFactor.Y, material.BaseColorFactor.Z);
        float metallic = material.MetallicFactor;
        float roughness = Math.Max(0.04f, material.RoughnessFactor); // Clamp to avoid divide by zero
        
        // Calculate F0 (reflectance at normal incidence)
        var dielectricF0 = new Vector3(0.04f);
        var f0 = Vector3.Lerp(dielectricF0, baseColor, metallic);
        
        // Diffuse color (metals have no diffuse)
        var diffuseColor = baseColor * (1f - metallic);
        
        var result = Vector3.Zero;
        
        // Add contribution from each light
        foreach (var light in Lights)
        {
            var lightDir = Vector3.Normalize(light.Position - position);
            var halfVec = Vector3.Normalize(viewDir + lightDir);
            
            float NdotL = Math.Max(0f, Vector3.Dot(normal, lightDir));
            float NdotV = Math.Max(0.001f, Vector3.Dot(normal, viewDir));
            float NdotH = Math.Max(0f, Vector3.Dot(normal, halfVec));
            float VdotH = Math.Max(0f, Vector3.Dot(viewDir, halfVec));
            
            if (NdotL > 0)
            {
                // Distance attenuation
                float dist = Vector3.Distance(light.Position, position);
                float attenuation = light.Intensity / (dist * dist);
                var radiance = light.Color * attenuation;
                
                // Cook-Torrance BRDF
                float D = DistributionGGX(NdotH, roughness);
                float G = GeometrySmith(NdotV, NdotL, roughness);
                var F = FresnelSchlick(VdotH, f0);
                
                var specular = (D * G * F) / (4f * NdotV * NdotL + 0.0001f);
                var kS = F;
                var kD = (Vector3.One - kS) * (1f - metallic);
                
                var diffuse = kD * diffuseColor / MathF.PI;
                
                result += (diffuse + specular) * radiance * NdotL;
            }
        }
        
        // IBL Ambient contribution
        float ambientOcclusion = 1f; // No AO texture for now
        
        // Fresnel for ambient
        float NdotV_ambient = Math.Max(0.001f, Vector3.Dot(normal, viewDir));
        var F_ambient = FresnelSchlickRoughness(NdotV_ambient, f0, roughness);
        var kS_ambient = F_ambient;
        var kD_ambient = (Vector3.One - kS_ambient) * (1f - metallic);
        
        // Diffuse IBL (irradiance from environment)
        var irradiance = SampleEnvironment(normal) * 0.3f;
        var ambientDiffuse = irradiance * diffuseColor * kD_ambient;
        
        // Specular IBL (reflection from environment)
        var R = Vector3.Reflect(-viewDir, normal);
        var prefilteredColor = SampleEnvironmentRough(R, roughness) * 0.5f;
        var ambientSpecular = prefilteredColor * F_ambient;
        
        result += (ambientDiffuse + ambientSpecular) * ambientOcclusion;
        
        // Emissive
        var emissive = material.EmissiveFactor * material.EmissiveIntensity;
        result += emissive;
        
        return result;
    }
    
    private static bool IntersectSphere(Vector3 ro, Vector3 rd, Vector3 center, float radius, out float t)
    {
        var oc = ro - center;
        float b = Vector3.Dot(oc, rd);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float discriminant = b * b - c;
        
        if (discriminant < 0)
        {
            t = 0;
            return false;
        }
        
        t = -b - MathF.Sqrt(discriminant);
        return t > 0;
    }
    
    // GGX/Trowbridge-Reitz normal distribution
    private static float DistributionGGX(float NdotH, float roughness)
    {
        float a = roughness * roughness;
        float a2 = a * a;
        float NdotH2 = NdotH * NdotH;
        
        float nom = a2;
        float denom = NdotH2 * (a2 - 1f) + 1f;
        denom = MathF.PI * denom * denom;
        
        return nom / Math.Max(denom, 0.0001f);
    }
    
    // Smith's Schlick-GGX geometry function
    private static float GeometrySmith(float NdotV, float NdotL, float roughness)
    {
        float r = roughness + 1f;
        float k = (r * r) / 8f;
        
        float ggx1 = NdotV / (NdotV * (1f - k) + k);
        float ggx2 = NdotL / (NdotL * (1f - k) + k);
        
        return ggx1 * ggx2;
    }
    
    // Schlick's Fresnel approximation
    private static Vector3 FresnelSchlick(float cosTheta, Vector3 f0)
    {
        float t = MathF.Pow(1f - cosTheta, 5f);
        return f0 + (Vector3.One - f0) * t;
    }
    
    // Fresnel with roughness factor (for IBL)
    private static Vector3 FresnelSchlickRoughness(float cosTheta, Vector3 f0, float roughness)
    {
        float t = MathF.Pow(1f - cosTheta, 5f);
        var maxReflect = new Vector3(Math.Max(1f - roughness, f0.X), Math.Max(1f - roughness, f0.Y), Math.Max(1f - roughness, f0.Z));
        return f0 + (maxReflect - f0) * t;
    }
    
    private readonly struct LightInfo
    {
        public readonly Vector3 Position;
        public readonly Vector3 Color;
        public readonly float Intensity;
        
        public LightInfo(Vector3 position, Vector3 color, float intensity)
        {
            Position = position;
            Color = color;
            Intensity = intensity;
        }
    }
}
