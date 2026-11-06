using System.Numerics;
using RedHoleEngine.Rendering.PBR;

namespace RedHoleEngine.Tests.Rendering;

public class MaterialLibraryBehaviorTests
{
    [Fact]
    public void AddGetUpdateRemove_TracksIdsAndDirtiness()
    {
        var library = new MaterialLibrary();
        Assert.Equal(1, library.Count);
        Assert.Equal("Default", library.GetMaterial(0)!.Name);
        Assert.True(library.IsDirty);

        int changes = 0;
        library.MaterialsChanged += () => changes++;

        var gold = PbrMaterial.Gold();
        int id = library.AddMaterial(gold);
        Assert.Equal(id, library.GetMaterial("gold")!.Id);
        Assert.Equal(id, library.GetMaterialId("GOLD"));
        Assert.Null(library.GetMaterial("nope"));
        Assert.Equal(0, library.GetMaterialId("nope"));
        Assert.Null(library.GetMaterial(99));

        library.UpdateMaterial(id, m => m.RoughnessFactor = 0.9f);
        Assert.Equal(0.9f, library.GetMaterial(id)!.RoughnessFactor);
        library.UpdateMaterial(99, _ => throw new InvalidOperationException("should not run"));

        var gpu = library.GetGpuMaterials();
        Assert.Equal(library.Count, gpu.Length);
        Assert.False(library.IsDirty);
        library.MarkDirty();
        Assert.True(library.IsDirty);
        library.MarkClean();

        Assert.True(library.RemoveMaterial(id));
        Assert.False(library.RemoveMaterial(id));
        Assert.False(library.RemoveMaterial(0)); // default stays
        Assert.True(changes >= 2);

        library.Clear();
        Assert.Equal(1, library.Count);
        library.AddStandardPresets();
        Assert.True(library.Count > 5);
        Assert.NotNull(library.GetGpuMaterials(textureLibrary: null));
    }
}
