using System.Numerics;

namespace RedHoleEngine.Rendering.PBR;

/// <summary>
/// Represents a legacy material format for conversion to PBR.
/// Supports multiple legacy workflows: simple color, diffuse/specular, Phong, Lambert.
/// </summary>
public struct LegacyMaterial
{
    /// <summary>
    /// Material name (used for preset matching)
    /// </summary>
    public string? Name;
    
    // Simple color material
    public Vector4? Color;
    
    // Diffuse/Specular workflow
    public Vector3? DiffuseColor;
    public Vector3? SpecularColor;
    public float? Shininess;        // Phong exponent (1-1000+)
    public float? SpecularPower;    // Alternative name for shininess
    
    // Lambert
    public Vector3? AmbientColor;
    
    // Emission
    public Vector3? EmissiveColor;
    public float? EmissiveIntensity;
    
    // Transparency
    public float? Opacity;
    public float? Transparency;     // Inverse of opacity
    
    // Bump/Normal (for reference, not converted to textures)
    public float? BumpScale;
    
    // Reflection
    public float? Reflectivity;
    public Vector3? ReflectionColor;
    
    // IOR
    public float? IndexOfRefraction;
    
    /// <summary>
    /// Create from a simple RGBA color
    /// </summary>
    public static LegacyMaterial FromColor(Vector4 color, string? name = null) => new()
    {
        Name = name,
        Color = color
    };
    
    /// <summary>
    /// Create from a simple RGB color
    /// </summary>
    public static LegacyMaterial FromColor(Vector3 color, string? name = null) => new()
    {
        Name = name,
        Color = new Vector4(color, 1f)
    };
    
    /// <summary>
    /// Create from diffuse/specular workflow
    /// </summary>
    public static LegacyMaterial FromDiffuseSpecular(
        Vector3 diffuse, 
        Vector3 specular, 
        float shininess,
        string? name = null) => new()
    {
        Name = name,
        DiffuseColor = diffuse,
        SpecularColor = specular,
        Shininess = shininess
    };
    
    /// <summary>
    /// Create from MaterialComponent
    /// </summary>
    public static LegacyMaterial FromMaterialComponent(Components.MaterialComponent mat) => new()
    {
        Color = mat.BaseColor,
        EmissiveColor = mat.EmissiveColor,
        // Note: MaterialComponent already has Metallic/Roughness, but we treat it as legacy
        // to allow re-inference with smarter heuristics
        Shininess = mat.Roughness > 0 ? (1f - mat.Roughness) * 100f : null,
        Reflectivity = mat.Metallic
    };
}

/// <summary>
/// Converts legacy materials to PBR materials using intelligent heuristics.
/// </summary>
public static class MaterialConverter
{
    // Known metallic material colors (sRGB, approximate)
    private static readonly (string Name, Vector3 Color, float DefaultRoughness)[] KnownMetals =
    {
        ("Gold", new Vector3(1.000f, 0.766f, 0.336f), 0.20f),
        ("Silver", new Vector3(0.972f, 0.960f, 0.915f), 0.15f),
        ("Copper", new Vector3(0.955f, 0.638f, 0.538f), 0.25f),
        ("Iron", new Vector3(0.560f, 0.570f, 0.580f), 0.40f),
        ("Steel", new Vector3(0.560f, 0.570f, 0.580f), 0.35f),
        ("Aluminum", new Vector3(0.913f, 0.922f, 0.924f), 0.30f),
        ("Titanium", new Vector3(0.542f, 0.497f, 0.449f), 0.35f),
        ("Brass", new Vector3(0.887f, 0.789f, 0.434f), 0.30f),
        ("Bronze", new Vector3(0.714f, 0.428f, 0.181f), 0.35f),
        ("Chromium", new Vector3(0.550f, 0.556f, 0.554f), 0.10f),
        ("Nickel", new Vector3(0.660f, 0.609f, 0.526f), 0.25f),
        ("Platinum", new Vector3(0.672f, 0.637f, 0.585f), 0.20f),
        ("Zinc", new Vector3(0.664f, 0.824f, 0.850f), 0.40f),
        ("Mercury", new Vector3(0.781f, 0.780f, 0.778f), 0.05f),
    };

    // Named material presets
    private static readonly Dictionary<string, Func<PbrMaterial>> NamedPresets = new(StringComparer.OrdinalIgnoreCase)
    {
        // Metals
        ["gold"] = () => PbrMaterial.Gold(),
        ["silver"] = () => PbrMaterial.Metal(new Vector3(0.972f, 0.960f, 0.915f), 0.15f),
        ["copper"] = () => PbrMaterial.Copper(),
        ["iron"] = () => PbrMaterial.Iron(),
        ["steel"] = () => PbrMaterial.Iron(0.35f),
        ["aluminum"] = () => PbrMaterial.Metal(new Vector3(0.913f, 0.922f, 0.924f), 0.30f),
        ["aluminium"] = () => PbrMaterial.Metal(new Vector3(0.913f, 0.922f, 0.924f), 0.30f),
        ["brass"] = () => PbrMaterial.Metal(new Vector3(0.887f, 0.789f, 0.434f), 0.30f),
        ["bronze"] = () => PbrMaterial.Metal(new Vector3(0.714f, 0.428f, 0.181f), 0.35f),
        ["chrome"] = () => PbrMaterial.Metal(new Vector3(0.550f, 0.556f, 0.554f), 0.10f),
        ["chromium"] = () => PbrMaterial.Metal(new Vector3(0.550f, 0.556f, 0.554f), 0.10f),
        ["nickel"] = () => PbrMaterial.Metal(new Vector3(0.660f, 0.609f, 0.526f), 0.25f),
        ["platinum"] = () => PbrMaterial.Metal(new Vector3(0.672f, 0.637f, 0.585f), 0.20f),
        ["titanium"] = () => PbrMaterial.Metal(new Vector3(0.542f, 0.497f, 0.449f), 0.35f),
        
        // Non-metals
        ["glass"] = () => PbrMaterial.Glass(),
        ["water"] = () => PbrMaterial.Glass(0.0f, 1.333f),
        ["diamond"] = () => PbrMaterial.Glass(0.0f, 2.42f),
        ["crystal"] = () => PbrMaterial.Glass(0.0f, 1.55f),
        
        // Plastics
        ["plastic"] = () => PbrMaterial.Plastic(Vector3.One, 0.4f),
        ["rubber"] = () => PbrMaterial.Plastic(new Vector3(0.1f, 0.1f, 0.1f), 0.9f),
        
        // Organics
        ["wood"] = () => new PbrMaterial { Name = "Wood", BaseColorFactor = new Vector4(0.55f, 0.35f, 0.2f, 1f), MetallicFactor = 0f, RoughnessFactor = 0.7f },
        ["skin"] = () => new PbrMaterial { Name = "Skin", BaseColorFactor = new Vector4(0.9f, 0.7f, 0.6f, 1f), MetallicFactor = 0f, RoughnessFactor = 0.5f, SubsurfaceFactor = 0.3f },
        ["leather"] = () => new PbrMaterial { Name = "Leather", BaseColorFactor = new Vector4(0.4f, 0.25f, 0.15f, 1f), MetallicFactor = 0f, RoughnessFactor = 0.6f },
        ["fabric"] = () => new PbrMaterial { Name = "Fabric", BaseColorFactor = Vector4.One, MetallicFactor = 0f, RoughnessFactor = 0.9f },
        ["cloth"] = () => new PbrMaterial { Name = "Cloth", BaseColorFactor = Vector4.One, MetallicFactor = 0f, RoughnessFactor = 0.9f },
        
        // Minerals
        ["concrete"] = () => new PbrMaterial { Name = "Concrete", BaseColorFactor = new Vector4(0.5f, 0.5f, 0.5f, 1f), MetallicFactor = 0f, RoughnessFactor = 0.9f },
        ["stone"] = () => new PbrMaterial { Name = "Stone", BaseColorFactor = new Vector4(0.45f, 0.45f, 0.45f, 1f), MetallicFactor = 0f, RoughnessFactor = 0.8f },
        ["marble"] = () => new PbrMaterial { Name = "Marble", BaseColorFactor = new Vector4(0.95f, 0.95f, 0.95f, 1f), MetallicFactor = 0f, RoughnessFactor = 0.2f, SubsurfaceFactor = 0.1f },
        ["brick"] = () => new PbrMaterial { Name = "Brick", BaseColorFactor = new Vector4(0.65f, 0.25f, 0.2f, 1f), MetallicFactor = 0f, RoughnessFactor = 0.85f },
        
        // Special
        ["mirror"] = () => PbrMaterial.Metal(Vector3.One, 0.0f),
        ["car"] = () => PbrMaterial.CarPaint(new Vector3(0.8f, 0.1f, 0.1f)),
        ["carpaint"] = () => PbrMaterial.CarPaint(new Vector3(0.8f, 0.1f, 0.1f)),
        ["car_paint"] = () => PbrMaterial.CarPaint(new Vector3(0.8f, 0.1f, 0.1f)),
        ["emissive"] = () => PbrMaterial.Emissive(Vector3.One, 5f),
        ["light"] = () => PbrMaterial.Emissive(Vector3.One, 10f),
    };

    /// <summary>
    /// Convert a legacy material to PBR using all available heuristics
    /// </summary>
    public static PbrMaterial Convert(LegacyMaterial legacy, ConversionOptions? options = null)
    {
        options ??= ConversionOptions.Default;
        
        // 1. Try name-based preset matching first
        if (!string.IsNullOrEmpty(legacy.Name) && options.UseNameMatching)
        {
            var preset = TryMatchNamedPreset(legacy.Name);
            if (preset != null)
            {
                // Apply any color overrides from the legacy material
                if (legacy.Color.HasValue || legacy.DiffuseColor.HasValue)
                {
                    var color = legacy.Color ?? new Vector4(legacy.DiffuseColor!.Value, 1f);
                    preset.BaseColorFactor = color;
                }
                if (legacy.EmissiveColor.HasValue)
                {
                    preset.EmissiveFactor = legacy.EmissiveColor.Value;
                    preset.EmissiveIntensity = legacy.EmissiveIntensity ?? 1f;
                }
                return preset;
            }
        }
        
        // 2. Determine base color
        Vector4 baseColor = DetermineBaseColor(legacy);
        
        // 3. Detect if material is metallic based on color
        bool isMetallic = false;
        float metallicFactor = 0f;
        float roughness = 0.5f;
        
        if (options.UseColorBasedDetection)
        {
            var (detectedMetal, metalRoughness) = DetectMetalFromColor(baseColor);
            if (detectedMetal)
            {
                isMetallic = true;
                metallicFactor = 1f;
                roughness = metalRoughness;
            }
        }
        
        // 4. Use specular/shininess to refine metallic and roughness
        if (legacy.SpecularColor.HasValue && legacy.Shininess.HasValue)
        {
            var (metal, rough) = ConvertSpecularToMetallicRoughness(
                legacy.DiffuseColor ?? Vector3.One,
                legacy.SpecularColor.Value,
                legacy.Shininess.Value);
            
            // Blend with color-based detection
            if (!isMetallic)
            {
                metallicFactor = metal;
                roughness = rough;
                isMetallic = metallicFactor > 0.5f;
            }
        }
        else if (legacy.Shininess.HasValue && options.UseLuminanceBasedRoughness)
        {
            // Convert shininess to roughness
            roughness = ShininessToRoughness(legacy.Shininess.Value);
        }
        else if (legacy.Reflectivity.HasValue)
        {
            // Use reflectivity to infer metallic/roughness
            metallicFactor = legacy.Reflectivity.Value;
            isMetallic = metallicFactor > 0.5f;
            roughness = 1f - legacy.Reflectivity.Value * 0.5f; // More reflective = smoother
        }
        else if (options.UseLuminanceBasedRoughness)
        {
            // Estimate roughness from color characteristics
            roughness = EstimateRoughnessFromColor(baseColor);
        }
        
        // 5. Build the PBR material
        var pbr = new PbrMaterial
        {
            Name = legacy.Name ?? "Converted Material",
            BaseColorFactor = baseColor,
            MetallicFactor = metallicFactor,
            RoughnessFactor = Math.Clamp(roughness, 0.04f, 1f), // Minimum roughness for stability
        };
        
        // 6. Handle emission
        if (legacy.EmissiveColor.HasValue)
        {
            pbr.EmissiveFactor = legacy.EmissiveColor.Value;
            pbr.EmissiveIntensity = legacy.EmissiveIntensity ?? 1f;
        }
        
        // 7. Handle transparency
        if (legacy.Opacity.HasValue)
        {
            pbr.BaseColorFactor = new Vector4(
                pbr.BaseColorFactor.X,
                pbr.BaseColorFactor.Y,
                pbr.BaseColorFactor.Z,
                legacy.Opacity.Value);
            pbr.AlphaMode = legacy.Opacity.Value < 1f ? AlphaMode.Blend : AlphaMode.Opaque;
        }
        else if (legacy.Transparency.HasValue)
        {
            float opacity = 1f - legacy.Transparency.Value;
            pbr.BaseColorFactor = new Vector4(
                pbr.BaseColorFactor.X,
                pbr.BaseColorFactor.Y,
                pbr.BaseColorFactor.Z,
                opacity);
            pbr.AlphaMode = opacity < 1f ? AlphaMode.Blend : AlphaMode.Opaque;
        }
        
        // 8. Handle IOR
        if (legacy.IndexOfRefraction.HasValue)
        {
            pbr.IndexOfRefraction = legacy.IndexOfRefraction.Value;
        }
        
        return pbr;
    }

    /// <summary>
    /// Convert a MaterialComponent to a full PbrMaterial
    /// </summary>
    public static PbrMaterial ConvertMaterialComponent(Components.MaterialComponent mat, string? name = null)
    {
        // If it already references a PBR material, we can't convert it here
        if (mat.UsesPbrMaterial)
        {
            throw new InvalidOperationException("MaterialComponent already uses a PBR material. Access it via MaterialLibrary.");
        }
        
        var legacy = LegacyMaterial.FromMaterialComponent(mat);
        legacy.Name = name;
        
        return Convert(legacy);
    }

    /// <summary>
    /// Batch convert multiple legacy materials
    /// </summary>
    public static IEnumerable<PbrMaterial> ConvertBatch(IEnumerable<LegacyMaterial> materials, ConversionOptions? options = null)
    {
        return materials.Select(m => Convert(m, options));
    }

    #region Heuristic Helpers

    private static PbrMaterial? TryMatchNamedPreset(string name)
    {
        // Direct match
        if (NamedPresets.TryGetValue(name, out var factory))
        {
            var mat = factory();
            mat.Name = name;
            return mat;
        }
        
        // Try partial matching (e.g., "Gold_Material" -> "Gold")
        foreach (var (presetName, presetFactory) in NamedPresets)
        {
            if (name.Contains(presetName, StringComparison.OrdinalIgnoreCase))
            {
                var mat = presetFactory();
                mat.Name = name;
                return mat;
            }
        }
        
        return null;
    }

    private static Vector4 DetermineBaseColor(LegacyMaterial legacy)
    {
        if (legacy.Color.HasValue)
            return legacy.Color.Value;
        
        if (legacy.DiffuseColor.HasValue)
            return new Vector4(legacy.DiffuseColor.Value, legacy.Opacity ?? 1f);
        
        if (legacy.AmbientColor.HasValue)
            return new Vector4(legacy.AmbientColor.Value, legacy.Opacity ?? 1f);
        
        return Vector4.One;
    }

    private static (bool IsMetal, float Roughness) DetectMetalFromColor(Vector4 color)
    {
        Vector3 rgb = new(color.X, color.Y, color.Z);
        
        // Find the closest known metal color
        float bestDistance = float.MaxValue;
        string? bestMatch = null;
        float bestRoughness = 0.3f;
        
        foreach (var (name, metalColor, roughness) in KnownMetals)
        {
            float distance = Vector3.Distance(rgb, metalColor);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestMatch = name;
                bestRoughness = roughness;
            }
        }
        
        // Threshold for considering it a metal (color distance in sRGB space)
        const float metalThreshold = 0.15f;
        
        if (bestDistance < metalThreshold)
        {
            return (true, bestRoughness);
        }
        
        // Additional heuristic: high saturation warm colors might be metals
        float max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        float min = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        float saturation = max > 0 ? (max - min) / max : 0;
        float luminance = 0.299f * rgb.X + 0.587f * rgb.Y + 0.114f * rgb.Z;
        
        // Metals tend to be bright, slightly saturated, and warm-tinted
        bool looksMetallic = luminance > 0.4f && 
                            saturation > 0.1f && saturation < 0.7f &&
                            rgb.X >= rgb.Z; // Warm tint
        
        if (looksMetallic && bestDistance < 0.3f)
        {
            return (true, bestRoughness);
        }
        
        return (false, 0.5f);
    }

    private static (float Metallic, float Roughness) ConvertSpecularToMetallicRoughness(
        Vector3 diffuse, 
        Vector3 specular, 
        float shininess)
    {
        // Based on the approach from Marmoset/Substance and glTF spec
        // https://docs.substance3d.com/sddoc/specular-to-metalness-166496167.html
        
        float specLuminance = 0.299f * specular.X + 0.587f * specular.Y + 0.114f * specular.Z;
        float diffLuminance = 0.299f * diffuse.X + 0.587f * diffuse.Y + 0.114f * diffuse.Z;
        
        // Dielectrics typically have specular luminance around 0.04 (F0 for IOR 1.5)
        // Metals have high specular that matches their diffuse color
        
        // Estimate metallic based on specular intensity
        float metallic = 0f;
        if (specLuminance > 0.04f)
        {
            // Higher specular = more metallic
            metallic = Math.Clamp((specLuminance - 0.04f) / 0.96f, 0f, 1f);
            
            // Also check if specular color matches diffuse (characteristic of metals)
            float colorMatch = 1f - Vector3.Distance(
                Vector3.Normalize(specular + new Vector3(0.001f)),
                Vector3.Normalize(diffuse + new Vector3(0.001f)));
            
            if (diffLuminance > 0.01f && colorMatch > 0.8f)
            {
                metallic = Math.Max(metallic, colorMatch);
            }
        }
        
        // Convert shininess to roughness
        float roughness = ShininessToRoughness(shininess);
        
        return (metallic, roughness);
    }

    private static float ShininessToRoughness(float shininess)
    {
        // Common conversion: roughness = sqrt(2 / (shininess + 2))
        // This maps Phong exponent to GGX roughness
        // Shininess 1 -> roughness ~0.82
        // Shininess 10 -> roughness ~0.41
        // Shininess 100 -> roughness ~0.14
        // Shininess 1000 -> roughness ~0.045
        
        shininess = Math.Max(1f, shininess);
        float roughness = MathF.Sqrt(2f / (shininess + 2f));
        return Math.Clamp(roughness, 0.04f, 1f);
    }

    private static float EstimateRoughnessFromColor(Vector4 color)
    {
        // Heuristic: brighter, more saturated colors tend to be smoother
        Vector3 rgb = new(color.X, color.Y, color.Z);
        
        float luminance = 0.299f * rgb.X + 0.587f * rgb.Y + 0.114f * rgb.Z;
        float max = Math.Max(rgb.X, Math.Max(rgb.Y, rgb.Z));
        float min = Math.Min(rgb.X, Math.Min(rgb.Y, rgb.Z));
        float saturation = max > 0 ? (max - min) / max : 0;
        
        // Dark colors -> rough (like charcoal, rubber)
        // Bright, saturated colors -> medium roughness (like plastic)
        // Bright, desaturated colors -> can be smooth (like ceramic) or rough (like chalk)
        
        float roughness = 0.5f;
        
        if (luminance < 0.2f)
        {
            // Dark materials tend to be rough
            roughness = 0.7f + (0.2f - luminance) * 0.5f;
        }
        else if (saturation > 0.5f)
        {
            // Saturated colors might be plastic-like
            roughness = 0.3f + (1f - saturation) * 0.2f;
        }
        else
        {
            // Default: moderate roughness based on luminance
            roughness = 0.6f - luminance * 0.2f;
        }
        
        return Math.Clamp(roughness, 0.1f, 0.9f);
    }

    #endregion
}

/// <summary>
/// Options for material conversion
/// </summary>
public class ConversionOptions
{
    /// <summary>
    /// Use color-based metal detection
    /// </summary>
    public bool UseColorBasedDetection { get; set; } = true;
    
    /// <summary>
    /// Use luminance-based roughness estimation when no shininess is provided
    /// </summary>
    public bool UseLuminanceBasedRoughness { get; set; } = true;
    
    /// <summary>
    /// Match material names to known presets
    /// </summary>
    public bool UseNameMatching { get; set; } = true;
    
    /// <summary>
    /// Default options with all heuristics enabled
    /// </summary>
    public static ConversionOptions Default => new();
    
    /// <summary>
    /// Minimal conversion with no heuristics
    /// </summary>
    public static ConversionOptions Minimal => new()
    {
        UseColorBasedDetection = false,
        UseLuminanceBasedRoughness = false,
        UseNameMatching = false
    };
}
