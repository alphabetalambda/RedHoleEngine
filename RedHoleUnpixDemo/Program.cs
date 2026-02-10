using System.Numerics;
using RedHoleEngine;
using RedHoleEngine.Components;
using RedHoleEngine.Core;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleUnpixDemo;

public static class Program
{
    public static void Main(string[] args)
    {

        var app = new Application
        {
            WindowWidth = 1280,
            WindowHeight = 720,
            WindowTitle = "RedHole Unpix Demo",
            VSync = true
        };

        var gameModule = new UnpixDemoModule();
        var saveManager = new GameSaveManager(gameModule.Name);
        app.OnInitialize += () =>
        {
            if (app.World == null)
                return;

            app.RenderSettings.Mode = RenderMode.Rasterized;
            var context = new GameContext(app.World, app.Resources, app.RenderSettings, app.RaytracerSettings, saveManager, app, isEditor: false);
            gameModule.BuildScene(context);
            Console.WriteLine($"Scene initialized by {gameModule.Name}");
        };

        app.Run(GraphicsBackendType.Vulkan);
    }
}
