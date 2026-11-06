using System.Numerics;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Tests.Rendering;

public class MaterialSerializerBehaviorTests
{
    [Fact]
    public void JsonRoundTrip_PreservesFactors_AndUnknownAlphaModeFallsBack()
    {
        var mat = PbrMaterial.CarPaint(new Vector3(0.1f, 0.2f, 0.8f), 0.6f);
        mat.Name = "Paint";
        mat.BaseColorTexturePath = "base.png";
        mat.NormalTexturePath = "n.png";
        mat.NormalScale = 0.4f;
        mat.MetallicRoughnessTexturePath = "mr.png";
        mat.OcclusionTexturePath = "ao.png";
        mat.OcclusionStrength = 0.7f;
        mat.EmissiveFactor = new Vector3(0.2f, 0, 0);
        mat.EmissiveIntensity = 3f;
        mat.EmissiveTexturePath = "e.png";
        mat.AlphaMode = AlphaMode.Mask;
        mat.AlphaCutoff = 0.3f;
        mat.DoubleSided = true;
        mat.SubsurfaceFactor = 0.2f;
        mat.SubsurfaceColor = new Vector3(1, 0, 0);
        mat.AnisotropyStrength = 0.4f;
        mat.AnisotropyRotation = 0.5f;
        mat.SpecularFactor = 0.8f;
        mat.IndexOfRefraction = 1.4f;

        var json = MaterialSerializer.ToJson(mat);
        var loaded = MaterialSerializer.FromJson(json);
        Assert.Equal("Paint", loaded.Name);
        Assert.Equal(0.6f, loaded.ClearcoatFactor, 1e-4f);
        Assert.Equal("base.png", loaded.BaseColorTexturePath);
        Assert.Equal(AlphaMode.Mask, loaded.AlphaMode);
        Assert.Equal(0.3f, loaded.AlphaCutoff);
        Assert.True(loaded.DoubleSided);
        Assert.Equal(0.4f, loaded.AnisotropyStrength);
        Assert.Equal(MaterialSerializer.FileExtension, ".rhmat");
        Assert.Contains("rhmat", MaterialSerializer.FileFilter);

        var data = MaterialSerializer.ToFileData(loaded);
        data.AlphaMode = "not-a-mode";
        var fallback = MaterialSerializer.FromFileData(data);
        Assert.Equal(AlphaMode.Opaque, fallback.AlphaMode);
    }

    [Fact]
    public void FileRoundTrip_AndDirectoryLoad_ReportsErrors()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rhmat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "gold.rhmat");
            MaterialSerializer.SaveToFile(PbrMaterial.Gold(), path);
            var loaded = MaterialSerializer.LoadFromFile(path);
            Assert.Equal("Gold", loaded.Name);

            Assert.True(MaterialSerializer.TryLoadFromFile(path, out var ok, out var error));
            Assert.NotNull(ok);
            Assert.Null(error);
            Assert.False(MaterialSerializer.TryLoadFromFile(Path.Combine(dir, "missing.rhmat"), out _, out var missing));
            Assert.False(string.IsNullOrEmpty(missing));

            File.WriteAllText(Path.Combine(dir, "bad.rhmat"), "{ not json");
            var all = MaterialSerializer.LoadAllFromDirectory(dir, out var errors);
            Assert.Contains(all, m => m.Name == "Gold");
            Assert.NotEmpty(errors);
            Assert.Contains(MaterialSerializer.GetMaterialFiles(dir), f => f.EndsWith(".rhmat"));

            Assert.ThrowsAny<Exception>(() => MaterialSerializer.LoadFromFile(Path.Combine(dir, "nope.rhmat")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
