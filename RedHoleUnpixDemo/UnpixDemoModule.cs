using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleUnpixDemo;

public class UnpixDemoModule : IGameModule
{
    public string Name => "UnpixDemo";

    public void BuildScene(GameContext context)
    {
        var world = context.World;
        var resources = context.Resources;

        resources.Add("mesh_plane", Mesh.CreatePlane(40f, 40f, 2, 2));
        resources.Add("mesh_sphere", Mesh.CreateSphere(1f, 32, 16));

        var planeHandle = resources.GetHandle<Mesh>("mesh_plane");
        var sphereHandle = resources.GetHandle<Mesh>("mesh_sphere");

        var settingsEntity = world.CreateEntity();
        world.AddComponent(settingsEntity, new RenderSettingsComponent(RenderMode.Rasterized));

        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent(new Vector3(0f, -2f, 0f)));
        world.AddComponent(ground, new MeshComponent(planeHandle));
        world.AddComponent(ground, new MaterialComponent
        {
            BaseColor = new Vector4(0.5f, 0.5f, 0.5f, 1f), // Brighter for visibility
            Metallic = 0.05f,
            Roughness = 0.9f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = false
        });
        var groundBody = RigidBodyComponent.CreateStatic();
        groundBody.Friction = 2.0f;  // Very high friction
        world.AddComponent(ground, groundBody);
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane(0f)); // Distance is relative to entity position

        var unpixTarget = world.CreateEntity();
        world.AddComponent(unpixTarget, new TransformComponent(
            new Vector3(0f, 1.5f, -3f),
            Quaternion.Identity,
            new Vector3(2f)
        ));
        world.AddComponent(unpixTarget, new MeshComponent(sphereHandle));
        world.AddComponent(unpixTarget, new MaterialComponent
        {
            BaseColor = new Vector4(0.8f, 0.9f, 1f, 1f),
            Metallic = 0.05f,
            Roughness = 0.2f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = false
        });
        world.AddComponent(unpixTarget, new UnpixComponent
        {
            CubeSize = 0.25f,  // Balance between detail and performance
            DissolveDuration = 5.0f,
            StartDelay = 1.0f,
            VelocityScale = 1.0f,
            SpawnOnStart = true,
            HideSource = true,
            MaxCubes = 200,  // Keep it manageable for physics
            
            // Enable physics
            UsePhysics = true,
            CubeMass = 0.2f,
            CubeRestitution = 0.2f,  // Less bouncy
            CubeFriction = 1.5f,     // High friction on cubes too
            InitialImpulseScale = 2.5f
        });
    }
}
