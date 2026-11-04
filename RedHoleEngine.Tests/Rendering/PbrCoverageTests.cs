using System.Numerics;
using System.Text;
using RedHoleEngine.Components;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Tests.Rendering;

public class PbrMaterialCoverageTests
{
    [Fact]
    public void Factories_SetWorkflowValues()
    {
        var metal = PbrMaterial.Metal(new Vector3(0.2f, 0.4f, 0.6f), 0.15f);
        Assert.Equal(1f, metal.MetallicFactor);
        Assert.Equal(0.15f, metal.RoughnessFactor);
        Assert.Equal(0.6f, metal.BaseColorFactor.Z);

        var gold = PbrMaterial.Gold();
        Assert.Equal("Gold", gold.Name);
        Assert.Equal(1f, gold.MetallicFactor);
        Assert.Equal(0.2f, gold.RoughnessFactor);

        var copper = PbrMaterial.Copper(0.4f);
        Assert.Equal(0.4f, copper.RoughnessFactor);
        Assert.True(copper.BaseColorFactor.X > copper.BaseColorFactor.Z);

        var iron = PbrMaterial.Iron();
        Assert.Equal(0.4f, iron.RoughnessFactor);

        var plastic = PbrMaterial.Plastic(new Vector3(0.1f, 0.2f, 0.3f));
        Assert.Equal(0f, plastic.MetallicFactor);
        Assert.Equal(1.46f, plastic.IndexOfRefraction);

        var glass = PbrMaterial.Glass(0.05f, 1.33f);
        Assert.Equal(AlphaMode.Blend, glass.AlphaMode);
        Assert.Equal(0.1f, glass.BaseColorFactor.W);
        Assert.Equal(1.33f, glass.IndexOfRefraction);

        var glow = PbrMaterial.Emissive(new Vector3(0.2f, 0.3f, 1f), 8f);
        Assert.Equal(8f, glow.EmissiveIntensity);
        Assert.Equal(1f, glow.RoughnessFactor);

        var paint = PbrMaterial.CarPaint(new Vector3(0.8f, 0.1f, 0.1f), 0.6f);
        Assert.Equal(0.6f, paint.ClearcoatFactor);
        Assert.Equal(0.1f, paint.ClearcoatRoughness);
        Assert.Equal(0f, paint.MetallicFactor);
    }

    [Fact]
    public void GpuMaterial_PacksFactorsAndLeavesTexturesUnset()
    {
        Assert.Equal(80, System.Runtime.InteropServices.Marshal.SizeOf<GpuMaterial>());

        var defaults = GpuMaterial.Default;
        Assert.Equal(-1, defaults.BaseColorTextureIndex);
        Assert.Equal(-1, defaults.MetallicRoughnessTextureIndex);
        Assert.Equal(-1, defaults.NormalTextureIndex);
        Assert.Equal(-1, defaults.EmissiveTextureIndex);
        Assert.Equal(0.5f, defaults.RoughnessFactor);

        var source = PbrMaterial.Gold();
        source.BaseColorTexturePath = "albedo.png";
        source.NormalTexturePath = "normal.png";
        source.MetallicRoughnessTexturePath = "mr.png";
        source.EmissiveTexturePath = "emit.png";
        source.OcclusionTexturePath = "ao.png";

        var gpu = GpuMaterial.FromPbrMaterial(source, path => path switch
        {
            "albedo.png" => 3,
            "mr.png" => 4,
            "normal.png" => 5,
            "emit.png" => 6,
            _ => -1
        });

        Assert.Equal(source.BaseColorFactor, gpu.BaseColorFactor);
        Assert.Equal(1f, gpu.MetallicFactor);
        Assert.Equal(3, gpu.BaseColorTextureIndex);
        Assert.Equal(4, gpu.MetallicRoughnessTextureIndex);
        Assert.Equal(5, gpu.NormalTextureIndex);
        Assert.Equal(6, gpu.EmissiveTextureIndex);
        Assert.Equal(source.ClearcoatFactor, gpu.ClearcoatFactor);
    }

    [Fact]
    public void GpuMaterial_WithoutResolver_UsesNegativeTextureIndices()
    {
        var gpu = GpuMaterial.FromPbrMaterial(PbrMaterial.Default());
        Assert.Equal(-1, gpu.BaseColorTextureIndex);
        Assert.Equal(Vector4.One, gpu.BaseColorFactor);
    }
}

public class MaterialLibraryCoverageTests
{
    [Fact]
    public void AddAndLookup_AreCaseInsensitive_AndDefaultStaysAtZero()
    {
        var library = new MaterialLibrary();
        Assert.Equal(1, library.Count);
        Assert.True(library.IsDirty);
        Assert.Equal(0, library.GetMaterialId("missing"));
        Assert.Null(library.GetMaterial(-1));
        Assert.Null(library.GetMaterial(5));
        Assert.Null(library.GetMaterial("nope"));

        int id = library.AddMaterial(PbrMaterial.Gold());
        Assert.Equal(1, id);
        Assert.Equal(id, library.GetMaterial("GOLD")!.Id);
        Assert.Same(library.Materials[id], library.GetMaterial(id));
    }

    [Fact]
    public void UpdateAndDirtyFlags_NotifyListeners()
    {
        var library = new MaterialLibrary();
        int changes = 0;
        library.MaterialsChanged += () => changes++;

        int id = library.AddMaterial(PbrMaterial.Iron());
        library.MarkClean();
        Assert.False(library.IsDirty);

        library.UpdateMaterial(id, mat => mat.RoughnessFactor = 0.9f);
        Assert.Equal(0.9f, library.GetMaterial(id)!.RoughnessFactor);
        Assert.True(library.IsDirty);

        library.UpdateMaterial(99, mat => mat.Name = "ignored");
        library.MarkDirty();
        Assert.True(library.IsDirty);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void GetGpuMaterials_CachesUntilDirty_AndResolvesTextureIndices()
    {
        var library = new MaterialLibrary();
        var gold = PbrMaterial.Gold();
        gold.BaseColorTexturePath = "gold.png";
        library.AddMaterial(gold);

        var textures = new TextureLibrary();
        var first = library.GetGpuMaterials(textures);
        var second = library.GetGpuMaterials();
        Assert.Same(first, second);
        Assert.False(library.IsDirty);
        Assert.Equal(-1, first[1].BaseColorTextureIndex);
        Assert.Equal(2, first.Length);

        library.MarkDirty();
        Assert.NotSame(first, library.GetGpuMaterials());
    }

    [Fact]
    public void RemoveMaterial_ShiftsLaterIds_AndRefusesDefault()
    {
        var library = new MaterialLibrary();
        library.AddMaterial(PbrMaterial.Gold());
        var iron = PbrMaterial.Iron();
        library.AddMaterial(iron);

        Assert.False(library.RemoveMaterial(0));
        Assert.False(library.RemoveMaterial(-1));
        Assert.True(library.RemoveMaterial(1));

        Assert.Equal(2, library.Count);
        Assert.Equal(1, iron.Id);
        Assert.Equal(1, library.GetMaterialId("Iron"));
        Assert.Null(library.GetMaterial("Gold"));
    }

    [Fact]
    public void RemoveMaterial_CurrentBehavior_DropsEarlierDuplicateName()
    {
        var library = new MaterialLibrary();
        var first = new PbrMaterial { Name = "Shared", RoughnessFactor = 0.2f };
        var second = new PbrMaterial { Name = "Shared", RoughnessFactor = 0.8f };
        library.AddMaterial(first);
        library.AddMaterial(second);

        Assert.True(library.RemoveMaterial(second.Id));
        Assert.Equal(0.2f, first.RoughnessFactor);
        Assert.Null(library.GetMaterial("Shared"));
    }

    [Fact(Skip = "BUG: expected the surviving material named Shared to stay reachable, actual GetMaterial(\"Shared\") is null after the later duplicate is removed")]
    public void RemoveMaterial_KeepsEarlierDuplicateName()
    {
        var library = new MaterialLibrary();
        library.AddMaterial(new PbrMaterial { Name = "Shared", RoughnessFactor = 0.2f });
        var second = new PbrMaterial { Name = "Shared", RoughnessFactor = 0.8f };
        library.AddMaterial(second);

        library.RemoveMaterial(second.Id);
        Assert.NotNull(library.GetMaterial("Shared"));
    }

    [Fact]
    public void Clear_RestoresASingleDefault_AndPresetsCoverKnownNames()
    {
        var library = new MaterialLibrary();
        library.AddMaterial(PbrMaterial.Gold());
        library.Clear();
        Assert.Equal(1, library.Count);
        Assert.Equal("Default", library.GetMaterial(0)!.Name);

        library.AddStandardPresets();
        Assert.NotNull(library.GetMaterial("Chrome"));
        Assert.NotNull(library.GetMaterial("Red Plastic"));
        Assert.NotNull(library.GetMaterial("Marble"));
        Assert.True(library.GetMaterial("Marble")!.SubsurfaceFactor > 0f);
        Assert.True(library.GetMaterial("Orange Glow")!.EmissiveIntensity > 1f);
        Assert.True(library.Count > 10);
    }
}

public class MaterialSerializerCoverageTests
{
    [Fact]
    public void JsonRoundTrip_PreservesExtendedFields()
    {
        var source = PbrMaterial.CarPaint(new Vector3(0.2f, 0.3f, 0.4f));
        source.Name = "Lacquer";
        source.BaseColorTexturePath = "a.png";
        source.MetallicRoughnessTexturePath = "mr.png";
        source.NormalTexturePath = "n.png";
        source.NormalScale = 0.7f;
        source.OcclusionTexturePath = "ao.png";
        source.OcclusionStrength = 0.4f;
        source.EmissiveFactor = new Vector3(0.1f, 0.2f, 0.3f);
        source.EmissiveIntensity = 3f;
        source.EmissiveTexturePath = "e.png";
        source.IndexOfRefraction = 1.8f;
        source.SpecularFactor = 0.6f;
        source.AlphaMode = AlphaMode.Mask;
        source.AlphaCutoff = 0.25f;
        source.DoubleSided = true;
        source.SubsurfaceFactor = 0.4f;
        source.SubsurfaceColor = new Vector3(0.2f, 0.3f, 0.4f);
        source.AnisotropyStrength = 0.5f;
        source.AnisotropyRotation = 1.2f;

        var restored = MaterialSerializer.FromJson(MaterialSerializer.ToJson(source));

        Assert.Equal("Lacquer", restored.Name);
        Assert.Equal(source.BaseColorFactor, restored.BaseColorFactor);
        Assert.Equal("a.png", restored.BaseColorTexturePath);
        Assert.Equal("mr.png", restored.MetallicRoughnessTexturePath);
        Assert.Equal(0.7f, restored.NormalScale);
        Assert.Equal(0.4f, restored.OcclusionStrength);
        Assert.Equal(source.EmissiveFactor, restored.EmissiveFactor);
        Assert.Equal(3f, restored.EmissiveIntensity);
        Assert.Equal(AlphaMode.Mask, restored.AlphaMode);
        Assert.Equal(0.25f, restored.AlphaCutoff);
        Assert.True(restored.DoubleSided);
        Assert.Equal(0.4f, restored.SubsurfaceFactor);
        Assert.Equal(source.SubsurfaceColor, restored.SubsurfaceColor);
        Assert.Equal(0.5f, restored.AnisotropyStrength);
        Assert.Equal(1.2f, restored.AnisotropyRotation);
        Assert.Contains("lacquer", MaterialSerializer.ToJson(source), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromFileData_FillsMissingChannels_AndRejectsUnknownAlpha()
    {
        var data = new MaterialFileData
        {
            Name = "Short",
            BaseColor = new[] { 0.25f },
            EmissiveColor = Array.Empty<float>(),
            SubsurfaceColor = new[] { 0.5f, 0.5f },
            AlphaMode = "not-a-mode",
            Metallic = 0.3f
        };

        var material = MaterialSerializer.FromFileData(data);
        Assert.Equal(new Vector4(0.25f, 1f, 1f, 1f), material.BaseColorFactor);
        Assert.Equal(Vector3.Zero, material.EmissiveFactor);
        Assert.Equal(new Vector3(0.5f, 0.5f, 0.1f), material.SubsurfaceColor);
        Assert.Equal(AlphaMode.Opaque, material.AlphaMode);
        Assert.Equal(0.3f, material.MetallicFactor);
    }

    [Fact]
    public void FileRoundTrip_AndDirectoryLoad_ReportsBadFiles()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rhmat-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(dir, "gold.rhmat");
            var nested = Directory.CreateDirectory(Path.Combine(dir, "nested")).FullName;
            MaterialSerializer.SaveToFile(PbrMaterial.Gold(), path);
            MaterialSerializer.SaveToFile(PbrMaterial.Glass(), Path.Combine(nested, "glass.rhmat"));
            File.WriteAllText(Path.Combine(dir, "broken.rhmat"), "{");

            Assert.True(MaterialSerializer.TryLoadFromFile(path, out var loaded, out var error));
            Assert.Null(error);
            Assert.Equal("Gold", loaded!.Name);
            Assert.Equal(1f, loaded.MetallicFactor);

            Assert.False(MaterialSerializer.TryLoadFromFile(Path.Combine(dir, "missing.rhmat"), out var missing, out var missingError));
            Assert.Null(missing);
            Assert.False(string.IsNullOrEmpty(missingError));

            var all = MaterialSerializer.LoadAllFromDirectory(dir, out var errors);
            Assert.Equal(2, all.Count);
            Assert.Contains(errors, e => e.StartsWith("broken.rhmat", StringComparison.Ordinal));
            Assert.Equal(3, MaterialSerializer.GetMaterialFiles(dir).Length);
            Assert.Empty(MaterialSerializer.GetMaterialFiles(Path.Combine(dir, "nope")));
            Assert.Equal(".rhmat", MaterialSerializer.FileExtension);
            Assert.Contains("*.rhmat", MaterialSerializer.FileFilter);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void FromJson_RejectsNullPayload()
    {
        Assert.Throws<InvalidDataException>(() => MaterialSerializer.FromJson("null"));
    }
}

public class MaterialConverterCoverageTests
{
    [Fact]
    public void NamedPresets_MatchPartialNames_AndApplyColorOverrides()
    {
        var gold = MaterialConverter.Convert(LegacyMaterial.FromColor(new Vector3(0.2f, 0.2f, 0.2f), "Brushed_Gold_01"));
        Assert.Equal(1f, gold.MetallicFactor);
        Assert.Equal(new Vector4(0.2f, 0.2f, 0.2f, 1f), gold.BaseColorFactor);
        Assert.Equal("Brushed_Gold_01", gold.Name);

        var water = MaterialConverter.Convert(new LegacyMaterial { Name = "water" });
        Assert.Equal(1.333f, water.IndexOfRefraction, precision: 3);
        Assert.Equal(AlphaMode.Blend, water.AlphaMode);

        var diamond = MaterialConverter.Convert(new LegacyMaterial { Name = "diamond" });
        Assert.Equal(2.42f, diamond.IndexOfRefraction, precision: 2);

        var rubber = MaterialConverter.Convert(new LegacyMaterial { Name = "rubber" });
        Assert.Equal(0f, rubber.MetallicFactor);
        Assert.True(rubber.RoughnessFactor > 0.8f);

        var mirror = MaterialConverter.Convert(new LegacyMaterial { Name = "mirror" });
        Assert.Equal(1f, mirror.MetallicFactor);
        Assert.Equal(0f, mirror.RoughnessFactor);
    }

    [Fact]
    public void MinimalOptions_KeepSuppliedColorWithoutHeuristics()
    {
        var legacy = LegacyMaterial.FromColor(new Vector4(0.8f, 0.1f, 0.1f, 1f), "gold");
        var material = MaterialConverter.Convert(legacy, ConversionOptions.Minimal);

        Assert.Equal("gold", material.Name);
        Assert.Equal(0f, material.MetallicFactor);
        Assert.Equal(0.5f, material.RoughnessFactor);
        Assert.Equal(new Vector4(0.8f, 0.1f, 0.1f, 1f), material.BaseColorFactor);
        Assert.False(ConversionOptions.Minimal.UseNameMatching);
        Assert.True(ConversionOptions.Default.UseColorBasedDetection);
    }

    [Fact]
    public void SpecularShininessOpacityAndIor_MapIntoPbr()
    {
        var legacy = LegacyMaterial.FromDiffuseSpecular(
            new Vector3(0.1f, 0.2f, 0.9f),
            new Vector3(0.1f, 0.2f, 0.9f),
            shininess: 100f,
            name: "Painted");
        legacy.Opacity = 0.4f;
        legacy.Transparency = 0.9f;
        legacy.EmissiveColor = new Vector3(0.2f, 0f, 0f);
        legacy.IndexOfRefraction = 1.7f;
        legacy.BumpScale = 2f;

        var material = MaterialConverter.Convert(legacy, new ConversionOptions
        {
            UseNameMatching = false,
            UseColorBasedDetection = false,
            UseLuminanceBasedRoughness = true
        });

        Assert.Equal(0.4f, material.BaseColorFactor.W);
        Assert.Equal(AlphaMode.Blend, material.AlphaMode);
        Assert.Equal(new Vector3(0.2f, 0f, 0f), material.EmissiveFactor);
        Assert.Equal(1f, material.EmissiveIntensity);
        Assert.Equal(1.7f, material.IndexOfRefraction);
        Assert.True(material.MetallicFactor > 0.8f);
        float expectedRoughness = MathF.Sqrt(2f / 102f);
        Assert.Equal(expectedRoughness, material.RoughnessFactor, precision: 3);
    }

    [Fact]
    public void TransparencyReflectivityAndAmbient_FillMissingChannels()
    {
        var transparent = MaterialConverter.Convert(new LegacyMaterial
        {
            Name = "custom-dielectric",
            AmbientColor = new Vector3(0.2f, 0.3f, 0.4f),
            Transparency = 0.25f,
            Reflectivity = 0.8f
        }, new ConversionOptions { UseNameMatching = false, UseColorBasedDetection = false, UseLuminanceBasedRoughness = false });

        Assert.Equal(0.75f, transparent.BaseColorFactor.W, precision: 3);
        Assert.Equal(AlphaMode.Blend, transparent.AlphaMode);
        Assert.Equal(0.8f, transparent.MetallicFactor, precision: 3);
        Assert.Equal(0.6f, transparent.RoughnessFactor, precision: 3);

        var unnamed = MaterialConverter.Convert(new LegacyMaterial(), ConversionOptions.Minimal);
        Assert.Equal("Converted Material", unnamed.Name);
        Assert.Equal(Vector4.One, unnamed.BaseColorFactor);
    }

    [Fact]
    public void ConvertBatch_PreservesOrder()
    {
        var results = MaterialConverter.ConvertBatch(new[]
        {
            new LegacyMaterial { Name = "wood" },
            new LegacyMaterial { Name = "concrete" }
        }).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal(0.7f, results[0].RoughnessFactor);
        Assert.Equal(0.9f, results[1].RoughnessFactor);
    }

    [Fact]
    public void ConvertMaterialComponent_RedDielectric_UsesShininess()
    {
        var component = MaterialComponent.Default;
        component.BaseColor = new Vector4(0.8f, 0.05f, 0.05f, 1f);
        component.Metallic = 0f;
        component.Roughness = 0.5f;

        var material = MaterialConverter.ConvertMaterialComponent(component, "Crate");
        Assert.Equal("Crate", material.Name);
        Assert.Equal(0f, material.MetallicFactor);
        Assert.Equal(MathF.Sqrt(2f / 52f), material.RoughnessFactor, precision: 3);
    }

    [Fact]
    public void ConvertMaterialComponent_CurrentBehavior_TreatsWhiteAsMetal()
    {
        var material = MaterialConverter.ConvertMaterialComponent(MaterialComponent.Default, null);
        Assert.Equal(1f, material.MetallicFactor);
    }

    [Fact(Skip = "BUG: expected MaterialComponent.Default (pure white, metallic 0) to stay dielectric, actual metallic factor is 1 because white is within 0.15 of silver")]
    public void ConvertMaterialComponent_WhiteDefault_StaysDielectric()
    {
        var material = MaterialConverter.ConvertMaterialComponent(MaterialComponent.Default);
        Assert.Equal(0f, material.MetallicFactor);
    }

    [Fact]
    public void ConvertMaterialComponent_ThrowsWhenAlreadyPbr()
    {
        var component = MaterialComponent.FromPbrMaterial(3);
        var ex = Assert.Throws<InvalidOperationException>(() => MaterialConverter.ConvertMaterialComponent(component));
        Assert.Contains("MaterialLibrary", ex.Message);
    }
}

public class TextureLibraryCoverageTests
{
    [Fact]
    public void LoadTexture_CachesResizeAndColorSpace()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pbrtex-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var rgba = new byte[8 * 4 * 4];
            for (int i = 0; i < 8 * 4; i++)
            {
                rgba[i * 4] = 255;
                rgba[i * 4 + 3] = 255;
            }
            var path = CoveragePng.Write(Path.Combine(dir, "Albedo.PNG"), 8, 4, rgba);

            var library = new TextureLibrary { MaxTextureSize = 4 };
            Assert.Null(library.LoadTexture("", PbrTextureType.BaseColor));
            Assert.Null(library.LoadTexture(Path.Combine(dir, "missing.png"), PbrTextureType.BaseColor));
            Assert.Equal(-1, library.GetTextureIndex(null, PbrTextureType.Normal));

            var albedo = library.LoadTexture(path, PbrTextureType.BaseColor);
            Assert.NotNull(albedo);
            Assert.True(albedo!.IsSrgb);
            Assert.Equal(0, albedo.Index);
            Assert.Equal(4, albedo.Width);
            Assert.Equal(2, albedo.Height);
            Assert.Equal(4 * 2 * 4, albedo.PixelData.Length);
            Assert.True(library.IsDirty);
            Assert.Same(albedo, library.LoadTexture(path.ToLowerInvariant(), PbrTextureType.Normal));
            Assert.Equal(1, library.Count);
            Assert.Equal(0, library.GetTextureIndex(path, PbrTextureType.BaseColor));
            Assert.Same(albedo, library.GetTexture(0));
            Assert.Same(albedo, library.GetTexture(path));
            Assert.Null(library.GetTexture(3));
            Assert.Null(library.GetTexture("other.png"));
            Assert.Equal(0, library.CreateResolver(PbrTextureType.BaseColor)(path));

            library.MarkClean();
            Assert.False(library.IsDirty);
            library.Clear();
            Assert.Equal(0, library.Count);
            Assert.True(library.IsDirty);
            library.Dispose();
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void LoadTexture_RejectsGarbage_AndPreloadsEveryMaterialSlot()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pbrtex-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var bad = Path.Combine(dir, "bad.png");
            File.WriteAllText(bad, "not an image");
            var library = new TextureLibrary();
            Assert.Null(library.LoadTexture(bad, PbrTextureType.Normal));

            var rgba = new byte[] { 0, 0, 255, 255 };
            var normal = CoveragePng.Write(Path.Combine(dir, "n.png"), 1, 1, rgba);
            var ao = CoveragePng.Write(Path.Combine(dir, "ao.png"), 1, 1, rgba);
            var emit = CoveragePng.Write(Path.Combine(dir, "e.png"), 1, 1, rgba);

            var materials = new MaterialLibrary();
            materials.AddMaterial(new PbrMaterial
            {
                Name = "Textured",
                NormalTexturePath = normal,
                OcclusionTexturePath = ao,
                EmissiveTexturePath = emit
            });
            library.PreloadMaterialTextures(materials);

            Assert.Equal(3, library.Count);
            Assert.False(library.GetTexture(normal)!.IsSrgb);
            Assert.False(library.GetTexture(ao)!.IsSrgb);
            Assert.True(library.GetTexture(emit)!.IsSrgb);

            var gpu = materials.GetGpuMaterials(library);
            Assert.Equal(library.GetTexture(normal)!.Index, gpu[1].NormalTextureIndex);
            Assert.Equal(library.GetTexture(emit)!.Index, gpu[1].EmissiveTextureIndex);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

public class EnvironmentMapCoverageTests
{
    [Fact]
    public void ProceduralSky_SamplesSkyAndGround_AndEncodesHalfFloat()
    {
        using var sky = EnvironmentMap.CreateProceduralSky(32, 16);
        Assert.True(sky.IsLoaded);
        Assert.Equal(32, sky.Width);
        Assert.Equal(16, sky.Height);
        Assert.Equal(32 * 16 * 3, sky.HdrData.Length);
        Assert.Equal("[Procedural Sky]", sky.FilePath);

        var up = sky.Sample(Vector3.UnitY);
        var down = sky.Sample(-Vector3.UnitY);
        Assert.Equal(0.2f, up.X, precision: 3);
        Assert.Equal(0.4f, up.Y, precision: 3);
        Assert.Equal(0.8f, up.Z, precision: 3);
        Assert.Equal(0.1f, down.X, precision: 3);
        Assert.Equal(0.08f, down.Y, precision: 3);
        Assert.Equal(0.06f, down.Z, precision: 3);

        sky.Intensity = -5f;
        Assert.Equal(0f, sky.Intensity);
        Assert.Equal(Vector3.Zero, sky.Sample(Vector3.UnitY));
        sky.Intensity = 2f;
        var boosted = sky.Sample(Vector3.UnitY);
        Assert.Equal(1.6f, boosted.Z, precision: 3);

        sky.Rotation = MathF.PI;
        var turned = sky.Sample(new Vector3(1f, 0.2f, 0f));
        Assert.True(float.IsFinite(turned.X));

        var rgb = sky.GetRgb16fData();
        var rgba = sky.GetRgba16fData();
        Assert.Equal(32 * 16 * 3, rgb.Length);
        Assert.Equal(32 * 16 * 4, rgba.Length);
        Assert.Contains(rgb, value => value != 0);

        sky.Dispose();
        Assert.False(sky.IsLoaded);
        Assert.Equal(Vector3.Zero, sky.Sample(Vector3.UnitX));
        Assert.Empty(sky.GetRgb16fData());
    }

    [Fact(Skip = "BUG: expected Sample(+Y) to read the blue procedural sky, actual +Y maps to v=1 while CreateProceduralSky stores +Y at v=0, so Sample(+Y) returns the ground color")]
    public void ProceduralSky_UpDirection_IsBlue()
    {
        using var sky = EnvironmentMap.CreateProceduralSky(32, 16);
        var up = sky.Sample(Vector3.UnitY);
        Assert.True(up.Z > up.X);
    }

    [Fact]
    public void Load_ConvertsSrgbPng_AndRejectsMissingFiles()
    {
        using var map = new EnvironmentMap();
        Assert.False(map.IsLoaded);
        Assert.False(map.Load(""));
        Assert.False(map.Load(Path.Combine(Path.GetTempPath(), "missing-env.png")));
        Assert.Equal(Vector3.Zero, map.Sample(Vector3.UnitX));

        var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "env-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var white = new byte[] { 255, 255, 255, 255 };
            var black = new byte[] { 0, 0, 0, 255 };
            var rgba = new byte[2 * 2 * 4];
            Buffer.BlockCopy(white, 0, rgba, 0, 4);
            Buffer.BlockCopy(black, 0, rgba, 4, 4);
            Buffer.BlockCopy(black, 0, rgba, 8, 4);
            Buffer.BlockCopy(white, 0, rgba, 12, 4);
            var path = CoveragePng.Write(Path.Combine(dir, "studio.png"), 2, 2, rgba);

            Assert.True(map.Load(path));
            Assert.Equal(path, map.FilePath);
            Assert.Equal(2 * 2 * 3, map.HdrData.Length);
            Assert.Equal(1f, map.HdrData[0], precision: 3);
            Assert.Equal(0f, map.HdrData[3], precision: 3);

            var half = map.GetRgba16fData();
            Assert.Equal(0x3C00, half[0]);
            Assert.Equal(0x3C00, half[3]);

            var bad = Path.Combine(dir, "bad.png");
            File.WriteAllText(bad, "nope");
            Assert.False(map.Load(bad));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

internal static class CoveragePng
{
    public static string Write(string path, int width, int height, byte[] rgba)
    {
        File.WriteAllBytes(path, Encode(width, height, rgba));
        return path;
    }

    public static byte[] Encode(int width, int height, byte[] rgba)
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        var ihdr = new byte[13];
        WriteBe(ihdr, 0, (uint)width);
        WriteBe(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(ms, "IHDR", ihdr);

        int stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int row = y * (stride + 1);
            raw[row] = 0;
            Buffer.BlockCopy(rgba, y * stride, raw, row + 1, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }
        WriteChunk(ms, "IDAT", compressed.ToArray());
        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = new byte[4];
        WriteBe(length, 0, (uint)data.Length);
        stream.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcSource = new byte[typeBytes.Length + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcSource, 0, typeBytes.Length);
        Buffer.BlockCopy(data, 0, crcSource, typeBytes.Length, data.Length);
        var crc = new byte[4];
        WriteBe(crc, 0, Crc32(crcSource));
        stream.Write(crc);
    }

    private static void WriteBe(byte[] dest, int offset, uint value)
    {
        dest[offset] = (byte)(value >> 24);
        dest[offset + 1] = (byte)(value >> 16);
        dest[offset + 2] = (byte)(value >> 8);
        dest[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }
}
