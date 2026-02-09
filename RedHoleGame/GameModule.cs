using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleGame;

public class GameModule : IGameModule
{
    public string Name => "RedHoleGame";

    public void BuildScene(GameContext context)
    {
        var world = context.World;
        var resources = context.Resources;

        resources.Add("mesh_plane", Mesh.CreatePlane(50f, 50f, 4, 4));
        resources.Add("mesh_cube", Mesh.CreateCube(1f));
        resources.Add("mesh_sphere", Mesh.CreateSphere(1f, 32, 16));

        var planeHandle = resources.GetHandle<Mesh>("mesh_plane");
        var cubeHandle = resources.GetHandle<Mesh>("mesh_cube");
        var sphereHandle = resources.GetHandle<Mesh>("mesh_sphere");

        var settingsEntity = world.CreateEntity();
        world.AddComponent(settingsEntity, new RenderSettingsComponent(RenderMode.Raytraced)
        {
            Preset = RaytracerQualityPreset.Balanced,
            Accumulate = true
        });

        if (context.Application != null)
        {
            context.Application.CreateBlackHole(
                position: new Vector3(0f, 0f, 0f),
                mass: 2.2f,
                withAccretionDisk: true
            );
        }
        else
        {
            var blackHole = world.CreateEntity();
            world.AddComponent(blackHole, new TransformComponent(new Vector3(0f, 0f, 0f)));
            world.AddComponent(blackHole, GravitySourceComponent.CreateBlackHole(2.2f));
            world.AddComponent(blackHole, AccretionDiskComponent.CreateForBlackHole(2.2f));
        }

        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent(new Vector3(0f, -2f, 0f)));
        world.AddComponent(ground, new MeshComponent(planeHandle));
        world.AddComponent(ground, new MaterialComponent
        {
            BaseColor = new Vector4(0.18f, 0.2f, 0.24f, 1f),
            Metallic = 0.05f,
            Roughness = 0.9f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = true
        });
        world.AddComponent(ground, new RaytracerMeshComponent(enabled: true) { StaticOnly = true });
        world.AddComponent(ground, RigidBodyComponent.CreateStatic());
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane(0f));

        var emissiveSun = world.CreateEntity();
        world.AddComponent(emissiveSun, new TransformComponent(
            new Vector3(10f, 8f, -6f),
            Quaternion.Identity,
            new Vector3(2.2f)
        ));
        world.AddComponent(emissiveSun, new MeshComponent(sphereHandle));
        world.AddComponent(emissiveSun, MaterialComponent.CreateEmissive(new Vector3(1f, 0.7f, 0.3f), 5f));
        world.AddComponent(emissiveSun, new RaytracerMeshComponent(enabled: true) { StaticOnly = true });

        var unpixTarget = world.CreateEntity();
        world.AddComponent(unpixTarget, new TransformComponent(
            new Vector3(0f, 2.5f, -4f),
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
            UseRaytracing = true
        });
        world.AddComponent(unpixTarget, new RaytracerMeshComponent(enabled: true) { StaticOnly = false });
        world.AddComponent(unpixTarget, new UnpixComponent
        {
            CubeSize = 0.35f,
            DissolveDuration = 2.5f,
            StartDelay = 1.0f,
            VelocityScale = 1.0f,
            HideSource = true,
            MaxCubes = 2000
        });

        CreateStaticColumn(world, cubeHandle, new Vector3(-8f, -2f, -8f), new Vector3(2f, 5f, 2f), new Vector4(0.35f, 0.4f, 0.45f, 1f));
        CreateStaticColumn(world, cubeHandle, new Vector3(8f, -2f, -8f), new Vector3(2f, 4f, 2f), new Vector4(0.45f, 0.35f, 0.25f, 1f));
        CreateStaticColumn(world, cubeHandle, new Vector3(0f, -2f, 10f), new Vector3(3f, 3f, 3f), new Vector4(0.3f, 0.5f, 0.35f, 1f));

        CreateDynamicSphere(world, sphereHandle, new Vector3(-3f, 6f, -2f), 0.6f, new Vector4(0.2f, 0.6f, 0.9f, 1f));
        CreateDynamicSphere(world, sphereHandle, new Vector3(2f, 8f, -1f), 0.5f, new Vector4(0.9f, 0.4f, 0.2f, 1f));
        CreateDynamicBox(world, cubeHandle, new Vector3(4f, 7f, 2f), new Vector3(0.8f, 0.8f, 0.8f), new Vector4(0.8f, 0.8f, 0.25f, 1f));
        CreateDynamicBox(world, cubeHandle, new Vector3(-5f, 9f, 3f), new Vector3(1f, 0.6f, 1.2f), new Vector4(0.6f, 0.25f, 0.7f, 1f));
    }

    private static void CreateStaticColumn(World world, ResourceHandle<Mesh> cubeHandle, Vector3 position, Vector3 size, Vector4 color)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position + new Vector3(0f, size.Y * 0.5f, 0f), Quaternion.Identity, size));
        world.AddComponent(entity, new MeshComponent(cubeHandle));
        world.AddComponent(entity, new MaterialComponent
        {
            BaseColor = color,
            Metallic = 0.1f,
            Roughness = 0.7f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = true
        });
        world.AddComponent(entity, new RaytracerMeshComponent(enabled: true) { StaticOnly = true });
        world.AddComponent(entity, RigidBodyComponent.CreateStatic());
        world.AddComponent(entity, ColliderComponent.CreateBox(size * 0.5f));
    }

    private static void CreateDynamicSphere(World world, ResourceHandle<Mesh> sphereHandle, Vector3 position, float radius, Vector4 color)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position, Quaternion.Identity, new Vector3(radius)));
        world.AddComponent(entity, new MeshComponent(sphereHandle));
        world.AddComponent(entity, new MaterialComponent
        {
            BaseColor = color,
            Metallic = 0.05f,
            Roughness = 0.4f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = true
        });
        world.AddComponent(entity, new RaytracerMeshComponent(enabled: true) { StaticOnly = false });
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1.2f));
        world.AddComponent(entity, ColliderComponent.CreateSphere(radius));
    }

    private static void CreateDynamicBox(World world, ResourceHandle<Mesh> cubeHandle, Vector3 position, Vector3 size, Vector4 color)
    {
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(position, Quaternion.Identity, size));
        world.AddComponent(entity, new MeshComponent(cubeHandle));
        world.AddComponent(entity, new MaterialComponent
        {
            BaseColor = color,
            Metallic = 0.15f,
            Roughness = 0.5f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = true
        });
        world.AddComponent(entity, new RaytracerMeshComponent(enabled: true) { StaticOnly = false });
        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1.5f));
        world.AddComponent(entity, ColliderComponent.CreateBox(size * 0.5f));
    }
}
