using System;
using System.Collections.Generic;
using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Physics;

namespace RedHoleEngine.Rendering;

/// <summary>
/// System that handles laser simulation, physics raycasting, and pulse movement.
/// Creates LaserSegmentComponents for the renderer to draw.
/// </summary>
public sealed class LaserSystem : GameSystem
{
    private PhysicsSystem? _physics;
    private readonly Random _random = new();
    private readonly List<Entity> _segmentsToCleanup = new();

    public override int Priority => -40; // Run after physics, before rendering

    public void Initialize(PhysicsSystem physics)
    {
        _physics = physics;
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        // Clean up old segments from last frame
        CleanupSegments();

        // Update emitters
        UpdateEmitters(deltaTime);

        // Update active pulses
        UpdatePulses(deltaTime);
    }

    private void CleanupSegments()
    {
        _segmentsToCleanup.Clear();
        
        foreach (var entity in World!.Query<LaserSegmentComponent>())
        {
            _segmentsToCleanup.Add(entity);
        }
        
        foreach (var entity in _segmentsToCleanup)
        {
            World.DestroyEntity(entity);
        }
    }

    private void UpdateEmitters(float deltaTime)
    {
        foreach (var entity in World!.Query<LaserEmitterComponent, TransformComponent>())
        {
            ref var emitter = ref World.GetComponent<LaserEmitterComponent>(entity);
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            if (!emitter.Enabled)
                continue;

            switch (emitter.Type)
            {
                case LaserType.Beam:
                    UpdateBeamEmitter(entity, ref emitter, ref transform);
                    break;

                case LaserType.Pulse:
                    UpdatePulseEmitter(entity, ref emitter, ref transform, deltaTime);
                    break;

                case LaserType.Scanning:
                    UpdateScanningEmitter(entity, ref emitter, ref transform, deltaTime);
                    break;
            }
        }
    }

    private void UpdateBeamEmitter(Entity entity, ref LaserEmitterComponent emitter, ref TransformComponent transform)
    {
        if (!emitter.FireOnStart)
            return;

        var origin = transform.Position;
        var direction = transform.Forward;

        TraceBeam(entity, origin, direction, ref emitter, emitter.MaxBounces);
    }

    private void UpdatePulseEmitter(Entity entity, ref LaserEmitterComponent emitter, ref TransformComponent transform, float deltaTime)
    {
        if (!emitter.AutoFire && !emitter.FireOnStart)
            return;

        emitter.FireTimer += deltaTime;
        float fireInterval = 1f / emitter.FireRate;

        while (emitter.FireTimer >= fireInterval)
        {
            emitter.FireTimer -= fireInterval;
            FirePulse(entity, ref emitter, ref transform);
        }

        // Reset FireOnStart after first shot
        if (emitter.FireOnStart)
            emitter.FireOnStart = false;
    }

    private void UpdateScanningEmitter(Entity entity, ref LaserEmitterComponent emitter, ref TransformComponent transform, float deltaTime)
    {
        if (!emitter.FireOnStart)
            return;

        // Update scan angle
        emitter.ScanAngleCurrent += emitter.ScanSpeed * emitter.ScanDirection * deltaTime;

        // Reverse direction at limits
        float halfAngle = emitter.ScanAngle * 0.5f;
        if (emitter.ScanAngleCurrent >= halfAngle)
        {
            emitter.ScanAngleCurrent = halfAngle;
            emitter.ScanDirection = -1;
        }
        else if (emitter.ScanAngleCurrent <= -halfAngle)
        {
            emitter.ScanAngleCurrent = -halfAngle;
            emitter.ScanDirection = 1;
        }

        // Calculate scan direction
        var baseDir = transform.Forward;
        var rotation = Quaternion.CreateFromAxisAngle(
            Vector3.Transform(emitter.ScanAxis, transform.Rotation),
            emitter.ScanAngleCurrent * MathF.PI / 180f);
        var scanDir = Vector3.Transform(baseDir, rotation);

        TraceBeam(entity, transform.Position, scanDir, ref emitter, 0);
    }

    private void FirePulse(Entity emitterEntity, ref LaserEmitterComponent emitter, ref TransformComponent transform)
    {
        var pulseEntity = World!.CreateEntity();

        World.AddComponent(pulseEntity, new LaserPulseComponent
        {
            Position = transform.Position,
            Direction = transform.Forward,
            Speed = emitter.PulseSpeed,
            Length = emitter.PulseLength,
            DistanceTraveled = 0f,
            MaxRange = emitter.MaxRange,
            BouncesRemaining = emitter.MaxBounces,
            RedirectAngle = emitter.RedirectAngle,
            Width = emitter.BeamWidth,
            BeamColor = emitter.BeamColor,
            CoreColor = emitter.CoreColor,
            CoreWidth = emitter.CoreWidth,
            CollisionMask = emitter.CollisionMask,
            Damage = emitter.Damage,
            PushForce = emitter.PushForce,
            EmitParticles = emitter.EmitParticles,
            ParticleRate = emitter.ParticleRate,
            ParticleSize = emitter.ParticleSize,
            ParticleLifetime = emitter.ParticleLifetime,
            ParticleColor = emitter.ParticleColor,
            EmitterEntity = emitterEntity
        });

        World.AddComponent(pulseEntity, new TransformComponent(transform.Position));
    }

    private void UpdatePulses(float deltaTime)
    {
        var pulsesToDestroy = new List<Entity>();

        foreach (var entity in World!.Query<LaserPulseComponent>())
        {
            ref var pulse = ref World.GetComponent<LaserPulseComponent>(entity);

            float moveDistance = pulse.Speed * deltaTime;
            var newPosition = pulse.Position + pulse.Direction * moveDistance;

            // Raycast for collision
            bool hitSomething = false;
            Vector3 hitPoint = newPosition;
            Vector3 hitNormal = Vector3.Zero;
            Entity hitEntity = default;

            if (_physics != null)
            {
                if (_physics.Raycast(pulse.Position, pulse.Direction, moveDistance + pulse.Length * 0.5f, out var hit))
                {
                    hitSomething = true;
                    hitPoint = hit.Point;
                    hitNormal = hit.Normal;
                    hitEntity = hit.Entity;

                    // Check for redirect surface
                    if (pulse.BouncesRemaining > 0 && TryRedirect(ref pulse, hitPoint, hitNormal, hitEntity))
                    {
                        pulse.BouncesRemaining--;
                        pulse.Position = hitPoint + pulse.Direction * 0.01f; // Small offset to avoid re-hit
                        continue;
                    }

                    // Apply hit effects
                    ApplyHitEffects(hitEntity, hitPoint, hitNormal, pulse.Direction, pulse.Damage, pulse.PushForce, entity);
                }
            }

            // Create segment for rendering
            var segStart = pulse.Position;
            var segEnd = hitSomething ? hitPoint : pulse.Position + pulse.Direction * pulse.Length;
            CreateSegment(segStart, segEnd, ref pulse, pulse.EmitterEntity);

            // Update position
            pulse.Position = newPosition;
            pulse.DistanceTraveled += moveDistance;

            // Update transform for any attached effects
            if (World.HasComponent<TransformComponent>(entity))
            {
                ref var transform = ref World.GetComponent<TransformComponent>(entity);
                transform.Position = pulse.Position;
            }

            // Check if pulse should be destroyed
            if (hitSomething || pulse.DistanceTraveled >= pulse.MaxRange)
            {
                pulsesToDestroy.Add(entity);
            }
        }

        foreach (var entity in pulsesToDestroy)
        {
            World.DestroyEntity(entity);
        }
    }

    private void TraceBeam(Entity emitterEntity, Vector3 origin, Vector3 direction, ref LaserEmitterComponent emitter, int bouncesRemaining)
    {
        // Collect gravity sources for bending
        var gravitySources = new List<(Vector3 position, float mass, float rs)>();
        foreach (var gsEntity in World!.Query<GravitySourceComponent, TransformComponent>())
        {
            ref var gs = ref World.GetComponent<GravitySourceComponent>(gsEntity);
            ref var gsTransform = ref World.GetComponent<TransformComponent>(gsEntity);
            
            if (gs.GravityType == GravityType.Schwarzschild || gs.GravityType == GravityType.Kerr)
            {
                // Heavily exaggerated effect for visible bending - multiply by 50 for dramatic effect
                float rs = gs.Mass * 50f;
                gravitySources.Add((gsTransform.Position, gs.Mass, rs));
            }
        }

        // If no gravity sources, use simple straight line trace
        if (gravitySources.Count == 0)
        {
            TraceBeamStraight(emitterEntity, origin, direction, ref emitter, bouncesRemaining);
            return;
        }

        // Trace beam with gravitational bending using geodesic integration
        // OPTIMIZED: Larger steps, less frequent raycasts
        var pos = origin;
        var vel = Vector3.Normalize(direction);
        float stepSize = 0.5f; // Larger step size for performance
        float totalDistance = 0f;
        float maxRange = emitter.MaxRange;
        var lastSegmentStart = origin;
        float segmentAccum = 0f;
        float segmentLength = 2.0f; // Longer segments for fewer draw calls
        int bounces = bouncesRemaining;
        int stepCount = 0;
        const int raycastInterval = 4; // Only raycast every N steps

        while (totalDistance < maxRange && stepCount < 150) // Cap iterations
        {
            stepCount++;
            
            // Calculate gravitational acceleration from all sources
            var accel = Vector3.Zero;
            foreach (var (gsPos, mass, rs) in gravitySources)
            {
                var toBlackHole = gsPos - pos;
                float rMag = toBlackHole.Length();
                
                if (rMag < rs * 0.3f)
                {
                    // Inside event horizon - terminate beam (absorbed)
                    CreateSegment(lastSegmentStart, pos, emitter, emitterEntity);
                    return;
                }
                
                // Simple gravitational attraction - only apply when reasonably close
                if (rMag < rs * 10f)
                {
                    var towardMass = Vector3.Normalize(toBlackHole);
                    float strength = rs * 0.5f / (rMag * rMag);
                    strength = MathF.Min(strength, 2f);
                    accel += towardMass * strength;
                }
            }

            // Euler integration - bend the light direction
            vel += accel * stepSize;
            vel = Vector3.Normalize(vel);
            var newPos = pos + vel * stepSize;
            
            // Check for collision only periodically to save performance
            if (_physics != null && emitter.UseRaycast && (stepCount % raycastInterval == 0))
            {
                float checkDist = stepSize * raycastInterval;
                if (_physics.Raycast(pos, vel, checkDist, out var hit))
                {
                    CreateSegment(lastSegmentStart, hit.Point, emitter, emitterEntity);
                    ApplyHitEffects(hit.Entity, hit.Point, hit.Normal, vel, 
                        emitter.Damage * 0.016f, emitter.PushForce * 0.016f, emitterEntity);

                    if (bounces > 0 && emitter.CanRedirect)
                    {
                        if (TryRedirectBeam(ref vel, hit.Point, hit.Normal, hit.Entity, emitter.RedirectAngle))
                        {
                            pos = hit.Point + vel * 0.01f;
                            lastSegmentStart = pos;
                            segmentAccum = 0f;
                            bounces--;
                            continue;
                        }
                    }
                    return;
                }
            }

            pos = newPos;
            totalDistance += stepSize;
            segmentAccum += stepSize;

            // Create segment at intervals for curved appearance
            if (segmentAccum >= segmentLength)
            {
                CreateSegment(lastSegmentStart, pos, emitter, emitterEntity);
                lastSegmentStart = pos;
                segmentAccum = 0f;
            }
        }

        // Final segment
        if (segmentAccum > 0.1f)
        {
            CreateSegment(lastSegmentStart, pos, emitter, emitterEntity);
        }
    }
    
    private void TraceBeamStraight(Entity emitterEntity, Vector3 origin, Vector3 direction, ref LaserEmitterComponent emitter, int bouncesRemaining)
    {
        var currentOrigin = origin;
        var currentDir = direction;
        float remainingRange = emitter.MaxRange;
        int bounces = bouncesRemaining;

        while (remainingRange > 0)
        {
            Vector3 endPoint = currentOrigin + currentDir * remainingRange;

            if (_physics != null && emitter.UseRaycast)
            {
                if (_physics.Raycast(currentOrigin, currentDir, remainingRange, out var hit))
                {
                    endPoint = hit.Point;

                    // Apply hit effects
                    ApplyHitEffects(hit.Entity, hit.Point, hit.Normal, currentDir, 
                        emitter.Damage * 0.016f, emitter.PushForce * 0.016f, emitterEntity);

                    // Check for redirect
                    if (bounces > 0 && emitter.CanRedirect)
                    {
                        if (TryRedirectBeam(ref currentDir, hit.Point, hit.Normal, hit.Entity, emitter.RedirectAngle))
                        {
                            CreateSegment(currentOrigin, hit.Point, emitter, emitterEntity);
                            currentOrigin = hit.Point + currentDir * 0.01f;
                            remainingRange -= Vector3.Distance(origin, hit.Point);
                            bounces--;
                            continue;
                        }
                    }
                }
            }

            // Create segment for this beam portion
            CreateSegment(currentOrigin, endPoint, emitter, emitterEntity);
            break;
        }
    }

    private bool TryRedirect(ref LaserPulseComponent pulse, Vector3 hitPoint, Vector3 hitNormal, Entity hitEntity)
    {
        if (!World!.HasComponent<LaserRedirectComponent>(hitEntity))
        {
            // Default reflection behavior if surface has high reflectivity based on angle
            float dotAngle = MathF.Abs(Vector3.Dot(pulse.Direction, hitNormal));
            if (dotAngle < 0.9f) // Glancing angle - reflect
            {
                pulse.Direction = Vector3.Reflect(pulse.Direction, hitNormal);
                return true;
            }
            return false;
        }

        ref var redirect = ref World.GetComponent<LaserRedirectComponent>(hitEntity);

        switch (redirect.Type)
        {
            case LaserRedirectComponent.RedirectType.Mirror:
                pulse.Direction = Vector3.Reflect(pulse.Direction, hitNormal);
                pulse.BeamColor *= redirect.Efficiency;
                if (redirect.TintColor.W > 0)
                    pulse.BeamColor = Vector4.Lerp(pulse.BeamColor, redirect.TintColor, 0.5f);
                return true;

            case LaserRedirectComponent.RedirectType.Refract:
                var refractAxis = Vector3.Cross(pulse.Direction, hitNormal);
                if (refractAxis.LengthSquared() > 0.001f)
                {
                    refractAxis = Vector3.Normalize(refractAxis);
                    var rotation = Quaternion.CreateFromAxisAngle(refractAxis, redirect.RefractAngle * MathF.PI / 180f);
                    pulse.Direction = Vector3.Transform(pulse.Direction, rotation);
                }
                pulse.BeamColor *= redirect.Efficiency;
                return true;

            case LaserRedirectComponent.RedirectType.Absorb:
                return false; // Stop the beam

            default:
                return false;
        }
    }

    private bool TryRedirectBeam(ref Vector3 direction, Vector3 hitPoint, Vector3 hitNormal, Entity hitEntity, float maxAngle)
    {
        if (!World!.HasComponent<LaserRedirectComponent>(hitEntity))
            return false;

        ref var redirect = ref World.GetComponent<LaserRedirectComponent>(hitEntity);

        if (redirect.Type == LaserRedirectComponent.RedirectType.Mirror)
        {
            direction = Vector3.Reflect(direction, hitNormal);
            return true;
        }

        if (redirect.Type == LaserRedirectComponent.RedirectType.Refract)
        {
            var refractAxis = Vector3.Cross(direction, hitNormal);
            if (refractAxis.LengthSquared() > 0.001f)
            {
                refractAxis = Vector3.Normalize(refractAxis);
                var rotation = Quaternion.CreateFromAxisAngle(refractAxis, redirect.RefractAngle * MathF.PI / 180f);
                direction = Vector3.Transform(direction, rotation);
                return true;
            }
        }

        return false;
    }

    private void ApplyHitEffects(Entity hitEntity, Vector3 hitPoint, Vector3 hitNormal, Vector3 laserDir, float damage, float pushForce, Entity laserEntity)
    {
        if (hitEntity.Id == 0)
            return;
        
        // Check if entity is still alive before applying effects
        if (!World!.IsAlive(hitEntity))
            return;

        // Add or update LaserHitComponent
        if (World.HasComponent<LaserHitComponent>(hitEntity))
        {
            ref var hit = ref World.GetComponent<LaserHitComponent>(hitEntity);
            hit.Damage += damage;
            hit.PushForce += pushForce;
        }
        else
        {
            World.AddComponent(hitEntity, new LaserHitComponent
            {
                HitPoint = hitPoint,
                HitNormal = hitNormal,
                Damage = damage,
                PushDirection = laserDir,
                PushForce = pushForce,
                LaserEntity = laserEntity
            });
        }

        // Apply physics push
        if (World.HasComponent<RigidBodyComponent>(hitEntity))
        {
            ref var rb = ref World.GetComponent<RigidBodyComponent>(hitEntity);
            rb.ApplyImpulse(laserDir * pushForce);
        }
    }

    private void CreateSegment(Vector3 start, Vector3 end, LaserEmitterComponent emitter, Entity emitterEntity)
    {
        var segEntity = World!.CreateEntity();
        
        // Create brighter energy pulse color
        var pulseColor = emitter.CoreColor;
        pulseColor.W = 1f;
        
        World.AddComponent(segEntity, new LaserSegmentComponent
        {
            Start = start,
            End = end,
            Width = emitter.BeamWidth,
            BeamColor = emitter.BeamColor,
            CoreColor = emitter.CoreColor,
            CoreWidth = emitter.CoreWidth,
            Intensity = 1f,
            EmitterEntity = emitterEntity,
            // Energy pulse settings
            ShowEnergyPulses = true,
            EnergyPulseCount = 8,
            EnergyPulseSpeed = 15f,
            EnergyPulseSize = 0.06f,
            EnergyPulseColor = pulseColor
        });
    }

    private void CreateSegment(Vector3 start, Vector3 end, ref LaserPulseComponent pulse, Entity emitterEntity)
    {
        var segEntity = World!.CreateEntity();
        
        // Create brighter energy pulse color
        var pulseColor = pulse.CoreColor;
        pulseColor.W = 1f;
        
        World.AddComponent(segEntity, new LaserSegmentComponent
        {
            Start = start,
            End = end,
            Width = pulse.Width,
            BeamColor = pulse.BeamColor,
            CoreColor = pulse.CoreColor,
            CoreWidth = pulse.CoreWidth,
            Intensity = 1f,
            EmitterEntity = emitterEntity,
            // Energy pulse settings
            ShowEnergyPulses = true,
            EnergyPulseCount = 6,
            EnergyPulseSpeed = 20f,
            EnergyPulseSize = 0.08f,
            EnergyPulseColor = pulseColor
        });
    }

    /// <summary>
    /// Manually fire a pulse from an emitter
    /// </summary>
    public void FirePulse(Entity emitterEntity)
    {
        if (!World!.HasComponent<LaserEmitterComponent>(emitterEntity) ||
            !World.HasComponent<TransformComponent>(emitterEntity))
            return;

        ref var emitter = ref World.GetComponent<LaserEmitterComponent>(emitterEntity);
        ref var transform = ref World.GetComponent<TransformComponent>(emitterEntity);

        if (emitter.Type == LaserType.Pulse)
        {
            FirePulse(emitterEntity, ref emitter, ref transform);
        }
    }

    /// <summary>
    /// Enable/disable an emitter
    /// </summary>
    public void SetEmitterEnabled(Entity emitterEntity, bool enabled)
    {
        if (World!.HasComponent<LaserEmitterComponent>(emitterEntity))
        {
            ref var emitter = ref World.GetComponent<LaserEmitterComponent>(emitterEntity);
            emitter.Enabled = enabled;
        }
    }
}
