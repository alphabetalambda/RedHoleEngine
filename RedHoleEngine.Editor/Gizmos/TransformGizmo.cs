using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Commands;
using RedHoleEngine.Editor.Selection;
using RedHoleEngine.Engine;

namespace RedHoleEngine.Editor.Gizmos;

/// <summary>
/// Gizmo operation mode
/// </summary>
public enum GizmoMode
{
    Translate,
    Rotate,
    Scale
}

/// <summary>
/// Gizmo coordinate space
/// </summary>
public enum GizmoSpace
{
    World,
    Local
}

/// <summary>
/// Which axis/plane is being manipulated
/// </summary>
public enum GizmoAxis
{
    None,
    X,
    Y,
    Z,
    XY,
    XZ,
    YZ,
    All // Center/uniform scale
}

/// <summary>
/// Interactive transform gizmo for manipulating entity transforms in the viewport
/// </summary>
public class TransformGizmo
{
    // Colors
    private static readonly Vector4 ColorX = new(0.95f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 ColorY = new(0.3f, 0.95f, 0.35f, 1f);
    private static readonly Vector4 ColorZ = new(0.3f, 0.55f, 1f, 1f);
    private static readonly Vector4 ColorHighlight = new(1f, 0.9f, 0.2f, 1f);
    private static readonly Vector4 ColorPlane = new(0.8f, 0.8f, 0.2f, 0.4f);

    // Configuration
    public GizmoMode Mode { get; set; } = GizmoMode.Translate;
    public GizmoSpace Space { get; set; } = GizmoSpace.World;
    public float AxisLength { get; set; } = 1.5f;
    public float AxisThickness { get; set; } = 3f;
    public float HitRadius { get; set; } = 8f; // Screen pixels
    public float PlaneSize { get; set; } = 0.4f; // Fraction of axis length
    public float RotationRingRadius { get; set; } = 1.2f;
    public int RotationRingSegments { get; set; } = 48;

    // State
    private GizmoAxis _hoveredAxis = GizmoAxis.None;
    private GizmoAxis _activeAxis = GizmoAxis.None;
    private bool _isDragging;
    private Vector2 _dragStartMouse;
    private Vector3 _dragStartPosition;
    private Quaternion _dragStartRotation;
    private Vector3 _dragStartScale;
    private Vector3 _dragPlaneNormal;
    private Vector3 _dragPlaneOrigin;
    private float _dragStartAngle;
    
    // Cached for undo
    private TransformComponent _originalTransform;

    /// <summary>
    /// Whether the gizmo is currently being dragged
    /// </summary>
    public bool IsDragging => _isDragging;

    /// <summary>
    /// Currently hovered axis (for highlighting)
    /// </summary>
    public GizmoAxis HoveredAxis => _hoveredAxis;

    /// <summary>
    /// Update gizmo state and handle input
    /// </summary>
    public void Update(
        World world,
        SelectionManager selection,
        UndoRedoManager undoRedo,
        Camera camera,
        Vector2 viewportMin,
        Vector2 viewportSize,
        Vector2 mousePos,
        bool mouseDown,
        bool mouseClicked,
        bool mouseReleased)
    {
        if (!selection.HasSelection) return;

        var entity = selection.PrimarySelection;
        if (!world.IsAlive(entity) || !world.HasComponent<TransformComponent>(entity))
            return;

        ref var transform = ref world.GetComponent<TransformComponent>(entity);
        var origin = transform.Position;

        // Calculate view/projection matrices
        float aspect = viewportSize.X / Math.Max(1f, viewportSize.Y);
        var view = camera.GetViewMatrix();
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            camera.FieldOfView * MathF.PI / 180f,
            aspect,
            0.1f,
            10000f);
        var viewProj = view * projection;

        // Calculate screen-space scale for consistent gizmo size
        float distanceToCamera = Vector3.Distance(origin, camera.Position);
        float screenScale = distanceToCamera * 0.1f;
        float scaledAxisLength = AxisLength * screenScale;

        if (!_isDragging)
        {
            // Hit testing
            _hoveredAxis = HitTest(origin, scaledAxisLength, viewProj, viewportMin, viewportSize, mousePos);

            if (mouseClicked && _hoveredAxis != GizmoAxis.None)
            {
                StartDrag(ref transform, mousePos, origin, camera, viewProj, viewportMin, viewportSize);
            }
        }
        else
        {
            // Continue dragging
            UpdateDrag(ref transform, mousePos, origin, camera, viewProj, viewportMin, viewportSize, screenScale);

            if (mouseReleased)
            {
                EndDrag(world, entity, undoRedo, ref transform);
            }
        }
    }

    private void StartDrag(ref TransformComponent transform, Vector2 mousePos, Vector3 origin, Camera camera, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        _isDragging = true;
        _activeAxis = _hoveredAxis;
        _dragStartMouse = mousePos;
        _dragStartPosition = transform.LocalPosition;
        _dragStartRotation = transform.LocalRotation;
        _dragStartScale = transform.LocalScale;
        _originalTransform = transform;

        // Calculate drag plane based on mode and axis
        CalculateDragPlane(origin, camera);

        if (Mode == GizmoMode.Rotate)
        {
            _dragStartAngle = CalculateAngleOnPlane(mousePos, origin, viewProj, viewportMin, viewportSize);
        }
    }

    private void UpdateDrag(ref TransformComponent transform, Vector2 mousePos, Vector3 origin, Camera camera, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize, float screenScale)
    {
        switch (Mode)
        {
            case GizmoMode.Translate:
                UpdateTranslation(ref transform, mousePos, origin, camera, viewProj, viewportMin, viewportSize);
                break;
            case GizmoMode.Rotate:
                UpdateRotation(ref transform, mousePos, origin, viewProj, viewportMin, viewportSize);
                break;
            case GizmoMode.Scale:
                UpdateScale(ref transform, mousePos, screenScale);
                break;
        }
    }

    private void UpdateTranslation(ref TransformComponent transform, Vector2 mousePos, Vector3 origin, Camera camera, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        // Cast ray from mouse position
        var ray = ScreenToRay(mousePos, camera, viewProj, viewportMin, viewportSize);
        
        // Intersect with drag plane
        if (RayPlaneIntersection(ray.Origin, ray.Direction, _dragPlaneOrigin, _dragPlaneNormal, out var hitPoint))
        {
            var delta = hitPoint - _dragPlaneOrigin;

            // Constrain to axis if single axis selected
            switch (_activeAxis)
            {
                case GizmoAxis.X:
                    delta = new Vector3(Vector3.Dot(delta, Vector3.UnitX), 0, 0);
                    break;
                case GizmoAxis.Y:
                    delta = new Vector3(0, Vector3.Dot(delta, Vector3.UnitY), 0);
                    break;
                case GizmoAxis.Z:
                    delta = new Vector3(0, 0, Vector3.Dot(delta, Vector3.UnitZ));
                    break;
                case GizmoAxis.XY:
                    delta = new Vector3(delta.X, delta.Y, 0);
                    break;
                case GizmoAxis.XZ:
                    delta = new Vector3(delta.X, 0, delta.Z);
                    break;
                case GizmoAxis.YZ:
                    delta = new Vector3(0, delta.Y, delta.Z);
                    break;
            }

            transform.LocalPosition = _dragStartPosition + delta;
        }
    }

    private void UpdateRotation(ref TransformComponent transform, Vector2 mousePos, Vector3 origin, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        float currentAngle = CalculateAngleOnPlane(mousePos, origin, viewProj, viewportMin, viewportSize);
        float deltaAngle = currentAngle - _dragStartAngle;

        Vector3 axis = _activeAxis switch
        {
            GizmoAxis.X => Vector3.UnitX,
            GizmoAxis.Y => Vector3.UnitY,
            GizmoAxis.Z => Vector3.UnitZ,
            _ => Vector3.UnitY
        };

        var rotation = Quaternion.CreateFromAxisAngle(axis, deltaAngle);
        transform.LocalRotation = rotation * _dragStartRotation;
    }

    private void UpdateScale(ref TransformComponent transform, Vector2 mousePos, float screenScale)
    {
        var delta = mousePos - _dragStartMouse;
        float scaleFactor = 1f + delta.X * 0.01f;
        scaleFactor = Math.Max(0.01f, scaleFactor);

        switch (_activeAxis)
        {
            case GizmoAxis.X:
                transform.LocalScale = new Vector3(_dragStartScale.X * scaleFactor, _dragStartScale.Y, _dragStartScale.Z);
                break;
            case GizmoAxis.Y:
                transform.LocalScale = new Vector3(_dragStartScale.X, _dragStartScale.Y * scaleFactor, _dragStartScale.Z);
                break;
            case GizmoAxis.Z:
                transform.LocalScale = new Vector3(_dragStartScale.X, _dragStartScale.Y, _dragStartScale.Z * scaleFactor);
                break;
            case GizmoAxis.All:
                transform.LocalScale = _dragStartScale * scaleFactor;
                break;
            default:
                transform.LocalScale = _dragStartScale * scaleFactor;
                break;
        }
    }

    private void EndDrag(World world, Entity entity, UndoRedoManager undoRedo, ref TransformComponent transform)
    {
        _isDragging = false;
        _activeAxis = GizmoAxis.None;

        // Create undo command if transform changed
        if (transform.LocalPosition != _originalTransform.LocalPosition ||
            transform.LocalRotation != _originalTransform.LocalRotation ||
            transform.LocalScale != _originalTransform.LocalScale)
        {
            var command = new ModifyComponentCommand<TransformComponent>(
                world, entity, _originalTransform, transform,
                Mode.ToString());
            
            // Execute without re-applying since we already modified the transform
            // Just push to undo stack
            undoRedo.ExecuteCommand(new AlreadyAppliedCommand(command));
        }
    }

    private void CalculateDragPlane(Vector3 origin, Camera camera)
    {
        _dragPlaneOrigin = origin;
        var cameraForward = camera.Forward;

        switch (_activeAxis)
        {
            case GizmoAxis.X:
                // Use plane perpendicular to X that faces camera most
                _dragPlaneNormal = Math.Abs(cameraForward.Y) > Math.Abs(cameraForward.Z) 
                    ? Vector3.UnitY : Vector3.UnitZ;
                break;
            case GizmoAxis.Y:
                _dragPlaneNormal = Math.Abs(cameraForward.X) > Math.Abs(cameraForward.Z) 
                    ? Vector3.UnitX : Vector3.UnitZ;
                break;
            case GizmoAxis.Z:
                _dragPlaneNormal = Math.Abs(cameraForward.X) > Math.Abs(cameraForward.Y) 
                    ? Vector3.UnitX : Vector3.UnitY;
                break;
            case GizmoAxis.XY:
                _dragPlaneNormal = Vector3.UnitZ;
                break;
            case GizmoAxis.XZ:
                _dragPlaneNormal = Vector3.UnitY;
                break;
            case GizmoAxis.YZ:
                _dragPlaneNormal = Vector3.UnitX;
                break;
            default:
                _dragPlaneNormal = -cameraForward;
                break;
        }
    }

    private float CalculateAngleOnPlane(Vector2 mousePos, Vector3 origin, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        // Project origin to screen
        if (!TryProject(origin, viewProj, viewportMin, viewportSize, out var screenOrigin))
            return 0;

        var dir = mousePos - screenOrigin;
        return MathF.Atan2(dir.Y, dir.X);
    }

    /// <summary>
    /// Perform hit testing against gizmo axes
    /// </summary>
    private GizmoAxis HitTest(Vector3 origin, float axisLength, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize, Vector2 mousePos)
    {
        if (!TryProject(origin, viewProj, viewportMin, viewportSize, out var screenOrigin))
            return GizmoAxis.None;

        // Test axis endpoints
        var xEnd = origin + Vector3.UnitX * axisLength;
        var yEnd = origin + Vector3.UnitY * axisLength;
        var zEnd = origin + Vector3.UnitZ * axisLength;

        TryProject(xEnd, viewProj, viewportMin, viewportSize, out var screenX);
        TryProject(yEnd, viewProj, viewportMin, viewportSize, out var screenY);
        TryProject(zEnd, viewProj, viewportMin, viewportSize, out var screenZ);

        // Test against each axis line
        float distX = DistanceToLineSegment(mousePos, screenOrigin, screenX);
        float distY = DistanceToLineSegment(mousePos, screenOrigin, screenY);
        float distZ = DistanceToLineSegment(mousePos, screenOrigin, screenZ);

        float minDist = Math.Min(distX, Math.Min(distY, distZ));

        if (minDist > HitRadius)
            return GizmoAxis.None;

        if (Mode == GizmoMode.Scale)
        {
            // Check center box for uniform scale
            float distCenter = Vector2.Distance(mousePos, screenOrigin);
            if (distCenter < HitRadius * 2)
                return GizmoAxis.All;
        }

        // Return closest axis
        if (distX == minDist) return GizmoAxis.X;
        if (distY == minDist) return GizmoAxis.Y;
        return GizmoAxis.Z;
    }

    /// <summary>
    /// Draw the gizmo overlay
    /// </summary>
    public void Draw(ImDrawListPtr drawList, World world, SelectionManager selection, Camera camera, Vector2 viewportMin, Vector2 viewportSize)
    {
        if (!selection.HasSelection) return;

        var entity = selection.PrimarySelection;
        if (!world.IsAlive(entity) || !world.HasComponent<TransformComponent>(entity))
            return;

        ref var transform = ref world.GetComponent<TransformComponent>(entity);
        var origin = transform.Position;

        // Calculate matrices
        float aspect = viewportSize.X / Math.Max(1f, viewportSize.Y);
        var view = camera.GetViewMatrix();
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(
            camera.FieldOfView * MathF.PI / 180f,
            aspect,
            0.1f,
            10000f);
        var viewProj = view * projection;

        // Screen-space consistent size
        float distanceToCamera = Vector3.Distance(origin, camera.Position);
        float screenScale = distanceToCamera * 0.1f;
        float scaledAxisLength = AxisLength * screenScale;

        switch (Mode)
        {
            case GizmoMode.Translate:
                DrawTranslateGizmo(drawList, origin, scaledAxisLength, viewProj, viewportMin, viewportSize);
                break;
            case GizmoMode.Rotate:
                DrawRotateGizmo(drawList, origin, scaledAxisLength, viewProj, viewportMin, viewportSize);
                break;
            case GizmoMode.Scale:
                DrawScaleGizmo(drawList, origin, scaledAxisLength, viewProj, viewportMin, viewportSize);
                break;
        }
    }

    private void DrawTranslateGizmo(ImDrawListPtr drawList, Vector3 origin, float axisLength, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        if (!TryProject(origin, viewProj, viewportMin, viewportSize, out var screenOrigin))
            return;

        // Draw axes with arrows
        DrawAxis(drawList, origin, Vector3.UnitX, axisLength, GetAxisColor(GizmoAxis.X), viewProj, viewportMin, viewportSize);
        DrawAxis(drawList, origin, Vector3.UnitY, axisLength, GetAxisColor(GizmoAxis.Y), viewProj, viewportMin, viewportSize);
        DrawAxis(drawList, origin, Vector3.UnitZ, axisLength, GetAxisColor(GizmoAxis.Z), viewProj, viewportMin, viewportSize);

        // Draw plane handles
        float planeOffset = axisLength * PlaneSize;
        DrawPlaneHandle(drawList, origin, Vector3.UnitX, Vector3.UnitY, planeOffset, GizmoAxis.XY, viewProj, viewportMin, viewportSize);
        DrawPlaneHandle(drawList, origin, Vector3.UnitX, Vector3.UnitZ, planeOffset, GizmoAxis.XZ, viewProj, viewportMin, viewportSize);
        DrawPlaneHandle(drawList, origin, Vector3.UnitY, Vector3.UnitZ, planeOffset, GizmoAxis.YZ, viewProj, viewportMin, viewportSize);
    }

    private void DrawRotateGizmo(ImDrawListPtr drawList, Vector3 origin, float axisLength, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        float radius = axisLength * RotationRingRadius;

        // Draw rotation rings for each axis
        DrawRotationRing(drawList, origin, Vector3.UnitX, radius, GetAxisColor(GizmoAxis.X), viewProj, viewportMin, viewportSize);
        DrawRotationRing(drawList, origin, Vector3.UnitY, radius, GetAxisColor(GizmoAxis.Y), viewProj, viewportMin, viewportSize);
        DrawRotationRing(drawList, origin, Vector3.UnitZ, radius, GetAxisColor(GizmoAxis.Z), viewProj, viewportMin, viewportSize);
    }

    private void DrawScaleGizmo(ImDrawListPtr drawList, Vector3 origin, float axisLength, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        if (!TryProject(origin, viewProj, viewportMin, viewportSize, out var screenOrigin))
            return;

        // Draw axes with boxes at ends
        DrawAxisWithBox(drawList, origin, Vector3.UnitX, axisLength, GetAxisColor(GizmoAxis.X), viewProj, viewportMin, viewportSize);
        DrawAxisWithBox(drawList, origin, Vector3.UnitY, axisLength, GetAxisColor(GizmoAxis.Y), viewProj, viewportMin, viewportSize);
        DrawAxisWithBox(drawList, origin, Vector3.UnitZ, axisLength, GetAxisColor(GizmoAxis.Z), viewProj, viewportMin, viewportSize);

        // Draw center box for uniform scale
        var centerColor = (_hoveredAxis == GizmoAxis.All || _activeAxis == GizmoAxis.All) ? ColorHighlight : new Vector4(0.8f, 0.8f, 0.8f, 1f);
        drawList.AddRectFilled(
            screenOrigin - new Vector2(6, 6),
            screenOrigin + new Vector2(6, 6),
            ImGui.GetColorU32(centerColor));
    }

    private void DrawAxis(ImDrawListPtr drawList, Vector3 origin, Vector3 direction, float length, Vector4 color, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        var end = origin + direction * length;
        
        if (!TryProject(origin, viewProj, viewportMin, viewportSize, out var screenStart) ||
            !TryProject(end, viewProj, viewportMin, viewportSize, out var screenEnd))
            return;

        drawList.AddLine(screenStart, screenEnd, ImGui.GetColorU32(color), AxisThickness);

        // Draw arrow head
        var dir = Vector2.Normalize(screenEnd - screenStart);
        var perp = new Vector2(-dir.Y, dir.X);
        var arrowSize = 8f;
        
        drawList.AddTriangleFilled(
            screenEnd,
            screenEnd - dir * arrowSize + perp * arrowSize * 0.5f,
            screenEnd - dir * arrowSize - perp * arrowSize * 0.5f,
            ImGui.GetColorU32(color));
    }

    private void DrawAxisWithBox(ImDrawListPtr drawList, Vector3 origin, Vector3 direction, float length, Vector4 color, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        var end = origin + direction * length;
        
        if (!TryProject(origin, viewProj, viewportMin, viewportSize, out var screenStart) ||
            !TryProject(end, viewProj, viewportMin, viewportSize, out var screenEnd))
            return;

        drawList.AddLine(screenStart, screenEnd, ImGui.GetColorU32(color), AxisThickness);

        // Draw box at end
        drawList.AddRectFilled(
            screenEnd - new Vector2(5, 5),
            screenEnd + new Vector2(5, 5),
            ImGui.GetColorU32(color));
    }

    private void DrawPlaneHandle(ImDrawListPtr drawList, Vector3 origin, Vector3 axis1, Vector3 axis2, float offset, GizmoAxis plane, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        var p1 = origin + axis1 * offset;
        var p2 = origin + axis1 * offset + axis2 * offset;
        var p3 = origin + axis2 * offset;

        if (!TryProject(origin, viewProj, viewportMin, viewportSize, out var s0) ||
            !TryProject(p1, viewProj, viewportMin, viewportSize, out var s1) ||
            !TryProject(p2, viewProj, viewportMin, viewportSize, out var s2) ||
            !TryProject(p3, viewProj, viewportMin, viewportSize, out var s3))
            return;

        var color = (_hoveredAxis == plane || _activeAxis == plane) ? ColorHighlight : ColorPlane;
        color.W = 0.3f;

        drawList.AddQuadFilled(s0, s1, s2, s3, ImGui.GetColorU32(color));
    }

    private void DrawRotationRing(ImDrawListPtr drawList, Vector3 origin, Vector3 axis, float radius, Vector4 color, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        // Calculate perpendicular vectors for the ring plane
        Vector3 perp1, perp2;
        if (Math.Abs(axis.X) < 0.9f)
            perp1 = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitX));
        else
            perp1 = Vector3.Normalize(Vector3.Cross(axis, Vector3.UnitY));
        perp2 = Vector3.Cross(axis, perp1);

        // Draw ring segments
        Vector2? lastScreen = null;
        for (int i = 0; i <= RotationRingSegments; i++)
        {
            float angle = (float)i / RotationRingSegments * MathF.PI * 2;
            var point = origin + (perp1 * MathF.Cos(angle) + perp2 * MathF.Sin(angle)) * radius;

            if (TryProject(point, viewProj, viewportMin, viewportSize, out var screenPoint))
            {
                if (lastScreen.HasValue)
                {
                    drawList.AddLine(lastScreen.Value, screenPoint, ImGui.GetColorU32(color), 2f);
                }
                lastScreen = screenPoint;
            }
            else
            {
                lastScreen = null;
            }
        }
    }

    private Vector4 GetAxisColor(GizmoAxis axis)
    {
        if (_activeAxis == axis || _hoveredAxis == axis)
            return ColorHighlight;

        return axis switch
        {
            GizmoAxis.X => ColorX,
            GizmoAxis.Y => ColorY,
            GizmoAxis.Z => ColorZ,
            _ => new Vector4(0.8f, 0.8f, 0.8f, 1f)
        };
    }

    #region Math Helpers

    private static bool TryProject(Vector3 world, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), viewProj);
        if (clip.W <= 0.0001f)
        {
            screen = default;
            return false;
        }

        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2(
            (ndc.X * 0.5f + 0.5f) * viewportSize.X + viewportMin.X,
            (1f - (ndc.Y * 0.5f + 0.5f)) * viewportSize.Y + viewportMin.Y);
        return ndc.Z >= -1f && ndc.Z <= 1f;
    }

    private static float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
    {
        var line = lineEnd - lineStart;
        float lengthSq = line.LengthSquared();
        
        if (lengthSq < 0.0001f)
            return Vector2.Distance(point, lineStart);

        float t = Math.Clamp(Vector2.Dot(point - lineStart, line) / lengthSq, 0f, 1f);
        var projection = lineStart + line * t;
        return Vector2.Distance(point, projection);
    }

    private static (Vector3 Origin, Vector3 Direction) ScreenToRay(Vector2 screenPos, Camera camera, Matrix4x4 viewProj, Vector2 viewportMin, Vector2 viewportSize)
    {
        // Convert screen to NDC
        float ndcX = ((screenPos.X - viewportMin.X) / viewportSize.X) * 2f - 1f;
        float ndcY = 1f - ((screenPos.Y - viewportMin.Y) / viewportSize.Y) * 2f;

        // Inverse view-projection
        if (!Matrix4x4.Invert(viewProj, out var invViewProj))
            return (camera.Position, camera.Forward);

        var nearPoint = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), invViewProj);
        var farPoint = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), invViewProj);

        nearPoint /= nearPoint.W;
        farPoint /= farPoint.W;

        var direction = Vector3.Normalize(new Vector3(farPoint.X - nearPoint.X, farPoint.Y - nearPoint.Y, farPoint.Z - nearPoint.Z));
        return (new Vector3(nearPoint.X, nearPoint.Y, nearPoint.Z), direction);
    }

    private static bool RayPlaneIntersection(Vector3 rayOrigin, Vector3 rayDir, Vector3 planePoint, Vector3 planeNormal, out Vector3 hitPoint)
    {
        float denom = Vector3.Dot(planeNormal, rayDir);
        if (Math.Abs(denom) < 0.0001f)
        {
            hitPoint = default;
            return false;
        }

        float t = Vector3.Dot(planePoint - rayOrigin, planeNormal) / denom;
        if (t < 0)
        {
            hitPoint = default;
            return false;
        }

        hitPoint = rayOrigin + rayDir * t;
        return true;
    }

    #endregion

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
}
