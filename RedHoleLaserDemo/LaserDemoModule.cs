using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleLaserDemo;

public class LaserDemoModule : IGameModule
{
    public string Name => "LaserDemo";

    public void BuildScene(GameContext context)
    {
        var world = context.World;
        var resources = context.Resources;

        // Create meshes
        resources.Add("mesh_plane", Mesh.CreatePlane(50f, 50f, 4, 4));
        resources.Add("mesh_cube", Mesh.CreateCube(1f));
        resources.Add("mesh_sphere", Mesh.CreateSphere(0.5f, 16, 8));

        var planeHandle = resources.GetHandle<Mesh>("mesh_plane");
        var cubeHandle = resources.GetHandle<Mesh>("mesh_cube");
        var sphereHandle = resources.GetHandle<Mesh>("mesh_sphere");

        // Render settings
        var settingsEntity = world.CreateEntity();
        world.AddComponent(settingsEntity, new RenderSettingsComponent(RenderMode.Rasterized));

        // Ground plane
        var ground = world.CreateEntity();
        world.AddComponent(ground, new TransformComponent(new Vector3(0f, -2f, 0f)));
        world.AddComponent(ground, new MeshComponent(planeHandle));
        world.AddComponent(ground, new MaterialComponent
        {
            BaseColor = new Vector4(0.15f, 0.15f, 0.2f, 1f),
            Metallic = 0.1f,
            Roughness = 0.8f
        });
        var groundBody = RigidBodyComponent.CreateStatic();
        groundBody.Friction = 1.0f;
        world.AddComponent(ground, groundBody);
        world.AddComponent(ground, ColliderComponent.CreateGroundPlane(0f));

        // === BEAM LASER EMITTER ===
        var beamEmitter = world.CreateEntity();
        world.AddComponent(beamEmitter, new TransformComponent(
            new Vector3(-5f, 1f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.25f), // Angled
            Vector3.One
        ));
        world.AddComponent(beamEmitter, new MeshComponent(cubeHandle));
        world.AddComponent(beamEmitter, new MaterialComponent
        {
            BaseColor = new Vector4(0.3f, 0.3f, 0.3f, 1f),
            EmissiveColor = new Vector3(1f, 0.2f, 0.2f) * 0.5f
        });
        var beamLaser = LaserEmitterComponent.CreateBeam(
            range: 50f,
            width: 0.15f,
            color: new Vector4(1f, 0.1f, 0.1f, 1f)
        );
        beamLaser.Damage = 5f;
        beamLaser.CanRedirect = true;
        beamLaser.MaxBounces = 3;
        world.AddComponent(beamEmitter, beamLaser);

        // === PULSE LASER EMITTER ===
        var pulseEmitter = world.CreateEntity();
        world.AddComponent(pulseEmitter, new TransformComponent(
            new Vector3(5f, 1f, -5f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI * 0.25f),
            Vector3.One
        ));
        world.AddComponent(pulseEmitter, new MeshComponent(cubeHandle));
        world.AddComponent(pulseEmitter, new MaterialComponent
        {
            BaseColor = new Vector4(0.3f, 0.3f, 0.3f, 1f),
            EmissiveColor = new Vector3(0.2f, 0.4f, 1f) * 0.5f
        });
        var pulseLaser = LaserEmitterComponent.CreatePulse(
            speed: 30f,
            length: 0.8f,
            fireRate: 3f,
            color: new Vector4(0.2f, 0.5f, 1f, 1f)
        );
        pulseLaser.AutoFire = true;
        pulseLaser.MaxBounces = 2;
        world.AddComponent(pulseEmitter, pulseLaser);

        // === SCANNING LASER EMITTER ===
        var scanEmitter = world.CreateEntity();
        world.AddComponent(scanEmitter, new TransformComponent(
            new Vector3(0f, 3f, -8f),
            Quaternion.Identity,
            Vector3.One
        ));
        world.AddComponent(scanEmitter, new MeshComponent(sphereHandle));
        world.AddComponent(scanEmitter, new MaterialComponent
        {
            BaseColor = new Vector4(0.2f, 0.2f, 0.2f, 1f),
            EmissiveColor = new Vector3(0.2f, 1f, 0.3f) * 0.5f
        });
        var scanLaser = LaserEmitterComponent.CreateScanning(
            scanSpeed: 60f,
            scanAngle: 120f,
            color: new Vector4(0.1f, 1f, 0.2f, 1f)
        );
        world.AddComponent(scanEmitter, scanLaser);

        // === MIRROR (redirect surface) ===
        var mirror = world.CreateEntity();
        world.AddComponent(mirror, new TransformComponent(
            new Vector3(0f, 1f, 0f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f),
            new Vector3(0.2f, 2f, 2f)
        ));
        world.AddComponent(mirror, new MeshComponent(cubeHandle));
        world.AddComponent(mirror, new MaterialComponent
        {
            BaseColor = new Vector4(0.8f, 0.85f, 0.9f, 1f),
            Metallic = 1.0f,
            Roughness = 0.05f
        });
        world.AddComponent(mirror, RigidBodyComponent.CreateStatic());
        world.AddComponent(mirror, ColliderComponent.CreateBox(new Vector3(0.1f, 1f, 1f)));
        world.AddComponent(mirror, new LaserRedirectComponent
        {
            Type = LaserRedirectComponent.RedirectType.Mirror,
            Efficiency = 0.95f,
            TintColor = new Vector4(0.9f, 0.95f, 1f, 0.1f)
        });

        // === TARGET CUBES (can be hit by lasers) ===
        for (int i = 0; i < 5; i++)
        {
            var target = world.CreateEntity();
            float angle = i * MathF.PI * 2f / 5f;
            float radius = 6f;
            world.AddComponent(target, new TransformComponent(
                new Vector3(MathF.Cos(angle) * radius, 0f, MathF.Sin(angle) * radius + 5f),
                Quaternion.Identity,
                new Vector3(0.8f)
            ));
            world.AddComponent(target, new MeshComponent(cubeHandle));
            world.AddComponent(target, new MaterialComponent
            {
                BaseColor = new Vector4(0.8f, 0.6f, 0.2f, 1f),
                Roughness = 0.4f
            });
            var targetBody = RigidBodyComponent.CreateDynamic(2f);
            targetBody.Friction = 0.8f;
            targetBody.LinearDamping = 0.1f;
            world.AddComponent(target, targetBody);
            world.AddComponent(target, ColliderComponent.CreateBox(new Vector3(0.4f)));
        }
    }
}
