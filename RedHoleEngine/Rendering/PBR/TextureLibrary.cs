using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace RedHoleEngine.Rendering.PBR;

/// <summary>
/// Represents a loaded texture ready for GPU upload
/// </summary>
public class PbrTexture : IDisposable
{
    /// <summary>
    /// Unique index in the texture array
    /// </summary>
    public int Index { get; internal set; } = -1;
    
    /// <summary>
    /// Original file path
    /// </summary>
    public string Path { get; }
    
    /// <summary>
    /// Texture width in pixels
    /// </summary>
    public int Width { get; }
    
    /// <summary>
    /// Texture height in pixels
    /// </summary>
    public int Height { get; }
    
    /// <summary>
    /// RGBA pixel data (4 bytes per pixel)
    /// </summary>
    public byte[] PixelData { get; }
    
    /// <summary>
    /// Whether this texture uses sRGB color space (true for albedo/emissive, false for normal/roughness)
    /// </summary>
    public bool IsSrgb { get; }

    public PbrTexture(string path, int width, int height, byte[] pixelData, bool isSrgb)
    {
        Path = path;
        Width = width;
        Height = height;
        PixelData = pixelData;
        IsSrgb = isSrgb;
    }

    public void Dispose()
    {
        // Pixel data is managed, no explicit cleanup needed
    }
}

/// <summary>
/// Texture type for proper color space handling
/// </summary>
public enum PbrTextureType
{
    /// <summary>
    /// Base color / Albedo - sRGB color space
    /// </summary>
    BaseColor,
    
    /// <summary>
    /// Metallic-Roughness - Linear (G=roughness, B=metallic)
    /// </summary>
    MetallicRoughness,
    
    /// <summary>
    /// Normal map - Linear, tangent space
    /// </summary>
    Normal,
    
    /// <summary>
    /// Ambient occlusion - Linear (R channel)
    /// </summary>
    Occlusion,
    
    /// <summary>
    /// Emissive - sRGB color space
    /// </summary>
    Emissive
}

/// <summary>
/// Manages PBR textures for materials.
/// Loads textures from disk and prepares them for GPU upload.
/// </summary>
public class TextureLibrary : IDisposable
{
    private readonly Dictionary<string, PbrTexture> _texturesByPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PbrTexture> _textures = new();
    private bool _isDirty = true;
    
    /// <summary>
    /// Maximum texture dimension (textures larger than this will be resized)
    /// </summary>
    public int MaxTextureSize { get; set; } = 2048;
    
    /// <summary>
    /// Number of loaded textures
    /// </summary>
    public int Count => _textures.Count;
    
    /// <summary>
    /// Whether the library has changed since last GPU upload
    /// </summary>
    public bool IsDirty => _isDirty;

    /// <summary>
    /// Get all loaded textures
    /// </summary>
    public IReadOnlyList<PbrTexture> Textures => _textures;

    /// <summary>
    /// Load a texture from file, or return existing if already loaded
    /// </summary>
    public PbrTexture? LoadTexture(string path, PbrTextureType type)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        // Check if already loaded
        if (_texturesByPath.TryGetValue(path, out var existing))
            return existing;

        // Try to load from disk
        if (!File.Exists(path))
        {
            Console.WriteLine($"Texture not found: {path}");
            return null;
        }

        try
        {
            using var image = Image.Load<Rgba32>(path);
            
            // Resize if too large
            if (image.Width > MaxTextureSize || image.Height > MaxTextureSize)
            {
                float scale = Math.Min(
                    (float)MaxTextureSize / image.Width,
                    (float)MaxTextureSize / image.Height);
                int newWidth = (int)(image.Width * scale);
                int newHeight = (int)(image.Height * scale);
                image.Mutate(x => x.Resize(newWidth, newHeight));
            }

            // Extract pixel data
            var pixelData = new byte[image.Width * image.Height * 4];
            image.CopyPixelDataTo(pixelData);

            bool isSrgb = type == PbrTextureType.BaseColor || type == PbrTextureType.Emissive;
            var texture = new PbrTexture(path, image.Width, image.Height, pixelData, isSrgb);
            
            // Assign index and register
            texture.Index = _textures.Count;
            _textures.Add(texture);
            _texturesByPath[path] = texture;
            _isDirty = true;

            Console.WriteLine($"Loaded texture: {path} ({image.Width}x{image.Height})");
            return texture;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load texture {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Get texture index for a path, loading if necessary
    /// </summary>
    public int GetTextureIndex(string? path, PbrTextureType type)
    {
        if (string.IsNullOrEmpty(path))
            return -1;

        var texture = LoadTexture(path, type);
        return texture?.Index ?? -1;
    }

    /// <summary>
    /// Create a texture index resolver function for use with GpuMaterial
    /// </summary>
    public Func<string?, int> CreateResolver(PbrTextureType type)
    {
        return path => GetTextureIndex(path, type);
    }

    /// <summary>
    /// Get texture by index
    /// </summary>
    public PbrTexture? GetTexture(int index)
    {
        if (index < 0 || index >= _textures.Count)
            return null;
        return _textures[index];
    }

    /// <summary>
    /// Get texture by path
    /// </summary>
    public PbrTexture? GetTexture(string path)
    {
        _texturesByPath.TryGetValue(path, out var texture);
        return texture;
    }

    /// <summary>
    /// Preload all textures referenced by materials in a MaterialLibrary
    /// </summary>
    public void PreloadMaterialTextures(MaterialLibrary materialLibrary)
    {
        foreach (var material in materialLibrary.Materials)
        {
            if (!string.IsNullOrEmpty(material.BaseColorTexturePath))
                LoadTexture(material.BaseColorTexturePath, PbrTextureType.BaseColor);
            
            if (!string.IsNullOrEmpty(material.MetallicRoughnessTexturePath))
                LoadTexture(material.MetallicRoughnessTexturePath, PbrTextureType.MetallicRoughness);
            
            if (!string.IsNullOrEmpty(material.NormalTexturePath))
                LoadTexture(material.NormalTexturePath, PbrTextureType.Normal);
            
            if (!string.IsNullOrEmpty(material.OcclusionTexturePath))
                LoadTexture(material.OcclusionTexturePath, PbrTextureType.Occlusion);
            
            if (!string.IsNullOrEmpty(material.EmissiveTexturePath))
                LoadTexture(material.EmissiveTexturePath, PbrTextureType.Emissive);
        }
    }

    /// <summary>
    /// Mark library as synced with GPU
    /// </summary>
    public void MarkClean()
    {
        _isDirty = false;
    }

    /// <summary>
    /// Clear all loaded textures
    /// </summary>
    public void Clear()
    {
        foreach (var texture in _textures)
        {
            texture.Dispose();
        }
        _textures.Clear();
        _texturesByPath.Clear();
        _isDirty = true;
    }

    public void Dispose()
    {
        Clear();
    }
}
