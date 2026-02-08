namespace RedHoleEngine.Core.ECS;

/// <summary>
/// Marker interface for all components.
/// Components are pure data containers - no logic.
/// </summary>
public interface IComponent
{
}

/// <summary>
/// Interface for components that need cleanup when removed
/// </summary>
public interface IDisposableComponent : IComponent, IDisposable
{
}
