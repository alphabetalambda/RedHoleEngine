using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.Scene;
using RedHoleEngine.Platform;
using RedHoleEngine.Rendering;
using RedHoleEngine.Resources;

namespace RedHoleEngine.Tests.Components;

public class ComponentCoverageTests
{
    private const float Epsilon = 0.001f;

    [Fact]
    public void TransformComponent_ConstructorsAndAccessors_DelegateToTransform()
    {
        var position = new Vector3(1f, 2f, 3f);
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
        var scale = new Vector3(2f, 3f, 4f);

        var fromPosition = new TransformComponent(position);
        var fromRotation = new TransformComponent(position, rotation);
        var full = new TransformComponent(position, rotation, scale);
        var wrapped = new TransformComponent(new Transform(position));
        var created = new TransformComponent();

        Assert.Equal(position, fromPosition.Position);
        Assert.Equal(position, fromPosition.LocalPosition);
        Assert.Equal(position, fromRotation.LocalPosition);
        Assert.Equal(scale, full.LocalScale);
        Assert.Equal(position, wrapped.Position);
        Assert.Equal(Vector3.Zero, created.Position);
        Assert.Equal(Vector3.One, created.LocalScale);

        full.Position = new Vector3(8f, 0f, 0f);
        full.LocalScale = new Vector3(1f, 1f, 1f);
        full.Rotation = Quaternion.Identity;
        Assert.Equal(new Vector3(8f, 0f, 0f), full.LocalPosition);
        Assert.Equal(full.Transform.Forward, full.Forward);
        Assert.Equal(full.Transform.Right, full.Right);
        Assert.Equal(full.Transform.Up, full.Up);
        Assert.Equal(full.Transform.WorldMatrix, full.WorldMatrix);
        Assert.Equal(full.Transform.LocalMatrix, full.LocalMatrix);
    }

    [Fact]
    public void UiComponents_StoreConstructorValues()
    {
        var rect = new UiRectComponent(new Vector2(1f, 2f), new Vector2(3f, 4f), new Vector4(0.1f, 0.2f, 0.3f, 0.4f));
        var text = new UiTextComponent(new Vector2(5f, 6f), "score", new Vector4(1f, 1f, 1f, 1f));
        var image = new UiImageComponent(new Vector2(7f, 8f), new Vector2(9f, 10f), "logo.png", new Vector4(1f, 0f, 0f, 1f), 3);
        var mediaImage = new UiImageComponent(Vector2.Zero, Vector2.One, 12, Vector4.One, 4);
        var video = new UiVideoComponent(new Vector2(1f, 1f), new Vector2(2f, 2f), "clip.mp4");
        var mediaVideo = new UiVideoComponent(Vector2.Zero, Vector2.One, 9, 24f, loop: false, playing: false, layer: 2);

        Assert.Equal(0, rect.Layer);
        Assert.Equal("score", text.Text);
        Assert.Equal(1f, text.Scale);
        Assert.Equal("logo.png", image.SourcePath);
        Assert.Equal(0, image.MediaId);
        Assert.Equal(3, image.Layer);
        Assert.Equal(string.Empty, mediaImage.SourcePath);
        Assert.Equal(12, mediaImage.MediaId);
        Assert.Equal(12f, video.FramesPerSecond);
        Assert.True(video.Loop);
        Assert.True(video.Playing);
        Assert.Equal(0, video.MediaId);
        Assert.Equal(string.Empty, mediaVideo.SourcePath);
        Assert.Equal(9, mediaVideo.MediaId);
        Assert.Equal(24f, mediaVideo.FramesPerSecond);
        Assert.False(mediaVideo.Loop);
        Assert.False(mediaVideo.Playing);
        Assert.Equal(2, mediaVideo.Layer);
    }

    [Fact]
    public void UnpixComponents_UseDocumentedDefaults()
    {
        var effect = new UnpixComponent(0.25f, 3f);
        var piece = new UnpixPieceComponent
        {
            Age = 1f,
            Lifetime = 2f,
            StartScale = Vector3.One,
            UsePhysics = true,
            Velocity = Vector3.UnitX
        };

        Assert.Equal(0.25f, effect.CubeSize);
        Assert.Equal(3f, effect.DissolveDuration);
        Assert.Equal(0f, effect.StartDelay);
        Assert.Equal(0.6f, effect.VelocityScale);
        Assert.True(effect.SpawnOnStart);
        Assert.True(effect.HideSource);
        Assert.Equal(4000, effect.MaxCubes);
        Assert.False(effect.UsePhysics);
        Assert.Equal(0.5f, effect.CubeMass);
        Assert.Equal(0.3f, effect.CubeRestitution);
        Assert.Equal(0.5f, effect.CubeFriction);
        Assert.Equal(2f, effect.InitialImpulseScale);
        Assert.Equal(1f, piece.Age);
        Assert.True(piece.UsePhysics);
        Assert.Equal(Vector3.UnitX, piece.Velocity);
    }

    [Fact]
    public void InputComponents_ReadMissingAndPresentActions()
    {
        var gameplay = InputReceiverComponent.CreateGameplay(2);
        var ui = InputReceiverComponent.CreateUI();
        var custom = InputReceiverComponent.Create("Driving", 3);
        gameplay.ButtonStates["Jump"] = true;
        gameplay.ButtonPressed["Jump"] = true;
        gameplay.ButtonReleased["Crouch"] = true;
        gameplay.AxisValues["Throttle"] = 0.5f;
        gameplay.Vector2Values["Move"] = new Vector2(1f, -1f);
        gameplay.Vector3Values["GyroLook"] = new Vector3(0.2f, 0.3f, 0.4f);

        Assert.Equal("Gameplay", gameplay.ActionMapName);
        Assert.True(gameplay.IsActive);
        Assert.Equal(2, gameplay.PlayerIndex);
        Assert.Equal("UI", ui.ActionMapName);
        Assert.Equal(0, ui.PlayerIndex);
        Assert.Equal("Driving", custom.ActionMapName);
        Assert.Equal(3, custom.PlayerIndex);
        Assert.True(gameplay.IsButtonPressed("Jump"));
        Assert.False(gameplay.IsButtonPressed("Missing"));
        Assert.True(gameplay.WasButtonPressedThisFrame("Jump"));
        Assert.False(gameplay.WasButtonPressedThisFrame("Crouch"));
        Assert.True(gameplay.WasButtonReleasedThisFrame("Crouch"));
        Assert.False(gameplay.WasButtonReleasedThisFrame("Jump"));
        Assert.Equal(0.5f, gameplay.GetAxis("Throttle"));
        Assert.Equal(0f, gameplay.GetAxis("Missing"));
        Assert.Equal(new Vector2(1f, -1f), gameplay.GetVector2("Move"));
        Assert.Equal(Vector2.Zero, gameplay.GetVector2("Missing"));
        Assert.Equal(new Vector3(0.2f, 0.3f, 0.4f), gameplay.GetVector3("GyroLook"));
        Assert.Equal(Vector3.Zero, gameplay.GetVector3("Missing"));

        var haptic = HapticSourceComponent.Create(1, 0.25f);
        Assert.True(haptic.Enabled);
        Assert.Equal(0.25f, haptic.IntensityMultiplier);
        Assert.Equal(1, haptic.PlayerIndex);
        haptic.Enabled = false;
        Assert.False(haptic.Enabled);
    }

    [Fact]
    public void TerminalComponent_StoresSlotAndAutoLoad()
    {
        var defaults = new TerminalComponent();
        var custom = new TerminalComponent("slot-a", false);

        Assert.Null(defaults.SaveSlot);
        Assert.False(defaults.AutoLoad);
        Assert.Equal("slot-a", custom.SaveSlot);
        Assert.False(custom.AutoLoad);
    }

    [Fact]
    public void CameraComponent_BuildsPerspectiveOrthographicAndInvalidMatrices()
    {
        var perspective = CameraComponent.CreatePerspective(90f, 1f, 0.1f, 50f);
        var orthographic = CameraComponent.CreateOrthographic(5f, 2f, 0.1f, 100f);
        var transform = new TransformComponent();

        Assert.Equal(ProjectionType.Perspective, perspective.ProjectionType);
        Assert.True(perspective.IsActive);
        Assert.Equal(0, perspective.Priority);
        Assert.Equal(5f, perspective.OrthographicSize);
        Assert.Equal(ProjectionType.Orthographic, orthographic.ProjectionType);
        Assert.Equal(60f, orthographic.FieldOfView);
        Assert.Equal(5f, orthographic.OrthographicSize);

        AssertMatrixEqual(
            Matrix4x4.CreatePerspectiveFieldOfView(90f * MathF.PI / 180f, 1f, 0.1f, 50f),
            perspective.GetProjectionMatrix());
        AssertMatrixEqual(
            Matrix4x4.CreateOrthographic(20f, 10f, 0.1f, 100f),
            orthographic.GetProjectionMatrix());

        var invalid = perspective;
        invalid.ProjectionType = (ProjectionType)42;
        AssertMatrixEqual(Matrix4x4.Identity, invalid.GetProjectionMatrix());

        var view = CameraComponent.GetViewMatrix(transform);
        AssertMatrixEqual(Matrix4x4.CreateLookAt(Vector3.Zero, transform.Forward, transform.Up), view);
        AssertMatrixEqual(view * perspective.GetProjectionMatrix(), perspective.GetViewProjectionMatrix(transform));
    }

    [Fact]
    public void RenderSettingsComponent_DefaultsProfileAndAutoDetect()
    {
        var manual = new RenderSettingsComponent(RenderMode.Rasterized);
        Assert.True(manual.Enabled);
        Assert.Equal(RenderMode.Rasterized, manual.Mode);
        Assert.Equal(2, manual.RaysPerPixel);
        Assert.Equal(2, manual.MaxBounces);
        Assert.Equal(1, manual.SamplesPerFrame);
        Assert.True(manual.Accumulate);
        Assert.False(manual.Denoise);
        Assert.Equal(RaytracerQualityPreset.Balanced, manual.Preset);
        Assert.Equal(LensingQuality.Medium, manual.LensingQuality);
        Assert.Equal(64, manual.LensingMaxSteps);
        Assert.Equal(0.4f, manual.LensingStepSize);
        Assert.False(manual.ShowErgosphere);
        Assert.Equal(0.3f, manual.ErgosphereOpacity);
        Assert.Equal(PerformanceProfileType.Auto, manual.PerformanceProfile);
        Assert.False(manual.UsePerformanceProfile);

        var balanced = PerformanceProfiles.Get(PerformanceProfileType.Balanced);
        var fromProfile = RenderSettingsComponent.FromProfile(PerformanceProfileType.Balanced, RenderMode.Raytraced);
        Assert.True(fromProfile.UsePerformanceProfile);
        Assert.Equal(PerformanceProfileType.Balanced, fromProfile.PerformanceProfile);
        Assert.Equal(balanced.RaysPerPixel, fromProfile.RaysPerPixel);
        Assert.Equal(balanced.MaxBounces, fromProfile.MaxBounces);
        Assert.Equal(balanced.SamplesPerFrame, fromProfile.SamplesPerFrame);
        Assert.Equal(balanced.Accumulate, fromProfile.Accumulate);
        Assert.Equal(balanced.Denoise, fromProfile.Denoise);
        Assert.Equal(balanced.LensingQuality, fromProfile.LensingQuality);
        Assert.Equal(balanced.LensingMaxSteps, fromProfile.LensingMaxSteps);
        Assert.Equal(balanced.ShowErgosphere, fromProfile.ShowErgosphere);
        Assert.Equal(balanced.ShowPhotonSphere, fromProfile.ShowPhotonSphere);

        var auto = PerformanceProfiles.Get(PerformanceProfileType.Auto);
        var detected = RenderSettingsComponent.AutoDetect(RenderMode.Rasterized);
        Assert.Equal(RenderMode.Rasterized, detected.Mode);
        Assert.True(detected.UsePerformanceProfile);
        Assert.Equal(PerformanceProfileType.Auto, detected.PerformanceProfile);
        Assert.Equal(auto.RaysPerPixel, detected.RaysPerPixel);
        Assert.Equal(auto.LensingQuality, detected.LensingQuality);
    }

    [Fact]
    public void AccretionDiskComponent_ScalesRadiiFromMass()
    {
        var disk = AccretionDiskComponent.CreateForBlackHole(2f);
        var custom = AccretionDiskComponent.Create(4f, 9f, 8000f, 2500f);

        Assert.Equal(12f, disk.InnerRadius);
        Assert.Equal(60f, disk.OuterRadius);
        Assert.Equal(10000f, disk.InnerTemperature);
        Assert.Equal(3000f, disk.OuterTemperature);
        Assert.Equal(0.1f, disk.Thickness);
        Assert.Equal(1f, disk.AngularVelocity);
        Assert.Equal(1f, disk.Opacity);
        Assert.True(disk.DopplerBeaming);
        Assert.True(disk.GravitationalRedshift);
        Assert.Equal(4f, custom.InnerRadius);
        Assert.Equal(9f, custom.OuterRadius);
        Assert.Equal(8000f, custom.InnerTemperature);
        Assert.Equal(2500f, custom.OuterTemperature);
    }

    [Fact]
    public void MeshAndMaterial_TrackHandlesAndPbrSelection()
    {
        var handle = default(ResourceHandle<Mesh>);
        var mesh = new MeshComponent();
        var assigned = new MeshComponent(handle);

        Assert.False(handle.IsValid);
        Assert.Null(handle.Get());
        Assert.False(handle.TryGet(out var loaded));
        Assert.Null(loaded);
        Assert.True(handle == default(ResourceHandle<Mesh>));
        Assert.Contains("Mesh", handle.ToString());
        Assert.True(mesh.CastShadows);
        Assert.True(mesh.ReceiveShadows);
        Assert.Equal(1u, mesh.LayerMask);
        Assert.True(mesh.Visible);
        Assert.Equal(handle, assigned.MeshHandle);

        var material = MaterialComponent.Default;
        var library = MaterialComponent.FromPbrMaterial(3);
        var emissive = MaterialComponent.CreateEmissive(new Vector3(1f, 0f, 0f), 2f);

        Assert.False(material.UsesPbrMaterial);
        Assert.Equal(-1, material.PbrMaterialId);
        Assert.Equal(Vector4.One, material.BaseColor);
        Assert.Equal(0.5f, material.Roughness);
        Assert.True(library.UsesPbrMaterial);
        Assert.Equal(3, library.PbrMaterialId);
        Assert.False(MaterialComponent.FromPbrMaterial(-1).UsesPbrMaterial);
        Assert.Equal(new Vector3(2f, 0f, 0f), emissive.EmissiveColor);
        Assert.False(emissive.UseRaytracing);
    }

    [Fact]
    public void LaserComponents_FactoriesAndDataFields()
    {
        var beam = LaserEmitterComponent.CreateBeam(80f, 0.2f, new Vector4(0f, 1f, 0f, 1f));
        var pulse = LaserEmitterComponent.CreatePulse(40f, 1.5f, 8f);
        var scan = LaserEmitterComponent.CreateScanning(30f, 45f);

        Assert.Equal(LaserType.Beam, beam.Type);
        Assert.True(beam.Enabled);
        Assert.True(beam.FireOnStart);
        Assert.Equal(80f, beam.MaxRange);
        Assert.Equal(0.2f, beam.BeamWidth);
        Assert.Equal(new Vector4(0f, 1f, 0f, 1f), beam.BeamColor);
        Assert.Equal(0.3f, beam.CoreWidth);
        Assert.True(beam.UseRaycast);
        Assert.Equal(uint.MaxValue, beam.CollisionMask);
        Assert.Equal(10f, beam.Damage);
        Assert.False(beam.CanRedirect);

        Assert.Equal(LaserType.Pulse, pulse.Type);
        Assert.False(pulse.FireOnStart);
        Assert.True(pulse.AutoFire);
        Assert.Equal(40f, pulse.PulseSpeed);
        Assert.Equal(1.5f, pulse.PulseLength);
        Assert.Equal(8f, pulse.FireRate);
        Assert.True(pulse.CanRedirect);
        Assert.Equal(3, pulse.MaxBounces);
        Assert.Equal(45f, pulse.RedirectAngle);
        Assert.Equal(25f, pulse.Damage);

        Assert.Equal(LaserType.Scanning, scan.Type);
        Assert.Equal(30f, scan.ScanSpeed);
        Assert.Equal(45f, scan.ScanAngle);
        Assert.Equal(Vector3.UnitY, scan.ScanAxis);
        Assert.Equal(5f, scan.Damage);
        Assert.False(scan.CanRedirect);

        var shot = new LaserPulseComponent
        {
            Position = Vector3.UnitX,
            Direction = Vector3.UnitZ,
            Speed = 10f,
            Length = 0.4f,
            DistanceTraveled = 1f,
            MaxRange = 20f,
            BouncesRemaining = 2,
            RedirectAngle = 15f,
            Width = 0.1f,
            Damage = 4f,
            PushForce = 3f,
            EmitParticles = true
        };
        var segment = new LaserSegmentComponent
        {
            Start = Vector3.Zero,
            End = Vector3.UnitY,
            Width = 0.2f,
            Intensity = 0.5f,
            ShowEnergyPulses = true,
            EnergyPulseCount = 4,
            EnergyPulseSpeed = 6f,
            EnergyPulseSize = 0.1f
        };
        var hit = new LaserHitComponent
        {
            HitPoint = Vector3.One,
            HitNormal = Vector3.UnitY,
            Damage = 2f,
            PushDirection = Vector3.UnitX,
            PushForce = 1f
        };
        var redirect = new LaserRedirectComponent
        {
            Type = LaserRedirectComponent.RedirectType.Split,
            Efficiency = 0.8f,
            RefractAngle = 12f,
            SplitCount = 2,
            TintColor = Vector4.One
        };

        Assert.Equal(10f, shot.Speed);
        Assert.Equal(2, shot.BouncesRemaining);
        Assert.True(shot.EmitParticles);
        Assert.Equal(Vector3.UnitY, segment.End);
        Assert.Equal(4, segment.EnergyPulseCount);
        Assert.Equal(2f, hit.Damage);
        Assert.Equal(LaserRedirectComponent.RedirectType.Split, redirect.Type);
        Assert.Equal(0.8f, redirect.Efficiency);
        Assert.Equal(2, redirect.SplitCount);
    }

    private static void AssertMatrixEqual(Matrix4x4 expected, Matrix4x4 actual)
    {
        Assert.Equal(expected.M11, actual.M11, Epsilon);
        Assert.Equal(expected.M12, actual.M12, Epsilon);
        Assert.Equal(expected.M13, actual.M13, Epsilon);
        Assert.Equal(expected.M14, actual.M14, Epsilon);
        Assert.Equal(expected.M21, actual.M21, Epsilon);
        Assert.Equal(expected.M22, actual.M22, Epsilon);
        Assert.Equal(expected.M23, actual.M23, Epsilon);
        Assert.Equal(expected.M24, actual.M24, Epsilon);
        Assert.Equal(expected.M31, actual.M31, Epsilon);
        Assert.Equal(expected.M32, actual.M32, Epsilon);
        Assert.Equal(expected.M33, actual.M33, Epsilon);
        Assert.Equal(expected.M34, actual.M34, Epsilon);
        Assert.Equal(expected.M41, actual.M41, Epsilon);
        Assert.Equal(expected.M42, actual.M42, Epsilon);
        Assert.Equal(expected.M43, actual.M43, Epsilon);
        Assert.Equal(expected.M44, actual.M44, Epsilon);
    }
}
