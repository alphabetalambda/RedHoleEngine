using System;
using System.Collections.Generic;
using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Rendering;

public sealed class UnpixSystem : GameSystem
{
    private ResourceManager? _resources;
    private ResourceHandle<Mesh> _cubeHandle;
    private readonly Random _random = new();

    public void Initialize(ResourceManager resources)
    {
        _resources = resources;
        if (!_resources.IsLoaded<Mesh>("mesh_unpix_cube"))
        {
            _resources.Add("mesh_unpix_cube", Mesh.CreateCube(1f));
        }
        _cubeHandle = _resources.GetHandle<Mesh>("mesh_unpix_cube");
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        UpdatePieces(deltaTime);
        SpawnSources(deltaTime);
    }

    private void SpawnSources(float deltaTime)
    {
        foreach (var entity in World!.Query<MeshComponent, TransformComponent, UnpixComponent>())
        {
            ref var unpix = ref World.GetComponent<UnpixComponent>(entity);
            unpix.Elapsed += deltaTime;

            if (!unpix.SpawnOnStart || unpix.Spawned)
                continue;

            if (unpix.Elapsed < unpix.StartDelay)
                continue;

            if (!World.HasComponent<MeshComponent>(entity))
                continue;

            ref var meshComponent = ref World.GetComponent<MeshComponent>(entity);
            var mesh = meshComponent.MeshHandle.Get();
            if (mesh == null || mesh.Vertices.Length == 0 || mesh.Indices.Length == 0)
                continue;

            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            SpawnCubes(entity, mesh, transform, unpix);
            unpix.Spawned = true;

            if (unpix.HideSource)
            {
                // Remove the mesh component entirely to hide the source
                World.RemoveComponent<MeshComponent>(entity);
                if (World.HasComponent<RaytracerMeshComponent>(entity))
                {
                    World.RemoveComponent<RaytracerMeshComponent>(entity);
                }
            }
        }
    }

    private void SpawnCubes(Entity source, Mesh mesh, TransformComponent transform, UnpixComponent unpix)
    {
        if (_resources == null)
            return;

        var boundsMin = mesh.BoundsMin;
        var boundsMax = mesh.BoundsMax;
        var cubeSize = Math.Max(0.05f, unpix.CubeSize);

        // Build triangle data for surface projection
        var triangles = BuildTriangleData(mesh);
        int spawned = 0;

        // Calculate the center of the source mesh for explosion direction
        var localMeshCenter = (boundsMin + boundsMax) * 0.5f;
        var sourceCenter = Vector3.Transform(localMeshCenter, transform.WorldMatrix);

        for (float z = boundsMin.Z; z <= boundsMax.Z; z += cubeSize)
        {
            for (float y = boundsMin.Y; y <= boundsMax.Y; y += cubeSize)
            {
                for (float x = boundsMin.X; x <= boundsMax.X; x += cubeSize)
                {
                    if (unpix.MaxCubes > 0 && spawned >= unpix.MaxCubes)
                        return;

                    var gridPoint = new Vector3(x + cubeSize * 0.5f, y + cubeSize * 0.5f, z + cubeSize * 0.5f);
                    
                    // Find closest point on mesh surface
                    if (!FindClosestSurfacePoint(gridPoint, triangles, cubeSize * 1.5f, out var surfacePoint, out var surfaceNormal))
                        continue;

                    // Transform to world space
                    var worldCenter = Vector3.Transform(surfacePoint, transform.WorldMatrix);
                    var worldNormal = Vector3.Normalize(Vector3.TransformNormal(surfaceNormal, transform.WorldMatrix));
                    var worldScale = transform.LocalScale * cubeSize;
                    var cubeEntity = World!.CreateEntity();

                    World.AddComponent(cubeEntity, new TransformComponent(worldCenter, transform.Rotation, worldScale));
                    World.AddComponent(cubeEntity, new MeshComponent(_cubeHandle));

                    if (World.HasComponent<MaterialComponent>(source))
                    {
                        var material = World.GetComponent<MaterialComponent>(source);
                        World.AddComponent(cubeEntity, material);
                    }
                    else
                    {
                        World.AddComponent(cubeEntity, MaterialComponent.Default);
                    }

                    World.AddComponent(cubeEntity, new RaytracerMeshComponent(true) { StaticOnly = false });

                    // Use surface normal as primary explosion direction (follows mesh shape)
                    var outwardDir = worldNormal;
                    
                    // Add some randomness while keeping general outward direction
                    outwardDir += new Vector3(
                        RandomRange(-0.2f, 0.2f),
                        RandomRange(-0.1f, 0.3f),  // Slight upward bias
                        RandomRange(-0.2f, 0.2f));
                    outwardDir = Vector3.Normalize(outwardDir);

                    if (unpix.UsePhysics)
                    {
                        // Add physics components
                        var rigidBody = RigidBodyComponent.CreateDynamic(unpix.CubeMass);
                        rigidBody.Restitution = unpix.CubeRestitution;
                        rigidBody.Friction = unpix.CubeFriction;
                        rigidBody.LinearDamping = 0.15f;   // More air drag
                        rigidBody.AngularDamping = 0.2f;
                        World.AddComponent(cubeEntity, rigidBody);

                        // Add box collider matching the scaled cube size
                        // The mesh is 1x1x1, scaled by worldScale, so half-extent is worldScale/2
                        float halfExtent = worldScale.X * 0.5f;
                        var collider = ColliderComponent.CreateBox(new Vector3(halfExtent, halfExtent, halfExtent));
                        World.AddComponent(cubeEntity, collider);

                        // Store initial impulse to apply after physics registration
                        var impulse = outwardDir * unpix.InitialImpulseScale * unpix.VelocityScale;
                        
                        World.AddComponent(cubeEntity, new UnpixPieceComponent
                        {
                            Age = 0f,
                            Lifetime = Math.Max(0.1f, unpix.DissolveDuration),
                            Velocity = impulse, // Store impulse to apply on first update
                            StartScale = worldScale,
                            UsePhysics = true
                        });
                    }
                    else
                    {
                        // Legacy kinematic mode
                        var velocity = outwardDir * unpix.VelocityScale;

                        World.AddComponent(cubeEntity, new UnpixPieceComponent
                        {
                            Age = 0f,
                            Lifetime = Math.Max(0.1f, unpix.DissolveDuration),
                            Velocity = velocity,
                            StartScale = worldScale,
                            UsePhysics = false
                        });
                    }

                    spawned++;
                }
            }
        }
    }

    private struct TriangleData
    {
        public Vector3 V0, V1, V2;
        public Vector3 Normal;
        public Vector3 Center;
    }

    private static List<TriangleData> BuildTriangleData(Mesh mesh)
    {
        var list = new List<TriangleData>(mesh.Indices.Length / 3);
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            var v0 = mesh.Vertices[mesh.Indices[i]].Position;
            var v1 = mesh.Vertices[mesh.Indices[i + 1]].Position;
            var v2 = mesh.Vertices[mesh.Indices[i + 2]].Position;

            var edge1 = v1 - v0;
            var edge2 = v2 - v0;
            var normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
            var center = (v0 + v1 + v2) / 3f;

            list.Add(new TriangleData { V0 = v0, V1 = v1, V2 = v2, Normal = normal, Center = center });
        }
        return list;
    }

    private static bool FindClosestSurfacePoint(Vector3 point, List<TriangleData> triangles, float maxDist, out Vector3 closestPoint, out Vector3 closestNormal)
    {
        closestPoint = point;
        closestNormal = Vector3.UnitY;
        float bestDistSq = maxDist * maxDist;
        bool found = false;

        foreach (var tri in triangles)
        {
            var candidate = ClosestPointOnTriangle(point, tri.V0, tri.V1, tri.V2);
            var distSq = Vector3.DistanceSquared(point, candidate);
            
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                closestPoint = candidate;
                closestNormal = tri.Normal;
                found = true;
            }
        }

        return found;
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // Check if P in vertex region outside A
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        // Check if P in vertex region outside B
        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        // Check if P in edge region of AB
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
        {
            float v = d1 / (d1 - d3);
            return a + v * ab;
        }

        // Check if P in vertex region outside C
        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        // Check if P in edge region of AC
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
        {
            float w = d2 / (d2 - d6);
            return a + w * ac;
        }

        // Check if P in edge region of BC
        float va = d3 * d6 - d5 * d4;
        if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
        {
            float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
            return b + w * (c - b);
        }

        // P inside face region
        float denom = 1f / (va + vb + vc);
        float vv = vb * denom;
        float ww = vc * denom;
        return a + ab * vv + ac * ww;
    }

    private void UpdatePieces(float deltaTime)
    {
        foreach (var entity in World!.Query<UnpixPieceComponent, TransformComponent>())
        {
            ref var piece = ref World.GetComponent<UnpixPieceComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            // Apply initial impulse on first frame for physics cubes
            if (piece.UsePhysics && piece.Age == 0f && piece.Velocity.LengthSquared() > 0.01f)
            {
                if (World.HasComponent<RigidBodyComponent>(entity))
                {
                    ref var rb = ref World.GetComponent<RigidBodyComponent>(entity);
                    rb.ApplyImpulse(piece.Velocity);
                    
                    // Add some random angular velocity for tumbling
                    rb.ApplyAngularImpulse(new Vector3(
                        RandomRange(-2f, 2f),
                        RandomRange(-2f, 2f),
                        RandomRange(-2f, 2f)));
                    
                    piece.Velocity = Vector3.Zero; // Clear so we don't apply again
                }
            }

            piece.Age += deltaTime;
            float t = Math.Clamp(piece.Age / piece.Lifetime, 0f, 1f);
            
            // Scale down over time
            var scale = piece.StartScale * (1f - t);
            transform.LocalScale = scale;
            
            // For non-physics mode, manually move the piece
            if (!piece.UsePhysics)
            {
                transform.Position += piece.Velocity * deltaTime;
            }

            // Destroy when lifetime expires
            if (t >= 1f)
            {
                World.DestroyEntity(entity);
            }
        }
    }

    private float RandomRange(float min, float max)
    {
        return (float)(_random.NextDouble() * (max - min) + min);
    }
}
