using System.Numerics;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Selection;
using RedHoleEngine.Resources;
using RedHoleEngine.Serialization;

namespace RedHoleEngine.Editor.Commands;

/// <summary>
/// Command to instantiate a prefab (root and children) into the scene
/// </summary>
public class InstantiatePrefabCommand : ICommand
{
    private readonly World _world;
    private readonly SelectionManager? _selection;
    private readonly Prefab _prefab;
    private readonly Vector3? _position;
    private readonly Quaternion? _rotation;
    private readonly List<Entity> _createdEntities = new();

    public string Description => $"Instantiate {_prefab.Name}";

    /// <summary>
    /// Root entity of the instance (Entity.Null before Execute / after Undo)
    /// </summary>
    public Entity Root => _createdEntities.Count > 0 ? _createdEntities[0] : Entity.Null;

    public InstantiatePrefabCommand(World world, SelectionManager? selection, Prefab prefab,
        Vector3? position = null, Quaternion? rotation = null)
    {
        _world = world;
        _selection = selection;
        _prefab = prefab;
        _position = position;
        _rotation = rotation;
    }

    public void Execute()
    {
        _createdEntities.Clear();
        _createdEntities.AddRange(PrefabUtility.InstantiateHierarchy(_world, _prefab, _position, _rotation));

        // Select the root of the new instance
        _selection?.Select(_createdEntities[0]);
    }

    public void Undo()
    {
        foreach (var entity in _createdEntities)
        {
            if (_world.IsAlive(entity))
            {
                _selection?.RemoveFromSelection(entity);
                _world.DestroyEntity(entity);
            }
        }
        _createdEntities.Clear();
    }
}
