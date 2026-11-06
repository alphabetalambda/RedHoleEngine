using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Game;

public class GameContextTests
{
    private sealed class DemoModule : IGameModule
    {
        public string Name => "Demo";
        public bool Built { get; private set; }

        public void BuildScene(GameContext context)
        {
            Built = true;
            Assert.True(context.IsEditor);
            Assert.Null(context.Application);
            context.World.CreateEntity();
        }
    }

    [Fact]
    public void Constructor_ExposesSuppliedServices()
    {
        using var world = new World();
        using var resources = new ResourceManager();
        var render = new RenderSettings { Mode = RenderMode.Rasterized };
        var raytracer = new RaytracerSettings { RaysPerPixel = 4 };
        var saves = new GameSaveManager("context-" + Guid.NewGuid().ToString("N"));

        try
        {
            var context = new GameContext(world, resources, render, raytracer, saves, application: null, isEditor: true);

            Assert.Same(world, context.World);
            Assert.Same(resources, context.Resources);
            Assert.Same(render, context.RenderSettings);
            Assert.Same(raytracer, context.RaytracerSettings);
            Assert.Same(saves, context.SaveManager);
            Assert.Null(context.Application);
            Assert.True(context.IsEditor);
            Assert.Equal(RenderMode.Rasterized, context.RenderSettings.Mode);
            Assert.Equal(4, context.RaytracerSettings.RaysPerPixel);
        }
        finally
        {
            DeleteSaves(saves);
        }
    }

    [Fact]
    public void GameModule_BuildScene_ReceivesContext()
    {
        using var world = new World();
        using var resources = new ResourceManager();
        var saves = new GameSaveManager("module-" + Guid.NewGuid().ToString("N"));
        try
        {
            var context = new GameContext(world, resources, new RenderSettings(), new RaytracerSettings(), saves, null, true);
            IGameModule module = new DemoModule();

            Assert.Equal("Demo", module.Name);
            module.BuildScene(context);

            Assert.True(((DemoModule)module).Built);
            Assert.Equal(1, world.EntityCount);
        }
        finally
        {
            DeleteSaves(saves);
        }
    }

    private static void DeleteSaves(GameSaveManager manager)
    {
        var slot = manager.GetSavePath("default");
        var saves = Path.GetDirectoryName(slot);
        var game = saves == null ? null : Path.GetDirectoryName(saves);
        if (game != null && Directory.Exists(game))
            Directory.Delete(game, true);
    }
}
