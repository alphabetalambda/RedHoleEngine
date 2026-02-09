using RedHoleEngine;
using RedHoleEngine.Components;
using RedHoleEngine.Core;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;

namespace RedHoleTestScene;

public static class Program
{
    public static void Main(string[] args)
    {
        Enginedeco.EngineTitlePrint();

        var app = new Application
        {
            WindowWidth = 1280,
            WindowHeight = 720,
            WindowTitle = "RedHole Test Scene - Raytraced Unpix + Lasers",
            VSync = true
        };

        var gameModule = new TestSceneModule();
        var saveManager = new GameSaveManager(gameModule.Name);
        app.OnInitialize += () =>
        {
            if (app.World == null)
                return;

            // Use raytraced rendering mode
            app.RenderSettings.Mode = RenderMode.Raytraced;
            var context = new GameContext(app.World, app.Resources, app.RenderSettings, app.RaytracerSettings, saveManager, app, isEditor: false);
            gameModule.BuildScene(context);
            Console.WriteLine($"Scene initialized by {gameModule.Name}");
            
            Console.WriteLine("\n=== Controls ===");
            Console.WriteLine("WASD - Move camera");
            Console.WriteLine("Mouse - Look around");
            Console.WriteLine("Space - Move up");
            Console.WriteLine("Shift - Move down");
            Console.WriteLine("Escape - Toggle mouse capture");
            Console.WriteLine("R - Toggle Raytraced/Rasterized");
            Console.WriteLine("================\n");
        };

        app.Run(GraphicsBackendType.Vulkan);
    }
}
