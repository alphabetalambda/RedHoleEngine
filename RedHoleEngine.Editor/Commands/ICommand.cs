namespace RedHoleEngine.Editor.Commands;

/// <summary>
/// Interface for undoable editor commands
/// </summary>
public interface ICommand
{
    /// <summary>
    /// Human-readable description of the command for UI display
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// Execute the command
    /// </summary>
    void Execute();
    
    /// <summary>
    /// Undo the command, restoring previous state
    /// </summary>
    void Undo();
    
    /// <summary>
    /// Whether this command can be merged with another command of the same type
    /// (useful for continuous value changes like dragging a slider)
    /// </summary>
    bool CanMergeWith(ICommand other) => false;
    
    /// <summary>
    /// Merge another command into this one (called when CanMergeWith returns true)
    /// </summary>
    void MergeWith(ICommand other) { }
}
