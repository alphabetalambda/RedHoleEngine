using System.Numerics;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Commands;

namespace RedHoleEngine.Tests.Editor;

public class UntrackedEditDetectorTests
{
    private static (World World, Entity Entity) CreateWorld()
    {
        var world = new World();
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TransformComponent(Vector3.Zero));
        world.AddComponent(entity, ColliderComponent.CreateSphere(1f));
        return (world, entity);
    }

    [Fact]
    public void Snapshot_EqualWhenNothingChanged()
    {
        var (world, entity) = CreateWorld();
        Assert.Equal(EntityStateSnapshot.Capture(world, entity), EntityStateSnapshot.Capture(world, entity));
    }

    [Fact]
    public void Snapshot_DetectsFieldChange()
    {
        var (world, entity) = CreateWorld();
        var before = EntityStateSnapshot.Capture(world, entity);

        world.GetComponent<ColliderComponent>(entity).SphereRadius = 2f;

        Assert.NotEqual(before, EntityStateSnapshot.Capture(world, entity));
    }

    [Fact]
    public void Snapshot_DetectsTransformChange()
    {
        var (world, entity) = CreateWorld();
        var before = EntityStateSnapshot.Capture(world, entity);

        world.GetComponent<TransformComponent>(entity).Position = new Vector3(1, 2, 3);

        Assert.NotEqual(before, EntityStateSnapshot.Capture(world, entity));
    }

    [Fact]
    public void Snapshot_DetectsComponentAddRemoveAndDestroy()
    {
        var (world, entity) = CreateWorld();
        var before = EntityStateSnapshot.Capture(world, entity);

        world.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));
        var added = EntityStateSnapshot.Capture(world, entity);
        Assert.NotEqual(before, added);

        world.RemoveComponent<RigidBodyComponent>(entity);
        Assert.Equal(before, EntityStateSnapshot.Capture(world, entity));

        world.DestroyEntity(entity);
        Assert.NotEqual(before, EntityStateSnapshot.Capture(world, entity));
    }

    [Fact]
    public void Detector_ReportsInstantUntrackedEdit()
    {
        var (world, entity) = CreateWorld();
        var detector = new UntrackedEditDetector();

        detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), undoVersion: 5);
        world.GetComponent<ColliderComponent>(entity).IsTrigger = true;
        var report = detector.EndFrame(EntityStateSnapshot.Capture(world, entity), undoVersion: 5, interactionActive: false);

        Assert.True(report);
    }

    [Fact]
    public void Detector_IgnoresFramesWithoutChanges()
    {
        var (world, entity) = CreateWorld();
        var detector = new UntrackedEditDetector();

        detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), 1);
        Assert.False(detector.EndFrame(EntityStateSnapshot.Capture(world, entity), 1, interactionActive: false));
    }

    [Fact]
    public void Detector_ReportsUntrackedDragOnlyWhenReleased()
    {
        var (world, entity) = CreateWorld();
        var detector = new UntrackedEditDetector();

        // Frames while dragging: value changes, widget active
        for (int i = 0; i < 3; i++)
        {
            detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), 7);
            world.GetComponent<ColliderComponent>(entity).SphereRadius += 0.1f;
            Assert.False(detector.EndFrame(EntityStateSnapshot.Capture(world, entity), 7, interactionActive: true));
        }

        // Release frame: no further change, nothing active, no undo command recorded
        detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), 7);
        Assert.True(detector.EndFrame(EntityStateSnapshot.Capture(world, entity), 7, interactionActive: false));

        // Reported once only
        detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), 7);
        Assert.False(detector.EndFrame(EntityStateSnapshot.Capture(world, entity), 7, interactionActive: false));
    }

    [Fact]
    public void Detector_DoesNotReportDragRecordedByUndo()
    {
        var (world, entity) = CreateWorld();
        var detector = new UntrackedEditDetector();

        detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), 3);
        world.GetComponent<TransformComponent>(entity).Position = Vector3.One;
        Assert.False(detector.EndFrame(EntityStateSnapshot.Capture(world, entity), 3, interactionActive: true));

        // On release the panel records an undo command, bumping the version
        detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), 3);
        Assert.False(detector.EndFrame(EntityStateSnapshot.Capture(world, entity), 4, interactionActive: false));
    }

    [Fact]
    public void Detector_DoesNotReportInstantEditRecordedByUndo()
    {
        var (world, entity) = CreateWorld();
        var detector = new UntrackedEditDetector();

        detector.BeginFrame(EntityStateSnapshot.Capture(world, entity), 10);
        world.GetComponent<ColliderComponent>(entity).IsTrigger = true;
        Assert.False(detector.EndFrame(EntityStateSnapshot.Capture(world, entity), 11, interactionActive: false));
    }
}
