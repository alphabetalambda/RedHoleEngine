using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Panel showing all entities in the scene hierarchy
/// </summary>
public class SceneHierarchyPanel : EditorPanel
{
    public override string Title => "Hierarchy";

    private string _searchFilter = "";
    private Entity _entityToDelete = Entity.Null;

    protected override void OnDraw()
    {
        if (World == null)
        {
            ImGui.TextColored(new Vector4(1, 1, 0, 1), "No active scene");
            return;
        }

        // Toolbar
        DrawToolbar();

        ImGui.Separator();

        // Entity list
        DrawEntityList();

        // Handle deferred deletion
        if (!_entityToDelete.IsNull)
        {
            World.DestroyEntity(_entityToDelete);
            Selection?.RemoveFromSelection(_entityToDelete);
            _entityToDelete = Entity.Null;
        }
    }

    private void DrawToolbar()
    {
        // Add entity button
        if (ImGui.Button("+ Add Entity"))
        {
            ImGui.OpenPopup("AddEntityPopup");
        }

        if (ImGui.BeginPopup("AddEntityPopup"))
        {
            if (ImGui.MenuItem("Empty Entity"))
            {
                var entity = World!.CreateEntity();
                World.AddComponent(entity, new TransformComponent());
                Selection?.Select(entity);
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Camera"))
            {
                var entity = World!.CreateEntity();
                World.AddComponent(entity, new TransformComponent());
                World.AddComponent(entity, CameraComponent.CreatePerspective(60f, 16f / 9f));
                Selection?.Select(entity);
            }

            if (ImGui.MenuItem("Black Hole"))
            {
                var entity = World!.CreateEntity();
                World.AddComponent(entity, new TransformComponent());
                World.AddComponent(entity, GravitySourceComponent.CreateBlackHole(1e6f));
                Selection?.Select(entity);
            }

            ImGui.Separator();

            if (ImGui.BeginMenu("Physics"))
            {
                if (ImGui.MenuItem("Sphere"))
                {
                    var entity = World!.CreateEntity();
                    World.AddComponent(entity, new TransformComponent());
                    World.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));
                    World.AddComponent(entity, ColliderComponent.CreateSphere(0.5f));
                    Selection?.Select(entity);
                }

                if (ImGui.MenuItem("Box"))
                {
                    var entity = World!.CreateEntity();
                    World.AddComponent(entity, new TransformComponent());
                    World.AddComponent(entity, RigidBodyComponent.CreateDynamic(1f));
                    World.AddComponent(entity, ColliderComponent.CreateBox(new Vector3(0.5f)));
                    Selection?.Select(entity);
                }

                if (ImGui.MenuItem("Ground Plane"))
                {
                    var entity = World!.CreateEntity();
                    World.AddComponent(entity, new TransformComponent());
                    World.AddComponent(entity, RigidBodyComponent.CreateStatic());
                    World.AddComponent(entity, ColliderComponent.CreateGroundPlane());
                    Selection?.Select(entity);
                }

                ImGui.EndMenu();
            }

            ImGui.EndPopup();
        }

        ImGui.SameLine();

        // Search filter
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        ImGui.InputTextWithHint("##Search", "Search...", ref _searchFilter, 256);
    }

    private void DrawEntityList()
    {
        if (World == null) return;

        // Get all entities with TransformComponent (most entities will have this)
        var entities = new List<Entity>();
        foreach (var entity in World.Query<TransformComponent>())
        {
            entities.Add(entity);
        }

        // Sort by ID for consistent ordering
        entities.Sort((a, b) => a.Id.CompareTo(b.Id));

        foreach (var entity in entities)
        {
            if (!MatchesFilter(entity))
                continue;

            DrawEntityNode(entity);
        }

        // Right-click on empty space
        if (ImGui.BeginPopupContextWindow("HierarchyContext", ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems))
        {
            if (ImGui.MenuItem("Create Empty Entity"))
            {
                var entity = World.CreateEntity();
                World.AddComponent(entity, new TransformComponent());
                Selection?.Select(entity);
            }
            ImGui.EndPopup();
        }
    }

    private void DrawEntityNode(Entity entity)
    {
        if (World == null || Selection == null) return;

        bool isSelected = Selection.IsSelected(entity);
        string label = GetEntityLabel(entity);

        ImGuiTreeNodeFlags flags = ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (isSelected)
            flags |= ImGuiTreeNodeFlags.Selected;

        // Different icon/color based on components
        var color = GetEntityColor(entity);
        ImGui.PushStyleColor(ImGuiCol.Text, color);

        bool nodeOpen = ImGui.TreeNodeEx($"##{entity.Id}", flags, label);

        ImGui.PopStyleColor();

        // Handle selection
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            if (ImGui.GetIO().KeyCtrl)
                Selection.ToggleSelection(entity);
            else
                Selection.Select(entity);
        }

        // Context menu
        if (ImGui.BeginPopupContextItem())
        {
            if (ImGui.MenuItem("Delete"))
            {
                _entityToDelete = entity;
            }

            if (ImGui.MenuItem("Duplicate"))
            {
                DuplicateEntity(entity);
            }

            ImGui.Separator();

            if (ImGui.MenuItem("Focus"))
            {
                // TODO: Focus camera on entity
            }

            ImGui.EndPopup();
        }

        // Drag source for reparenting
        if (ImGui.BeginDragDropSource())
        {
            unsafe
            {
                int id = entity.Id;
                ImGui.SetDragDropPayload("ENTITY", (IntPtr)(&id), sizeof(int));
            }
            ImGui.Text(label);
            ImGui.EndDragDropSource();
        }

        if (nodeOpen)
            ImGui.TreePop();
    }

    private string GetEntityLabel(Entity entity)
    {
        if (World == null) return $"Entity {entity.Id}";

        // Try to build a descriptive name from components
        var parts = new List<string>();

        if (World.HasComponent<CameraComponent>(entity))
            parts.Add("Camera");
        if (World.HasComponent<GravitySourceComponent>(entity))
            parts.Add("BlackHole");
        if (World.HasComponent<RigidBodyComponent>(entity))
        {
            if (World.HasComponent<ColliderComponent>(entity))
            {
                ref var col = ref World.GetComponent<ColliderComponent>(entity);
                parts.Add(col.ShapeType.ToString());
            }
            else
            {
                parts.Add("RigidBody");
            }
        }
        if (World.HasComponent<Audio.AudioSourceComponent>(entity))
            parts.Add("AudioSource");

        if (parts.Count > 0)
            return $"{string.Join(", ", parts)} ({entity.Id})";

        return $"Entity {entity.Id}";
    }

    private Vector4 GetEntityColor(Entity entity)
    {
        if (World == null) return new Vector4(1, 1, 1, 1);

        // Color based on primary component type
        if (World.HasComponent<CameraComponent>(entity))
            return new Vector4(0.5f, 0.8f, 1f, 1f); // Light blue

        if (World.HasComponent<GravitySourceComponent>(entity))
            return new Vector4(1f, 0.4f, 0.4f, 1f); // Red

        if (World.HasComponent<Audio.AudioSourceComponent>(entity))
            return new Vector4(0.4f, 1f, 0.6f, 1f); // Green

        if (World.HasComponent<RigidBodyComponent>(entity))
            return new Vector4(1f, 0.8f, 0.4f, 1f); // Orange

        return new Vector4(0.8f, 0.8f, 0.8f, 1f); // Gray for generic
    }

    private bool MatchesFilter(Entity entity)
    {
        if (string.IsNullOrEmpty(_searchFilter))
            return true;

        string label = GetEntityLabel(entity);
        return label.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
    }

    private void DuplicateEntity(Entity source)
    {
        if (World == null) return;

        var newEntity = World.CreateEntity();

        // Copy TransformComponent
        if (World.HasComponent<TransformComponent>(source))
        {
            ref var srcTransform = ref World.GetComponent<TransformComponent>(source);
            World.AddComponent(newEntity, new TransformComponent(
                srcTransform.Position + new Vector3(1, 0, 0), // Offset slightly
                srcTransform.Rotation,
                srcTransform.LocalScale
            ));
        }

        // Copy RigidBodyComponent
        if (World.HasComponent<RigidBodyComponent>(source))
        {
            ref var src = ref World.GetComponent<RigidBodyComponent>(source);
            World.AddComponent(newEntity, new RigidBodyComponent
            {
                Type = src.Type,
                Mass = src.Mass,
                Restitution = src.Restitution,
                Friction = src.Friction,
                LinearDamping = src.LinearDamping,
                AngularDamping = src.AngularDamping,
                UseGravity = src.UseGravity
            });
        }

        // Copy ColliderComponent
        if (World.HasComponent<ColliderComponent>(source))
        {
            ref var src = ref World.GetComponent<ColliderComponent>(source);
            World.AddComponent(newEntity, src);
        }

        // Copy CameraComponent
        if (World.HasComponent<CameraComponent>(source))
        {
            ref var src = ref World.GetComponent<CameraComponent>(source);
            World.AddComponent(newEntity, src);
        }

        // Copy GravitySourceComponent
        if (World.HasComponent<GravitySourceComponent>(source))
        {
            ref var src = ref World.GetComponent<GravitySourceComponent>(source);
            World.AddComponent(newEntity, src);
        }

        Selection?.Select(newEntity);
    }
}
