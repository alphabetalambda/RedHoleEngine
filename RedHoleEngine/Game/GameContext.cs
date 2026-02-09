using RedHoleEngine.Core;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Game;

public sealed class GameContext
{
    public GameContext(World world, ResourceManager resources, RenderSettings renderSettings, RaytracerSettings raytracerSettings, GameSaveManager saveManager, Application? application, bool isEditor)
    {
        World = world;
        Resources = resources;
        RenderSettings = renderSettings;
        RaytracerSettings = raytracerSettings;
        SaveManager = saveManager;
        Application = application;
        IsEditor = isEditor;
    }

    public World World { get; }
    public ResourceManager Resources { get; }
    public RenderSettings RenderSettings { get; }
    public RaytracerSettings RaytracerSettings { get; }
    public GameSaveManager SaveManager { get; }
    public Application? Application { get; }
    public bool IsEditor { get; }
}
