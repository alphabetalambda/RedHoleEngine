using RedHoleEngine.Core;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;
using RedHoleEngine.Terminal;

namespace RedHoleEngine.Tests.Game;

public class GameSaveBehaviorTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsVirtualFileSystem_AndBlankSlotUsesDefault()
    {
        var gameId = "tests-" + Guid.NewGuid().ToString("N");
        var saves = new GameSaveManager(gameId);
        var path = saves.GetSavePath("  ");
        Assert.EndsWith("default.json", path.Replace('\\', '/'));

        var data = new GameSaveData
        {
            VirtualFileSystem = new VirtualFileSystemState
            {
                Root = new VirtualDirectoryState("/")
                {
                    Files = { new VirtualFileState("readme", "hi") }
                }
            }
        };

        saves.Save("slot-a", data);
        var loaded = saves.Load("slot-a");
        Assert.NotNull(loaded);
        Assert.Equal("readme", loaded!.VirtualFileSystem!.Root.Files[0].Name);
        Assert.Equal("hi", loaded.VirtualFileSystem.Root.Files[0].Contents);
        Assert.Null(saves.Load("missing-slot"));

        Directory.Delete(Path.GetDirectoryName(path)!, recursive: true);
    }

    [Fact]
    public void GameContext_HoldsTheCollaboratorsItWasGiven()
    {
        using var world = new World();
        var resources = new ResourceManager();
        var render = new RenderSettings { Mode = RenderMode.Rasterized };
        var ray = new RaytracerSettings();
        var saves = new GameSaveManager("ctx-" + Guid.NewGuid().ToString("N"));
        var ctx = new GameContext(world, resources, render, ray, saves, application: null, isEditor: true);

        Assert.Same(world, ctx.World);
        Assert.Same(resources, ctx.Resources);
        Assert.Equal(RenderMode.Rasterized, ctx.RenderSettings.Mode);
        Assert.Same(ray, ctx.RaytracerSettings);
        Assert.True(ctx.IsEditor);
        Assert.Null(ctx.Application);

        Directory.Delete(Path.GetDirectoryName(saves.GetSavePath("x"))!, recursive: true);
        resources.Dispose();
    }
}
