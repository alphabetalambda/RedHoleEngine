using System.Numerics;
using System;
using ImGuiNET;
using RedHoleEngine.Audio;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Commands;
using RedHoleEngine.Physics;
using RedHoleEngine.Physics.Collision;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Panel for inspecting and editing entity components
/// </summary>
public class InspectorPanel : EditorPanel
{
    public override string Title => "Inspector";

    // Undo tracking - stores component state when editing starts
    private TransformComponent? _editStartTransform;
    private RigidBodyComponent? _editStartRigidBody;
    private ColliderComponent? _editStartCollider;
    private CameraComponent? _editStartCamera;
    private GravitySourceComponent? _editStartGravity;
    private AccretionDiskComponent? _editStartAccretion;
    private AudioSourceComponent? _editStartAudioSource;
    private CollisionSoundComponent? _editStartCollisionSound;
    private RenderSettingsComponent? _editStartRenderSettings;
    
    private Entity _editingEntity;
    private string _editingProperty = "";

    protected override void OnDraw()
    {
        if (World == null || Selection == null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No active scene");
            return;
        }

        if (!Selection.HasSelection)
        {
            ImGui.TextDisabled("Select an entity to inspect");
            return;
        }

        var entity = Selection.PrimarySelection;
        if (!World.IsAlive(entity))
        {
            ImGui.TextColored(new Vector4(1, 0, 0, 1), "Selected entity no longer exists");
            return;
        }

        // Entity header
        DrawEntityHeader(entity);

        ImGui.Separator();

        // Draw each component
        DrawTransformComponent(entity);
        DrawRigidBodyComponent(entity);
        DrawColliderComponent(entity);
        DrawCameraComponent(entity);
        DrawGravitySourceComponent(entity);
        DrawAudioSourceComponent(entity);
        DrawCollisionSoundComponent(entity);
        DrawRenderSettingsComponent(entity);

        ImGui.Separator();

        // Add component button
        DrawAddComponentButton(entity);
    }

    private void DrawEntityHeader(Entity entity)
    {
        ImGui.Text($"Entity {entity.Id}");
        ImGui.SameLine();
        ImGui.TextDisabled($"(Gen: {entity.Generation})");
    }

    private void DrawTransformComponent(Entity entity)
    {
        if (!World!.HasComponent<TransformComponent>(entity)) return;

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ref var transform = ref World.GetComponent<TransformComponent>(entity);

            // Position
            var position = transform.Position;
            BeginPropertyEdit(entity, "Position", ref transform, ref _editStartTransform);
            if (ImGui.DragFloat3("Position", ref position, 0.1f))
            {
                transform.Position = position;
            }
            EndPropertyEdit(entity, "Position", ref transform, ref _editStartTransform);

            // Rotation (as Euler angles)
            var euler = QuaternionToEuler(transform.Rotation);
            BeginPropertyEdit(entity, "Rotation", ref transform, ref _editStartTransform);
            if (ImGui.DragFloat3("Rotation", ref euler, 1f))
            {
                transform.Rotation = EulerToQuaternion(euler);
            }
            EndPropertyEdit(entity, "Rotation", ref transform, ref _editStartTransform);

            // Scale
            var scale = transform.LocalScale;
            BeginPropertyEdit(entity, "Scale", ref transform, ref _editStartTransform);
            if (ImGui.DragFloat3("Scale", ref scale, 0.1f, 0.01f, 100f))
            {
                transform.LocalScale = scale;
            }
            EndPropertyEdit(entity, "Scale", ref transform, ref _editStartTransform);

            ImGui.Spacing();
        }
    }

    #region Undo Helpers

    /// <summary>
    /// Call before an ImGui edit widget to capture initial state
    /// </summary>
    private void BeginPropertyEdit<T>(Entity entity, string propertyName, ref T current, ref T? stored) where T : struct, IComponent
    {
        if (ImGui.IsItemActivated())
        {
            stored = current;
            _editingEntity = entity;
            _editingProperty = propertyName;
        }
    }

    /// <summary>
    /// Call after an ImGui edit widget to create undo command if changed
    /// </summary>
    private void EndPropertyEdit<T>(Entity entity, string propertyName, ref T current, ref T? stored) where T : struct, IComponent
    {
        if (ImGui.IsItemDeactivatedAfterEdit() && stored.HasValue && UndoRedo != null && World != null)
        {
            // Create undo command
            var command = new ModifyComponentCommand<T>(
                World, entity, stored.Value, current, propertyName);
            
            // Execute without re-applying (already applied during drag)
            UndoRedo.ExecuteCommand(new AlreadyAppliedCommand(command));
            
            stored = null;
        }
    }

    /// <summary>
    /// Helper command that wraps an already-applied modification for undo
    /// </summary>
    private class AlreadyAppliedCommand : ICommand
    {
        private readonly ICommand _inner;
        private bool _firstExecute = true;

        public string Description => _inner.Description;

        public AlreadyAppliedCommand(ICommand inner)
        {
            _inner = inner;
        }

        public void Execute()
        {
            // Skip first execute since change was already applied during drag
            if (_firstExecute)
            {
                _firstExecute = false;
                return;
            }
            _inner.Execute();
        }

        public void Undo() => _inner.Undo();
    }

    #endregion

    private void DrawRigidBodyComponent(Entity entity)
    {
        if (!World!.HasComponent<RigidBodyComponent>(entity)) return;

        bool open = ImGui.CollapsingHeader("Rigid Body", ImGuiTreeNodeFlags.DefaultOpen);
        
        // Context menu for removal
        if (ImGui.BeginPopupContextItem("RigidBodyContext"))
        {
            if (ImGui.MenuItem("Remove Component"))
            {
                World.RemoveComponent<RigidBodyComponent>(entity);
            }
            ImGui.EndPopup();
        }

        if (!open) return;

        ref var rb = ref World.GetComponent<RigidBodyComponent>(entity);

        // Body type
        var types = new[] { "Dynamic", "Static", "Kinematic" };
        int currentType = (int)rb.Type;
        BeginPropertyEdit(entity, "Type", ref rb, ref _editStartRigidBody);
        if (ImGui.Combo("Type", ref currentType, types, types.Length))
        {
            rb.Type = (RigidBodyType)currentType;
        }
        EndPropertyEdit(entity, "Type", ref rb, ref _editStartRigidBody);

        // Mass (only for dynamic)
        if (rb.Type == RigidBodyType.Dynamic)
        {
            var mass = rb.Mass;
            BeginPropertyEdit(entity, "Mass", ref rb, ref _editStartRigidBody);
            if (ImGui.DragFloat("Mass", ref mass, 0.1f, 0.001f, 10000f))
            {
                rb.Mass = mass;
            }
            EndPropertyEdit(entity, "Mass", ref rb, ref _editStartRigidBody);
        }

        // Material properties
        var restitution = rb.Restitution;
        BeginPropertyEdit(entity, "Restitution", ref rb, ref _editStartRigidBody);
        if (ImGui.DragFloat("Restitution", ref restitution, 0.01f, 0f, 1f))
        {
            rb.Restitution = restitution;
        }
        EndPropertyEdit(entity, "Restitution", ref rb, ref _editStartRigidBody);

        var friction = rb.Friction;
        BeginPropertyEdit(entity, "Friction", ref rb, ref _editStartRigidBody);
        if (ImGui.DragFloat("Friction", ref friction, 0.01f, 0f, 1f))
        {
            rb.Friction = friction;
        }
        EndPropertyEdit(entity, "Friction", ref rb, ref _editStartRigidBody);

        // Damping
        if (ImGui.TreeNode("Damping"))
        {
            var linearDamping = rb.LinearDamping;
            BeginPropertyEdit(entity, "LinearDamping", ref rb, ref _editStartRigidBody);
            if (ImGui.DragFloat("Linear", ref linearDamping, 0.01f, 0f, 1f))
            {
                rb.LinearDamping = linearDamping;
            }
            EndPropertyEdit(entity, "LinearDamping", ref rb, ref _editStartRigidBody);

            var angularDamping = rb.AngularDamping;
            BeginPropertyEdit(entity, "AngularDamping", ref rb, ref _editStartRigidBody);
            if (ImGui.DragFloat("Angular", ref angularDamping, 0.01f, 0f, 1f))
            {
                rb.AngularDamping = angularDamping;
            }
            EndPropertyEdit(entity, "AngularDamping", ref rb, ref _editStartRigidBody);

            ImGui.TreePop();
        }

        // Gravity
        var useGravity = rb.UseGravity;
        BeginPropertyEdit(entity, "UseGravity", ref rb, ref _editStartRigidBody);
        if (ImGui.Checkbox("Use Gravity", ref useGravity))
        {
            rb.UseGravity = useGravity;
        }
        EndPropertyEdit(entity, "UseGravity", ref rb, ref _editStartRigidBody);

        // Constraints
        if (ImGui.TreeNode("Constraints"))
        {
            ImGui.Text("Freeze Position:");
            ImGui.SameLine();
            var freezeX = rb.FreezePositionX;
            var freezeY = rb.FreezePositionY;
            var freezeZ = rb.FreezePositionZ;
            if (ImGui.Checkbox("X##PosX", ref freezeX)) rb.FreezePositionX = freezeX;
            ImGui.SameLine();
            if (ImGui.Checkbox("Y##PosY", ref freezeY)) rb.FreezePositionY = freezeY;
            ImGui.SameLine();
            if (ImGui.Checkbox("Z##PosZ", ref freezeZ)) rb.FreezePositionZ = freezeZ;

            ImGui.Text("Freeze Rotation:");
            ImGui.SameLine();
            var freezeRX = rb.FreezeRotationX;
            var freezeRY = rb.FreezeRotationY;
            var freezeRZ = rb.FreezeRotationZ;
            if (ImGui.Checkbox("X##RotX", ref freezeRX)) rb.FreezeRotationX = freezeRX;
            ImGui.SameLine();
            if (ImGui.Checkbox("Y##RotY", ref freezeRY)) rb.FreezeRotationY = freezeRY;
            ImGui.SameLine();
            if (ImGui.Checkbox("Z##RotZ", ref freezeRZ)) rb.FreezeRotationZ = freezeRZ;

            ImGui.TreePop();
        }

        // Runtime info (read-only)
        if (ImGui.TreeNode("Runtime Info"))
        {
            ImGui.TextDisabled($"Velocity: {rb.LinearVelocity.X:F2}, {rb.LinearVelocity.Y:F2}, {rb.LinearVelocity.Z:F2}");
            ImGui.TextDisabled($"Angular: {rb.AngularVelocity.X:F2}, {rb.AngularVelocity.Y:F2}, {rb.AngularVelocity.Z:F2}");
            ImGui.TextDisabled($"Awake: {(rb.IsAwake ? "Yes" : "No")}");
            ImGui.TreePop();
        }

        ImGui.Spacing();
    }

    private void DrawColliderComponent(Entity entity)
    {
        if (!World!.HasComponent<ColliderComponent>(entity)) return;

        bool open = ImGui.CollapsingHeader("Collider", ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.BeginPopupContextItem("ColliderContext"))
        {
            if (ImGui.MenuItem("Remove Component"))
            {
                World.RemoveComponent<ColliderComponent>(entity);
            }
            ImGui.EndPopup();
        }

        if (!open) return;

        ref var collider = ref World.GetComponent<ColliderComponent>(entity);

        // Shape type (read-only for now)
        ImGui.Text($"Shape: {collider.ShapeType}");

        // Shape-specific properties
        switch (collider.ShapeType)
        {
            case ColliderType.Sphere:
                var sphereRadius = collider.SphereRadius;
                if (ImGui.DragFloat("Radius", ref sphereRadius, 0.1f, 0.01f, 100f))
                {
                    collider.SphereRadius = sphereRadius;
                }
                break;

            case ColliderType.Box:
                var halfExtents = collider.BoxHalfExtents;
                if (ImGui.DragFloat3("Half Extents", ref halfExtents, 0.1f, 0.01f, 100f))
                {
                    collider.BoxHalfExtents = halfExtents;
                }
                break;

            case ColliderType.Capsule:
                var capsuleRadius = collider.CapsuleRadius;
                if (ImGui.DragFloat("Radius##Capsule", ref capsuleRadius, 0.1f, 0.01f, 100f))
                {
                    collider.CapsuleRadius = capsuleRadius;
                }
                var capsuleHeight = collider.CapsuleHeight;
                if (ImGui.DragFloat("Height", ref capsuleHeight, 0.1f, 0.01f, 100f))
                {
                    collider.CapsuleHeight = capsuleHeight;
                }
                break;

            case ColliderType.Plane:
                ImGui.Text($"Normal: {collider.PlaneNormal}");
                var distance = collider.PlaneDistance;
                if (ImGui.DragFloat("Distance", ref distance, 0.1f))
                {
                    collider.PlaneDistance = distance;
                }
                break;
        }

        // Offset
        var offset = collider.Offset;
        if (ImGui.DragFloat3("Offset", ref offset, 0.1f))
        {
            collider.Offset = offset;
        }

        // Trigger
        var isTrigger = collider.IsTrigger;
        if (ImGui.Checkbox("Is Trigger", ref isTrigger))
        {
            collider.IsTrigger = isTrigger;
        }

        ImGui.Spacing();
    }

    private void DrawCameraComponent(Entity entity)
    {
        if (!World!.HasComponent<CameraComponent>(entity)) return;

        bool open = ImGui.CollapsingHeader("Camera", ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.BeginPopupContextItem("CameraContext"))
        {
            if (ImGui.MenuItem("Remove Component"))
            {
                World.RemoveComponent<CameraComponent>(entity);
            }
            ImGui.EndPopup();
        }

        if (!open) return;

        ref var camera = ref World.GetComponent<CameraComponent>(entity);

        // Projection type
        var projTypes = new[] { "Perspective", "Orthographic" };
        int projType = camera.ProjectionType == ProjectionType.Orthographic ? 1 : 0;
        if (ImGui.Combo("Projection", ref projType, projTypes, projTypes.Length))
        {
            camera.ProjectionType = projType == 1 ? ProjectionType.Orthographic : ProjectionType.Perspective;
        }

        if (camera.ProjectionType == ProjectionType.Perspective)
        {
            var fov = camera.FieldOfView;
            if (ImGui.DragFloat("Field of View", ref fov, 0.5f, 1f, 179f))
            {
                camera.FieldOfView = fov;
            }
        }
        else
        {
            var orthoSize = camera.OrthographicSize;
            if (ImGui.DragFloat("Ortho Size", ref orthoSize, 0.1f, 0.1f, 1000f))
            {
                camera.OrthographicSize = orthoSize;
            }
        }

        var nearPlane = camera.NearPlane;
        if (ImGui.DragFloat("Near Plane", ref nearPlane, 0.01f, 0.001f, camera.FarPlane - 0.01f))
        {
            camera.NearPlane = nearPlane;
        }

        var farPlane = camera.FarPlane;
        if (ImGui.DragFloat("Far Plane", ref farPlane, 1f, camera.NearPlane + 0.01f, 100000f))
        {
            camera.FarPlane = farPlane;
        }

        var isActive = camera.IsActive;
        if (ImGui.Checkbox("Is Active", ref isActive))
        {
            camera.IsActive = isActive;
        }

        ImGui.Spacing();
    }

    private void DrawGravitySourceComponent(Entity entity)
    {
        if (!World!.HasComponent<GravitySourceComponent>(entity)) return;

        bool open = ImGui.CollapsingHeader("Gravity Source", ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.BeginPopupContextItem("GravityContext"))
        {
            if (ImGui.MenuItem("Remove Component"))
            {
                World.RemoveComponent<GravitySourceComponent>(entity);
            }
            ImGui.EndPopup();
        }

        if (!open) return;

        ref var gravity = ref World.GetComponent<GravitySourceComponent>(entity);

        // Gravity type
        var types = Enum.GetNames<GravityType>();
        int currentType = (int)gravity.GravityType;
        BeginPropertyEdit(entity, "GravityType", ref gravity, ref _editStartGravity);
        if (ImGui.Combo("Type", ref currentType, types, types.Length))
        {
            gravity.GravityType = (GravityType)currentType;
        }
        EndPropertyEdit(entity, "GravityType", ref gravity, ref _editStartGravity);

        // Mass
        var mass = gravity.Mass;
        BeginPropertyEdit(entity, "Mass", ref gravity, ref _editStartGravity);
        if (ImGui.DragFloat("Mass", ref mass, 1000f, 0f, float.MaxValue, "%.0f"))
        {
            gravity.Mass = mass;
        }
        EndPropertyEdit(entity, "Mass", ref gravity, ref _editStartGravity);

        // Spin (for Kerr black holes)
        if (gravity.GravityType == GravityType.Kerr)
        {
            var spin = gravity.SpinParameter;
            BeginPropertyEdit(entity, "SpinParameter", ref gravity, ref _editStartGravity);
            if (ImGui.DragFloat("Spin Parameter", ref spin, 0.01f, 0f, 1f))
            {
                gravity.SpinParameter = spin;
            }
            EndPropertyEdit(entity, "SpinParameter", ref gravity, ref _editStartGravity);
        }

        // Range
        var maxRange = gravity.MaxRange;
        BeginPropertyEdit(entity, "MaxRange", ref gravity, ref _editStartGravity);
        if (ImGui.DragFloat("Max Range", ref maxRange, 1f, 0f, 10000f))
        {
            gravity.MaxRange = maxRange;
        }
        EndPropertyEdit(entity, "MaxRange", ref gravity, ref _editStartGravity);

        // Affects Light
        var affectsLight = gravity.AffectsLight;
        BeginPropertyEdit(entity, "AffectsLight", ref gravity, ref _editStartGravity);
        if (ImGui.Checkbox("Affects Light", ref affectsLight))
        {
            gravity.AffectsLight = affectsLight;
        }
        EndPropertyEdit(entity, "AffectsLight", ref gravity, ref _editStartGravity);

        // Computed values (read-only)
        if (ImGui.TreeNode("Computed Radii"))
        {
            float rs = gravity.SchwarzschildRadius;
            ImGui.TextDisabled($"Schwarzschild: {rs:F2}");
            ImGui.TextDisabled($"Photon Sphere: {rs * 1.5f:F2}");
            ImGui.TextDisabled($"ISCO: {rs * 3f:F2}");
            ImGui.TreePop();
        }

        ImGui.Spacing();
    }

    private void DrawAudioSourceComponent(Entity entity)
    {
        if (!World!.HasComponent<AudioSourceComponent>(entity)) return;

        bool open = ImGui.CollapsingHeader("Audio Source", ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.BeginPopupContextItem("AudioContext"))
        {
            if (ImGui.MenuItem("Remove Component"))
            {
                World.RemoveComponent<AudioSourceComponent>(entity);
            }
            ImGui.EndPopup();
        }

        if (!open) return;

        ref var audio = ref World.GetComponent<AudioSourceComponent>(entity);

        // Clip
        var clipId = audio.ClipId ?? "";
        if (ImGui.InputText("Clip", ref clipId, 256))
        {
            audio.ClipId = clipId;
        }

        // Playback
        var isPlaying = audio.IsPlaying;
        if (ImGui.Checkbox("Playing", ref isPlaying))
        {
            audio.IsPlaying = isPlaying;
        }
        ImGui.SameLine();
        var loop = audio.Loop;
        if (ImGui.Checkbox("Loop", ref loop))
        {
            audio.Loop = loop;
        }

        // Volume & Pitch
        var volume = audio.Volume;
        if (ImGui.SliderFloat("Volume", ref volume, 0f, 1f))
        {
            audio.Volume = volume;
        }

        var pitch = audio.Pitch;
        if (ImGui.DragFloat("Pitch", ref pitch, 0.01f, 0.1f, 4f))
        {
            audio.Pitch = pitch;
        }

        // Spatial
        if (ImGui.TreeNode("Spatial Settings"))
        {
            var minDist = audio.MinDistance;
            if (ImGui.DragFloat("Min Distance", ref minDist, 0.1f, 0f, audio.MaxDistance))
            {
                audio.MinDistance = minDist;
            }

            var maxDist = audio.MaxDistance;
            if (ImGui.DragFloat("Max Distance", ref maxDist, 1f, audio.MinDistance, 1000f))
            {
                audio.MaxDistance = maxDist;
            }

            ImGui.TreePop();
        }

        ImGui.Spacing();
    }

    private void DrawCollisionSoundComponent(Entity entity)
    {
        if (!World!.HasComponent<CollisionSoundComponent>(entity)) return;

        bool open = ImGui.CollapsingHeader("Collision Sound", ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.BeginPopupContextItem("CollisionSoundContext"))
        {
            if (ImGui.MenuItem("Remove Component"))
            {
                World.RemoveComponent<CollisionSoundComponent>(entity);
            }
            ImGui.EndPopup();
        }

        if (!open) return;

        ref var sound = ref World.GetComponent<CollisionSoundComponent>(entity);

        // Surface type
        var surfaceTypes = Enum.GetNames<SurfaceType>();
        int currentSurface = (int)sound.SurfaceType;
        if (ImGui.Combo("Surface Type", ref currentSurface, surfaceTypes, surfaceTypes.Length))
        {
            sound.SurfaceType = (SurfaceType)currentSurface;
        }

        // Volume & Pitch multipliers
        var volumeMult = sound.VolumeMultiplier;
        if (ImGui.SliderFloat("Volume Mult", ref volumeMult, 0f, 2f))
        {
            sound.VolumeMultiplier = volumeMult;
        }

        var pitchMult = sound.PitchMultiplier;
        if (ImGui.DragFloat("Pitch Mult", ref pitchMult, 0.01f, 0.1f, 4f))
        {
            sound.PitchMultiplier = pitchMult;
        }

        // Enabled
        var enabled = sound.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            sound.Enabled = enabled;
        }

        ImGui.Spacing();
    }

    private void DrawRenderSettingsComponent(Entity entity)
    {
        if (!World!.HasComponent<RenderSettingsComponent>(entity)) return;

        bool open = ImGui.CollapsingHeader("Render Settings", ImGuiTreeNodeFlags.DefaultOpen);

        if (ImGui.BeginPopupContextItem("RenderSettingsContext"))
        {
            if (ImGui.MenuItem("Remove Component"))
            {
                World.RemoveComponent<RenderSettingsComponent>(entity);
            }
            ImGui.EndPopup();
        }

        if (!open) return;

        ref var settings = ref World.GetComponent<RenderSettingsComponent>(entity);

        var enabled = settings.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            settings.Enabled = enabled;
        }

        var modes = new[] { "Raytraced", "Rasterized" };
        int mode = settings.Mode == RenderMode.Rasterized ? 1 : 0;
        if (ImGui.Combo("Mode", ref mode, modes, modes.Length))
        {
            settings.Mode = mode == 1 ? RenderMode.Rasterized : RenderMode.Raytraced;
        }

        var presets = new[] { "Fast", "Balanced", "Quality", "Custom" };
        int presetIndex = settings.Preset switch
        {
            RaytracerQualityPreset.Fast => 0,
            RaytracerQualityPreset.Balanced => 1,
            RaytracerQualityPreset.Quality => 2,
            _ => 3
        };
        if (ImGui.Combo("Preset", ref presetIndex, presets, presets.Length))
        {
            settings.Preset = presetIndex switch
            {
                0 => RaytracerQualityPreset.Fast,
                1 => RaytracerQualityPreset.Balanced,
                2 => RaytracerQualityPreset.Quality,
                _ => RaytracerQualityPreset.Custom
            };
            if (settings.Preset != RaytracerQualityPreset.Custom)
            {
                var preset = RaytracerPresetUtilities.GetPresetValues(settings.Preset);
                settings.RaysPerPixel = preset.RaysPerPixel;
                settings.MaxBounces = preset.MaxBounces;
                settings.SamplesPerFrame = preset.SamplesPerFrame;
                settings.Accumulate = preset.Accumulate;
                settings.Denoise = preset.Denoise;
            }
        }

        int rays = settings.RaysPerPixel;
        if (ImGui.SliderInt("Rays Per Pixel", ref rays, 1, settings.MaxRaysPerPixelLimit))
        {
            settings.RaysPerPixel = rays;
            settings.Preset = RaytracerQualityPreset.Custom;
        }

        int bounces = settings.MaxBounces;
        if (ImGui.SliderInt("Max Bounces", ref bounces, 1, settings.MaxBouncesLimit))
        {
            settings.MaxBounces = bounces;
            settings.Preset = RaytracerQualityPreset.Custom;
        }

        int samplesPerFrame = settings.SamplesPerFrame;
        if (ImGui.SliderInt("Samples/Frame", ref samplesPerFrame, 1, settings.MaxSamplesPerFrameLimit))
        {
            settings.SamplesPerFrame = samplesPerFrame;
            settings.Preset = RaytracerQualityPreset.Custom;
        }

        var accumulate = settings.Accumulate;
        if (ImGui.Checkbox("Accumulate", ref accumulate))
        {
            settings.Accumulate = accumulate;
            settings.Preset = RaytracerQualityPreset.Custom;
        }

        var denoise = settings.Denoise;
        if (ImGui.Checkbox("Denoise", ref denoise))
        {
            settings.Denoise = denoise;
            settings.Preset = RaytracerQualityPreset.Custom;
        }

        if (ImGui.Button("Reset Accumulation"))
        {
            settings.ResetAccumulation = true;
        }

        int maxRays = settings.MaxRaysPerPixelLimit;
        if (ImGui.DragInt("RPP Limit", ref maxRays, 1, 1, 256))
        {
            settings.MaxRaysPerPixelLimit = Math.Max(1, maxRays);
        }

        int maxBounces = settings.MaxBouncesLimit;
        if (ImGui.DragInt("Bounce Limit", ref maxBounces, 1, 1, 32))
        {
            settings.MaxBouncesLimit = Math.Max(1, maxBounces);
        }

        int maxSamples = settings.MaxSamplesPerFrameLimit;
        if (ImGui.DragInt("Samples Limit", ref maxSamples, 1, 1, 32))
        {
            settings.MaxSamplesPerFrameLimit = Math.Max(1, maxSamples);
        }

        ImGui.Spacing();
    }

    private void DrawAddComponentButton(Entity entity)
    {
        if (ImGui.Button("Add Component", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            ImGui.OpenPopup("AddComponentPopup");
        }

        if (ImGui.BeginPopup("AddComponentPopup"))
        {
            if (!World!.HasComponent<RigidBodyComponent>(entity) && ImGui.MenuItem("Rigid Body"))
            {
                World.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));
            }

            if (!World.HasComponent<ColliderComponent>(entity))
            {
                if (ImGui.BeginMenu("Collider"))
                {
                    if (ImGui.MenuItem("Sphere"))
                        World.AddComponent(entity, ColliderComponent.CreateSphere(0.5f));
                    if (ImGui.MenuItem("Box"))
                        World.AddComponent(entity, ColliderComponent.CreateBox(new Vector3(0.5f)));
                    if (ImGui.MenuItem("Capsule"))
                        World.AddComponent(entity, ColliderComponent.CreateCapsule(0.5f, 2f));
                    if (ImGui.MenuItem("Plane"))
                        World.AddComponent(entity, ColliderComponent.CreateGroundPlane());
                    ImGui.EndMenu();
                }
            }

            if (!World.HasComponent<CameraComponent>(entity) && ImGui.MenuItem("Camera"))
            {
                World.AddComponent(entity, CameraComponent.CreatePerspective(60f, 16f / 9f));
            }

            if (!World.HasComponent<GravitySourceComponent>(entity) && ImGui.MenuItem("Gravity Source"))
            {
                World.AddComponent(entity, GravitySourceComponent.CreateBlackHole(1e6f));
            }

            if (!World.HasComponent<AudioSourceComponent>(entity) && ImGui.MenuItem("Audio Source"))
            {
                World.AddComponent(entity, AudioSourceComponent.Default);
            }

            if (!World.HasComponent<CollisionSoundComponent>(entity) && ImGui.MenuItem("Collision Sound"))
            {
                World.AddComponent(entity, CollisionSoundComponent.Default);
            }

            if (!World.HasComponent<RenderSettingsComponent>(entity) && ImGui.MenuItem("Render Settings"))
            {
                World.AddComponent(entity, new RenderSettingsComponent());
            }

            ImGui.EndPopup();
        }
    }

    #region Helpers

    private static Vector3 QuaternionToEuler(Quaternion q)
    {
        // Convert quaternion to Euler angles (degrees)
        float sinr_cosp = 2 * (q.W * q.X + q.Y * q.Z);
        float cosr_cosp = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        float roll = MathF.Atan2(sinr_cosp, cosr_cosp);

        float sinp = 2 * (q.W * q.Y - q.Z * q.X);
        float pitch = MathF.Abs(sinp) >= 1 
            ? MathF.CopySign(MathF.PI / 2, sinp) 
            : MathF.Asin(sinp);

        float siny_cosp = 2 * (q.W * q.Z + q.X * q.Y);
        float cosy_cosp = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        float yaw = MathF.Atan2(siny_cosp, cosy_cosp);

        return new Vector3(
            roll * 180f / MathF.PI,
            pitch * 180f / MathF.PI,
            yaw * 180f / MathF.PI
        );
    }

    private static Quaternion EulerToQuaternion(Vector3 euler)
    {
        // Convert Euler angles (degrees) to quaternion
        float roll = euler.X * MathF.PI / 180f;
        float pitch = euler.Y * MathF.PI / 180f;
        float yaw = euler.Z * MathF.PI / 180f;

        return Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
    }

    #endregion
}
