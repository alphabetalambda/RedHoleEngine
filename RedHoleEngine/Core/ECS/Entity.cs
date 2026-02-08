namespace RedHoleEngine.Core.ECS;

/// <summary>
/// Represents a unique entity in the game world.
/// An entity is just an ID - all data lives in components.
/// </summary>
public readonly struct Entity : IEquatable<Entity>
{
    /// <summary>
    /// Unique identifier for this entity
    /// </summary>
    public readonly int Id;
    
    /// <summary>
    /// Generation counter to detect stale entity references
    /// </summary>
    public readonly int Generation;

    public Entity(int id, int generation)
    {
        Id = id;
        Generation = generation;
    }

    public static Entity Null => new(0, 0);
    
    public bool IsNull => Id == 0 && Generation == 0;

    public bool Equals(Entity other) => Id == other.Id && Generation == other.Generation;
    public override bool Equals(object? obj) => obj is Entity other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, Generation);
    
    public static bool operator ==(Entity left, Entity right) => left.Equals(right);
    public static bool operator !=(Entity left, Entity right) => !left.Equals(right);
    
    public override string ToString() => $"Entity({Id}:{Generation})";
}
