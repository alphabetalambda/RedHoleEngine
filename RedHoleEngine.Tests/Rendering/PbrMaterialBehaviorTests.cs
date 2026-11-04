using System.Numerics;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Tests.Rendering;

public class PbrMaterialBehaviorTests
{
    [Fact]
    public void Factories_SetTheFactorsTheirNamesImply()
    {
        var metal = PbrMaterial.Metal(new Vector3(0.2f, 0.4f, 0.6f), 0.15f);
        Assert.Equal(1f, metal.MetallicFactor);
        Assert.Equal(0.15f, metal.RoughnessFactor);
        Assert.Equal(0.2f, metal.BaseColorFactor.X);

        Assert.Equal(1f, PbrMaterial.Gold().MetallicFactor);
        Assert.Equal(1f, PbrMaterial.Copper().MetallicFactor);
        Assert.Equal(1f, PbrMaterial.Iron().MetallicFactor);
        Assert.Equal(0f, PbrMaterial.Plastic(Vector3.One).MetallicFactor);
        Assert.Equal(AlphaMode.Blend, PbrMaterial.Glass(0.05f, 1.45f).AlphaMode);
        Assert.Equal(1.45f, PbrMaterial.Glass(0.05f, 1.45f).IndexOfRefraction);
        Assert.Equal(8f, PbrMaterial.Emissive(Vector3.UnitX, 8f).EmissiveIntensity);
        Assert.Equal(0.7f, PbrMaterial.CarPaint(Vector3.One, 0.7f).ClearcoatFactor);
        Assert.Equal(0.1f, PbrMaterial.CarPaint(Vector3.One, 0.7f).ClearcoatRoughness);
        Assert.Equal("Default", PbrMaterial.Default().Name);
    }

    [Fact]
    public void GpuMaterial_PacksFactors_AndResolvesTextures()
    {
        var mat = PbrMaterial.Gold();
        mat.BaseColorTexturePath = "a.png";
        mat.NormalTexturePath = "n.png";
        mat.NormalScale = 0.5f;
        mat.Id = 4;

        var gpu = GpuMaterial.FromPbrMaterial(mat, path => path switch
        {
            "a.png" => 3,
            "n.png" => 7,
            _ => -1
        });
        Assert.Equal(mat.MetallicFactor, gpu.MetallicFactor);
        Assert.Equal(0.5f, gpu.NormalScale);
        Assert.Equal(3, gpu.BaseColorTextureIndex);
        Assert.Equal(7, gpu.NormalTextureIndex);
        Assert.Equal(-1, gpu.MetallicRoughnessTextureIndex);

        var unresolved = GpuMaterial.FromPbrMaterial(mat);
        Assert.Equal(-1, unresolved.BaseColorTextureIndex);
        Assert.Equal(1f, GpuMaterial.Default.OcclusionStrength);
    }
}
