using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleTestScene;

/// <summary>
/// Combined test scene for raytraced rendering with Unpix dissolution and laser systems.
/// </summary>
public class TestSceneModule : IGameModule
{
    public string Name => "TestScene";

    public void BuildScene(GameContext context)
    {
        var world = context.World;
        var resources = context.Resources;

        // Create meshes - larger ground plane for expanded scene
        resources.Add("mesh_plane", Mesh.CreatePlane(120f, 120f, 8, 8));
        resources.Add("mesh_cube", Mesh.CreateCube(1f));
        resources.Add("mesh_sphere", Mesh.CreateSphere(1f, 32, 16));
        resources.Add("mesh_small_sphere", Mesh.CreateSphere(0.3f, 16, 8));

        var planeHandle = resources.GetHandle<Mesh>("mesh_plane");
        var cubeHandle = resources.GetHandle<Mesh>("mesh_cube");
        var sphereHandle = resources.GetHandle<Mesh>("mesh_sphere");
        var smallSphereHandle = resources.GetHandle<Mesh>("mesh_small_sphere");

        // === RENDER SETTINGS - RAYTRACED ===
        // Accumulation disabled for dynamic scene (physics, lasers)
        var settingsEntity = world.CreateEntity();
        world.AddComponent(settingsEntity, new RenderSettingsComponent(RenderMode.Raytraced)
        {
            Enabled = true,
            RaysPerPixel = 1,
            MaxBounces = 2,
            SamplesPerFrame = 1,
            Accumulate = false,  // Disabled - scene has dynamic objects
            Denoise = false,
            Preset = RaytracerQualityPreset.Custom
        });

        // === BLACK HOLE (for gravitational lensing) ===
        var blackHole = world.CreateEntity();
        world.AddComponent(blackHole, new TransformComponent(
            new Vector3(0f, 5f, 5f),
            Quaternion.Identity,
            new Vector3(1.5f) // Visual size of the black hole sphere
        ));
        world.AddComponent(blackHole, new MeshComponent(sphereHandle));
        world.AddComponent(blackHole, new MaterialComponent
        {
            BaseColor = new Vector4(0.02f, 0.02f, 0.02f, 1f), // Nearly black
            Metallic = 0f,
            Roughness = 1f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = true
        });
        world.AddComponent(blackHole, new RaytracerMeshComponent(true) { StaticOnly = true });
        // Mass of 1.5 gives Schwarzschild radius of 3, disk inner=9, outer=45
        // This creates visible lensing at reasonable scene scale
        world.AddComponent(blackHole, GravitySourceComponent.CreateBlackHole(1.5f));
        
        // Accretion disk around the black hole (bright ring)
        // With mass 1.5: rs=3, disk inner=9, outer=45
        // We'll create a visible disk that matches
        var accretionDisk = world.CreateEntity();
        world.AddComponent(accretionDisk, new TransformComponent(
            new Vector3(0f, 5f, 5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI * 0.5f), // Horizontal disk
            new Vector3(12f, 12f, 0.15f) // Disk sized to match ~inner radius of 9
        ));
        world.AddComponent(accretionDisk, new MeshComponent(sphereHandle)); // Use sphere, squashed flat
        world.AddComponent(accretionDisk, new MaterialComponent
        {
            BaseColor = new Vector4(1f, 0.6f, 0.2f, 1f),
            Metallic = 0f,
            Roughness = 1f,
            EmissiveColor = new Vector3(1f, 0.5f, 0.1f) * 40f, // Bright orange glow
            UseRaytracing = true
        });
        world.AddComponent(accretionDisk, new RaytracerMeshComponent(true) { StaticOnly = true });

        // === GROUND PLANE ===
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent(new Vector3(0f, -2f, 0f)));
        world.AddComponent(ground, new MeshComponent(planeHandle));
        world.AddComponent(ground, new MaterialComponent
        {
            BaseColor = new Vector4(0.3f, 0.3f, 0.35f, 1f),
            Metallic = 0.02f,
            Roughness = 0.85f,
            EmissiveColor = Vector3.Zero,
            UseRaytracing = true
        });
        world.AddComponent(ground, new RaytracerMeshComponent(true) { StaticOnly = true });
        var groundBody = RigidBodyComponent.CreateStatic();
        groundBody.Friction = 1.5f;
        world.AddComponent(ground, groundBody);
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane(0f));

        // === BACK WALL (for laser hits) - moved farther back ===
        var backWall = world.CreateEntity();
        world.AddComponent(backWall, new TransformComponent(
            new Vector3(0f, 5f, 35f),
            Quaternion.Identity,
            new Vector3(50f, 20f, 0.5f)
        ));
        world.AddComponent(backWall, new MeshComponent(cubeHandle));
        world.AddComponent(backWall, new MaterialComponent
        {
            BaseColor = new Vector4(0.4f, 0.35f, 0.3f, 1f),
            Metallic = 0.0f,
            Roughness = 0.9f,
            UseRaytracing = true
        });
        world.AddComponent(backWall, new RaytracerMeshComponent(true) { StaticOnly = true });
        world.AddComponent(backWall, RigidBodyComponent.CreateStatic());
        world.AddComponent(backWall, ColliderComponent.CreateBox(new Vector3(15f, 6f, 0.25f)));

        // === UNPIX TARGET SPHERE - moved to far left ===
        var unpixSphere = world.CreateEntity();
        world.AddComponent(unpixSphere, new TransformComponent(
            new Vector3(-25f, 2f, -10f),
            Quaternion.Identity,
            new Vector3(2.5f)
        ));
        world.AddComponent(unpixSphere, new MeshComponent(sphereHandle));
        world.AddComponent(unpixSphere, new MaterialComponent
        {
            BaseColor = new Vector4(0.9f, 0.85f, 0.7f, 1f),
            Metallic = 0.1f,
            Roughness = 0.3f,
            EmissiveColor = new Vector3(0.1f, 0.08f, 0.05f),
            UseRaytracing = true
        });
        world.AddComponent(unpixSphere, new RaytracerMeshComponent(true) { StaticOnly = false });
        world.AddComponent(unpixSphere, new UnpixComponent
        {
            CubeSize = 0.2f,
            DissolveDuration = 8.0f,
            StartDelay = 2.0f,
            VelocityScale = 0.8f,
            SpawnOnStart = true,
            HideSource = true,
            MaxCubes = 300,
            UsePhysics = true,
            CubeMass = 0.15f,
            CubeRestitution = 0.3f,
            CubeFriction = 1.2f,
            InitialImpulseScale = 2.0f
        });

        // === SECOND UNPIX TARGET - moved to far right ===
        var unpixSphere2 = world.CreateEntity();
        world.AddComponent(unpixSphere2, new TransformComponent(
            new Vector3(25f, 1f, -8f),
            Quaternion.Identity,
            new Vector3(1.5f)
        ));
        world.AddComponent(unpixSphere2, new MeshComponent(sphereHandle));
        world.AddComponent(unpixSphere2, new MaterialComponent
        {
            BaseColor = new Vector4(0.3f, 0.7f, 0.9f, 1f),
            Metallic = 0.6f,
            Roughness = 0.15f,
            EmissiveColor = new Vector3(0.02f, 0.05f, 0.1f),
            UseRaytracing = true
        });
        world.AddComponent(unpixSphere2, new RaytracerMeshComponent(true) { StaticOnly = false });
        world.AddComponent(unpixSphere2, new UnpixComponent
        {
            CubeSize = 0.15f,
            DissolveDuration = 6.0f,
            StartDelay = 4.0f,
            VelocityScale = 1.2f,
            SpawnOnStart = true,
            HideSource = true,
            MaxCubes = 150,
            UsePhysics = true,
            CubeMass = 0.1f,
            CubeRestitution = 0.4f,
            CubeFriction = 1.0f,
            InitialImpulseScale = 3.0f
        });

        // === RED BEAM LASER (shoots PAST the black hole to show gravitational lensing) ===
        // Black hole is at (0, 5, 5), so we shoot from far left toward it
        var beamEmitter = world.CreateEntity();
        world.AddComponent(beamEmitter, new TransformComponent(
            new Vector3(-30f, 5f, 3f),  // Far left, same height as black hole
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0f), // Point straight along +X
            new Vector3(0.6f)
        ));
        world.AddComponent(beamEmitter, new MeshComponent(cubeHandle));
        world.AddComponent(beamEmitter, new MaterialComponent
        {
            BaseColor = new Vector4(0.2f, 0.2f, 0.25f, 1f),
            Metallic = 0.8f,
            Roughness = 0.2f,
            EmissiveColor = new Vector3(1f, 0.1f, 0.1f) * 5f,
            UseRaytracing = true
        });
        world.AddComponent(beamEmitter, new RaytracerMeshComponent(true) { StaticOnly = true });
        var beamLaser = LaserEmitterComponent.CreateBeam(
            range: 60f,
            width: 0.15f,  // Slightly wider for visibility
            color: new Vector4(1f, 0.15f, 0.1f, 1f)
        );
        beamLaser.CoreColor = new Vector4(1f, 0.8f, 0.6f, 1f);
        beamLaser.CoreWidth = 0.4f;
        beamLaser.Damage = 8f;
        beamLaser.PushForce = 15f;
        beamLaser.CanRedirect = true;
        beamLaser.MaxBounces = 4;
        world.AddComponent(beamEmitter, beamLaser);
        
        // (Removed extra lasers for performance - one beam is enough to show the effect)

        // === MIRROR (angled to redirect beams) - moved far right ===
        var mirror = world.CreateEntity();
        world.AddComponent(mirror, new TransformComponent(
            new Vector3(20f, 2f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.6f),
            new Vector3(0.15f, 3f, 2.5f)
        ));
        world.AddComponent(mirror, new MeshComponent(cubeHandle));
        world.AddComponent(mirror, new MaterialComponent
        {
            BaseColor = new Vector4(0.95f, 0.95f, 1f, 1f),
            Metallic = 1.0f,
            Roughness = 0.02f,
            UseRaytracing = true
        });
        world.AddComponent(mirror, new RaytracerMeshComponent(true) { StaticOnly = true });
        world.AddComponent(mirror, RigidBodyComponent.CreateStatic());
        world.AddComponent(mirror, ColliderComponent.CreateBox(new Vector3(0.075f, 1.5f, 1.25f)));
        world.AddComponent(mirror, new LaserRedirectComponent
        {
            Type = LaserRedirectComponent.RedirectType.Mirror,
            Efficiency = 0.9f,
            TintColor = new Vector4(0.95f, 0.98f, 1f, 0.05f)
        });

        // === DYNAMIC TARGET CUBES (can be pushed by lasers) - spread around edges ===
        CreateTargetCube(world, cubeHandle, new Vector3(-20f, 0f, 15f), new Vector4(0.9f, 0.4f, 0.2f, 1f));
        CreateTargetCube(world, cubeHandle, new Vector3(0f, 0f, 25f), new Vector4(0.3f, 0.8f, 0.4f, 1f));
        CreateTargetCube(world, cubeHandle, new Vector3(20f, 0f, 15f), new Vector4(0.4f, 0.5f, 0.9f, 1f));
        CreateTargetCube(world, cubeHandle, new Vector3(-15f, 0f, 20f), new Vector4(0.9f, 0.8f, 0.2f, 1f));
        CreateTargetCube(world, cubeHandle, new Vector3(15f, 0f, 20f), new Vector4(0.7f, 0.3f, 0.7f, 1f));

        // === EMISSIVE LIGHT SPHERES (for raytracer illumination) - spread wider ===
        CreateLightSphere(world, smallSphereHandle, new Vector3(-25f, 10f, -10f), new Vector3(1f, 0.9f, 0.8f) * 60f);
        CreateLightSphere(world, smallSphereHandle, new Vector3(25f, 12f, -5f), new Vector3(0.8f, 0.9f, 1f) * 50f);
        CreateLightSphere(world, smallSphereHandle, new Vector3(0f, 15f, -15f), new Vector3(1f, 1f, 1f) * 70f);

        // === REFLECTIVE SPHERE (shows raytracing reflections) - moved far right ===
        var reflectiveSphere = world.CreateEntity();
        world.AddComponent(reflectiveSphere, new TransformComponent(
            new Vector3(18f, 0.5f, -12f),
            Quaternion.Identity,
            new Vector3(1.5f)
        ));
        world.AddComponent(reflectiveSphere, new MeshComponent(sphereHandle));
        world.AddComponent(reflectiveSphere, new MaterialComponent
        {
            BaseColor = new Vector4(0.95f, 0.95f, 0.98f, 1f),
            Metallic = 1.0f,
            Roughness = 0.05f,
            UseRaytracing = true
        });
        world.AddComponent(reflectiveSphere, new RaytracerMeshComponent(true) { StaticOnly = true });
        world.AddComponent(reflectiveSphere, RigidBodyComponent.CreateStatic());
        world.AddComponent(reflectiveSphere, ColliderComponent.CreateSphere(0.75f));
    }

    private static void CreateTargetCube(World world, ResourceHandle<Mesh> cubeHandle, Vector3 position, Vector4 color)
    {
        var target = world.CreateEntity();
        world.AddComponent(target, new TransformComponent(position, Quaternion.Identity, new Vector3(0.9f)));
        world.AddComponent(target, new MeshComponent(cubeHandle));
        world.AddComponent(target, new MaterialComponent
        {
            BaseColor = color,
            Metallic = 0.1f,
            Roughness = 0.5f,
            UseRaytracing = true
        });
        world.AddComponent(target, new RaytracerMeshComponent(true) { StaticOnly = false });
        var body = RigidBodyComponent.CreateDynamic(1.5f);
        body.Friction = 0.9f;
        body.LinearDamping = 0.15f;
        world.AddComponent(target, body);
        world.AddComponent(target, ColliderComponent.CreateBox(new Vector3(0.45f)));
    }

    private static void CreateLightSphere(World world, ResourceHandle<Mesh> sphereHandle, Vector3 position, Vector3 emissive)
    {
        var light = world.CreateEntity();
        world.AddComponent(light, new TransformComponent(position, Quaternion.Identity, Vector3.One));
        world.AddComponent(light, new MeshComponent(sphereHandle));
        world.AddComponent(light, new MaterialComponent
        {
            BaseColor = new Vector4(1f, 1f, 1f, 1f),
            Metallic = 0f,
            Roughness = 1f,
            EmissiveColor = emissive,
            UseRaytracing = true
        });
        world.AddComponent(light, new RaytracerMeshComponent(true) { StaticOnly = true });
    }
}
