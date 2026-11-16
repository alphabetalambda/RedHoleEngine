using RedHoleEngine.Editor.Commands;

namespace RedHoleEngine.Editor.Project;

/// <summary>
/// Tracks whether the open scene has unsaved changes.
/// The scene is dirty when the undo/redo history has moved away from the state that was last
/// saved or loaded, or when an edit that bypasses the undo system was reported via <see cref="MarkDirty"/>.
/// Undoing back to the save point makes the scene clean again (unless an untracked edit happened).
/// </summary>
public sealed class SceneDirtyTracker
{
    private readonly UndoRedoManager _undoRedo;
    private long _savedVersion;
    private bool _untrackedChanges;
    private bool _lastReportedDirty;

    /// <summary>
    /// Raised when <see cref="IsDirty"/> changes. The argument is the new value.
    /// </summary>
    public event Action<bool>? DirtyChanged;

    public SceneDirtyTracker(UndoRedoManager undoRedo)
    {
        _undoRedo = undoRedo;
        _savedVersion = undoRedo.StateVersion;
        _undoRedo.StateChanged += RaiseIfChanged;
    }

    /// <summary>
    /// Whether the scene differs from the last saved/loaded state
    /// </summary>
    public bool IsDirty => _untrackedChanges || _undoRedo.StateVersion != _savedVersion;

    /// <summary>
    /// Report a scene edit that was not made through the undo/redo system
    /// </summary>
    public void MarkDirty()
    {
        _untrackedChanges = true;
        RaiseIfChanged();
    }

    /// <summary>
    /// Record the current state as saved (call after saving, loading or creating a scene)
    /// </summary>
    public void MarkClean()
    {
        _savedVersion = _undoRedo.StateVersion;
        _untrackedChanges = false;
        RaiseIfChanged();
    }

    private void RaiseIfChanged()
    {
        var dirty = IsDirty;
        if (dirty == _lastReportedDirty)
            return;

        _lastReportedDirty = dirty;
        DirtyChanged?.Invoke(dirty);
    }
}
