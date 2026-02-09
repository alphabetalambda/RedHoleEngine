using RedHoleEngine;
using RedHoleEngine.Core;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;

namespace RedHoleGame;

public static class Program
{
    public static void Main(string[] args)
    {
        Enginedeco.EngineTitlePrint();

        var app = new Application
        {
            WindowWidth = 1280,
            WindowHeight = 720,
            WindowTitle = "RedHole Engine",
            VSync = true
        };

        var gameModule = new GameModule();
        var saveManager = new GameSaveManager(gameModule.Name);
        app.OnInitialize += () =>
        {
            if (app.World == null)
                return;

            var context = new GameContext(app.World, app.Resources, app.RenderSettings, app.RaytracerSettings, saveManager, app, isEditor: false);
            gameModule.BuildScene(context);
            Console.WriteLine($"Scene initialized by {gameModule.Name}");
        };

        app.Run(GraphicsBackendType.Vulkan);
    }
}
