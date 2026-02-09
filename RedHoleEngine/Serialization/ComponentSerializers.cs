using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Serialization;

/// <summary>
/// Serializer for TransformComponent
/// </summary>
public class TransformComponentSerializer : ComponentSerializer<TransformComponent>
{
    protected override void Serialize(ref TransformComponent component, BinaryWriter writer)
    {
        WriteVector3(writer, component.LocalPosition);
        WriteQuaternion(writer, component.LocalRotation);
        WriteVector3(writer, component.LocalScale);
    }

    protected override TransformComponent Deserialize(BinaryReader reader)
    {
        var position = ReadVector3(reader);
        var rotation = ReadQuaternion(reader);
        var scale = ReadVector3(reader);
        return new TransformComponent(position, rotation, scale);
    }
}

/// <summary>
/// Serializer for CameraComponent
/// </summary>
public class CameraComponentSerializer : ComponentSerializer<CameraComponent>
{
    protected override void Serialize(ref CameraComponent component, BinaryWriter writer)
    {
        writer.Write((int)component.ProjectionType);
        writer.Write(component.FieldOfView);
        writer.Write(component.NearPlane);
        writer.Write(component.FarPlane);
        writer.Write(component.OrthographicSize);
        writer.Write(component.AspectRatio);
        writer.Write(component.IsActive);
        writer.Write(component.Priority);
    }

    protected override CameraComponent Deserialize(BinaryReader reader)
    {
        return new CameraComponent
        {
            ProjectionType = (ProjectionType)reader.ReadInt32(),
            FieldOfView = reader.ReadSingle(),
            NearPlane = reader.ReadSingle(),
            FarPlane = reader.ReadSingle(),
            OrthographicSize = reader.ReadSingle(),
            AspectRatio = reader.ReadSingle(),
            IsActive = reader.ReadBoolean(),
            Priority = reader.ReadInt32()
        };
    }
}

/// <summary>
/// Serializer for GravitySourceComponent
/// </summary>
public class GravitySourceComponentSerializer : ComponentSerializer<GravitySourceComponent>
{
    protected override void Serialize(ref GravitySourceComponent component, BinaryWriter writer)
    {
        writer.Write((int)component.GravityType);
        writer.Write(component.Mass);
        writer.Write(component.SpinParameter);
        WriteVector3(writer, component.SpinAxis);
        writer.Write(component.MaxRange);
        WriteVector3(writer, component.UniformDirection);
        writer.Write(component.UniformStrength);
        writer.Write(component.AffectsLight);
    }

    protected override GravitySourceComponent Deserialize(BinaryReader reader)
    {
        var gravityType = (GravityType)reader.ReadInt32();
        var mass = reader.ReadSingle();
        var spin = reader.ReadSingle();
        var spinAxis = ReadVector3(reader);
        var maxRange = reader.ReadSingle();
        var uniformDir = ReadVector3(reader);
        var uniformStrength = reader.ReadSingle();
        var affectsLight = reader.ReadBoolean();

        return gravityType switch
        {
            GravityType.Kerr => GravitySourceComponent.CreateRotatingBlackHole(mass, spin, spinAxis),
            GravityType.Schwarzschild => GravitySourceComponent.CreateBlackHole(mass),
            _ => new GravitySourceComponent
            {
                GravityType = gravityType,
                Mass = mass,
                SpinParameter = spin,
                SpinAxis = spinAxis,
                MaxRange = maxRange,
                UniformDirection = uniformDir,
                UniformStrength = uniformStrength,
                AffectsLight = affectsLight
            }
        };
    }
}

/// <summary>
/// Serializer for RigidBodyComponent
/// </summary>
public class RigidBodyComponentSerializer : ComponentSerializer<RigidBodyComponent>
{
    protected override void Serialize(ref RigidBodyComponent component, BinaryWriter writer)
    {
        writer.Write((int)component.Type);
        writer.Write(component.Mass);
        writer.Write(component.Restitution);
        writer.Write(component.Friction);
        writer.Write(component.LinearDamping);
        writer.Write(component.AngularDamping);
        writer.Write(component.UseGravity);
        writer.Write(component.FreezePositionX);
        writer.Write(component.FreezePositionY);
        writer.Write(component.FreezePositionZ);
        writer.Write(component.FreezeRotationX);
        writer.Write(component.FreezeRotationY);
        writer.Write(component.FreezeRotationZ);
        writer.Write(component.CollisionLayer);
        writer.Write(component.CollisionMask);
    }

    protected override RigidBodyComponent Deserialize(BinaryReader reader)
    {
        return new RigidBodyComponent
        {
            Type = (RigidBodyType)reader.ReadInt32(),
            Mass = reader.ReadSingle(),
            Restitution = reader.ReadSingle(),
            Friction = reader.ReadSingle(),
            LinearDamping = reader.ReadSingle(),
            AngularDamping = reader.ReadSingle(),
            UseGravity = reader.ReadBoolean(),
            FreezePositionX = reader.ReadBoolean(),
            FreezePositionY = reader.ReadBoolean(),
            FreezePositionZ = reader.ReadBoolean(),
            FreezeRotationX = reader.ReadBoolean(),
            FreezeRotationY = reader.ReadBoolean(),
            FreezeRotationZ = reader.ReadBoolean(),
            CollisionLayer = reader.ReadUInt32(),
            CollisionMask = reader.ReadUInt32()
        };
    }
}

/// <summary>
/// Serializer for ColliderComponent
/// </summary>
public class ColliderComponentSerializer : ComponentSerializer<ColliderComponent>
{
    protected override void Serialize(ref ColliderComponent component, BinaryWriter writer)
    {
        writer.Write((int)component.ShapeType);
        WriteVector3(writer, component.Offset);
        writer.Write(component.IsTrigger);
        
        // Shape-specific data
        writer.Write(component.SphereRadius);
        WriteVector3(writer, component.BoxHalfExtents);
        writer.Write(component.CapsuleRadius);
        writer.Write(component.CapsuleHeight);
        writer.Write(component.CapsuleAxis);
        WriteVector3(writer, component.PlaneNormal);
        writer.Write(component.PlaneDistance);
        
        // Material overrides
        writer.Write(component.MaterialRestitution.HasValue);
        writer.Write(component.MaterialRestitution ?? 0f);
        writer.Write(component.MaterialStaticFriction.HasValue);
        writer.Write(component.MaterialStaticFriction ?? 0f);
        writer.Write(component.MaterialDynamicFriction.HasValue);
        writer.Write(component.MaterialDynamicFriction ?? 0f);
    }

    protected override ColliderComponent Deserialize(BinaryReader reader)
    {
        var component = new ColliderComponent
        {
            ShapeType = (ColliderType)reader.ReadInt32(),
            Offset = ReadVector3(reader),
            IsTrigger = reader.ReadBoolean(),
            SphereRadius = reader.ReadSingle(),
            BoxHalfExtents = ReadVector3(reader),
            CapsuleRadius = reader.ReadSingle(),
            CapsuleHeight = reader.ReadSingle(),
            CapsuleAxis = reader.ReadInt32(),
            PlaneNormal = ReadVector3(reader),
            PlaneDistance = reader.ReadSingle()
        };

        if (reader.ReadBoolean())
            component.MaterialRestitution = reader.ReadSingle();
        else
            reader.ReadSingle(); // Skip
            
        if (reader.ReadBoolean())
            component.MaterialStaticFriction = reader.ReadSingle();
        else
            reader.ReadSingle(); // Skip
            
        if (reader.ReadBoolean())
            component.MaterialDynamicFriction = reader.ReadSingle();
        else
            reader.ReadSingle(); // Skip

        return component;
    }
}

/// <summary>
/// Serializer for MeshComponent - saves mesh resource handle info
/// Note: MeshHandle needs to be re-resolved after loading using the saved ID
/// </summary>
public class MeshComponentSerializer : ComponentSerializer<MeshComponent>
{
    protected override void Serialize(ref MeshComponent component, BinaryWriter writer)
    {
        // Save mesh handle ID (if available) or empty string
        var meshId = component.MeshHandle.Id ?? "";
        WriteString(writer, meshId);
        writer.Write(component.CastShadows);
        writer.Write(component.ReceiveShadows);
        writer.Write(component.LayerMask);
        writer.Write(component.Visible);
    }

    protected override MeshComponent Deserialize(BinaryReader reader)
    {
        var meshId = ReadString(reader) ?? "";
        var castShadows = reader.ReadBoolean();
        var receiveShadows = reader.ReadBoolean();
        var layerMask = reader.ReadUInt32();
        var visible = reader.ReadBoolean();
        
        // Note: MeshHandle is left default - caller must resolve it using meshId
        // Store meshId in a way that can be retrieved (we'll use a special component or post-process)
        return new MeshComponent
        {
            // MeshHandle will need to be resolved post-load
            CastShadows = castShadows,
            ReceiveShadows = receiveShadows,
            LayerMask = layerMask,
            Visible = visible
        };
    }
    
    /// <summary>
    /// Get the mesh ID from serialized data (for post-load resolution)
    /// </summary>
    public static string? GetMeshIdFromData(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);
        return ReadString(reader);
    }
}

/// <summary>
/// Serializer for MaterialComponent
/// </summary>
public class MaterialComponentSerializer : ComponentSerializer<MaterialComponent>
{
    protected override void Serialize(ref MaterialComponent component, BinaryWriter writer)
    {
        WriteVector4(writer, component.BaseColor);
        writer.Write(component.Metallic);
        writer.Write(component.Roughness);
        WriteVector3(writer, component.EmissiveColor);
        writer.Write(component.UseRaytracing);
    }

    protected override MaterialComponent Deserialize(BinaryReader reader)
    {
        return new MaterialComponent
        {
            BaseColor = ReadVector4(reader),
            Metallic = reader.ReadSingle(),
            Roughness = reader.ReadSingle(),
            EmissiveColor = ReadVector3(reader),
            UseRaytracing = reader.ReadBoolean()
        };
    }
}

/// <summary>
/// Serializer for RenderSettingsComponent
/// </summary>
public class RenderSettingsComponentSerializer : ComponentSerializer<RenderSettingsComponent>
{
    protected override void Serialize(ref RenderSettingsComponent component, BinaryWriter writer)
    {
        writer.Write(component.Enabled);
        writer.Write((int)component.Mode);
        writer.Write((int)component.Preset);
        writer.Write(component.RaysPerPixel);
        writer.Write(component.MaxBounces);
        writer.Write(component.SamplesPerFrame);
        writer.Write(component.Accumulate);
        writer.Write(component.Denoise);
        writer.Write((int)component.LensingQuality);
        writer.Write(component.LensingMaxSteps);
        writer.Write(component.LensingStepSize);
        writer.Write(component.LensingBvhCheckInterval);
        writer.Write(component.LensingMaxDistance);
        writer.Write(component.ShowErgosphere);
        writer.Write(component.ErgosphereOpacity);
        writer.Write(component.ShowPhotonSphere);
        writer.Write(component.PhotonSphereOpacity);
    }

    protected override RenderSettingsComponent Deserialize(BinaryReader reader)
    {
        return new RenderSettingsComponent
        {
            Enabled = reader.ReadBoolean(),
            Mode = (RenderMode)reader.ReadInt32(),
            Preset = (RaytracerQualityPreset)reader.ReadInt32(),
            RaysPerPixel = reader.ReadInt32(),
            MaxBounces = reader.ReadInt32(),
            SamplesPerFrame = reader.ReadInt32(),
            Accumulate = reader.ReadBoolean(),
            Denoise = reader.ReadBoolean(),
            LensingQuality = (LensingQuality)reader.ReadInt32(),
            LensingMaxSteps = reader.ReadInt32(),
            LensingStepSize = reader.ReadSingle(),
            LensingBvhCheckInterval = reader.ReadInt32(),
            LensingMaxDistance = reader.ReadSingle(),
            ShowErgosphere = reader.ReadBoolean(),
            ErgosphereOpacity = reader.ReadSingle(),
            ShowPhotonSphere = reader.ReadBoolean(),
            PhotonSphereOpacity = reader.ReadSingle()
        };
    }
}

/// <summary>
/// Serializer for RaytracerMeshComponent
/// </summary>
public class RaytracerMeshComponentSerializer : ComponentSerializer<RaytracerMeshComponent>
{
    protected override void Serialize(ref RaytracerMeshComponent component, BinaryWriter writer)
    {
        writer.Write(component.Enabled);
        writer.Write(component.StaticOnly);
    }

    protected override RaytracerMeshComponent Deserialize(BinaryReader reader)
    {
        return new RaytracerMeshComponent(reader.ReadBoolean())
        {
            StaticOnly = reader.ReadBoolean()
        };
    }
}
