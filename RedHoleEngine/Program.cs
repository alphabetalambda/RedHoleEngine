using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core;
using RedHoleEngine.Rendering;

namespace RedHoleEngine;

class Program
{
    static void Main(string[] args)
    {
        // On macOS, the native launcher (RedHoleEngine executable) handles environment setup.
        // On Windows/Linux, Vulkan is found through standard system paths.
        
        Enginedeco.EngineTitlePrint();
        // Create and configure the application
        var app = new Application
        {
            WindowWidth = 1280,
            WindowHeight = 720,
            WindowTitle = "RedHole Engine - Schwarzschild Black Hole Raytracer",
            VSync = true
        };

        // Setup scene when initialized
        app.OnInitialize += () =>
        {
            // Create a black hole at the origin using the new ECS
            app.CreateBlackHole(
                position: Vector3.Zero,
                mass: 2.0f,
                withAccretionDisk: true
            );

            Console.WriteLine("Scene initialized with ECS-based black hole entity");
        };

        // Optional: Add custom update logic
        app.OnUpdate += deltaTime =>
        {
            // Custom game logic here
            // For example, you could query entities and update them:
            // foreach (var entity in app.World!.Query<GravitySourceComponent>())
            // {
            //     // Update gravity sources
            // }
        };

        // Run the application
        app.Run(GraphicsBackendType.Vulkan);
    }
    
}
