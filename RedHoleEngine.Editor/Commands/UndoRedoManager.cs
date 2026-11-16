namespace RedHoleEngine.Editor.Commands;

/// <summary>
/// Manages undo/redo command history for the editor
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<Entry> _undoStack = new();
    private readonly Stack<Entry> _redoStack = new();
    private readonly int _maxHistorySize;
    private bool _isExecutingCommand;

    // Versioning: every distinct document state reachable through undo/redo gets a unique id.
    // StateVersion is the id of the current state, so callers can remember a "save point"
    // and later tell whether the document has moved away from it (or been undone back to it).
    private long _nextVersion;
    private long _baseVersion;

    private readonly record struct Entry(ICommand Command, long Version);
    
    /// <summary>
    /// Event fired when the undo/redo state changes
    /// </summary>
    public event Action? StateChanged;

    /// <summary>
    /// Maximum number of commands to keep in history
    /// </summary>
    public int MaxHistorySize => _maxHistorySize;
    
    /// <summary>
    /// Whether there are commands that can be undone
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;
    
    /// <summary>
    /// Whether there are commands that can be redone
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;
    
    /// <summary>
    /// Number of commands in the undo stack
    /// </summary>
    public int UndoCount => _undoStack.Count;
    
    /// <summary>
    /// Number of commands in the redo stack
    /// </summary>
    public int RedoCount => _redoStack.Count;
    
    /// <summary>
    /// Description of the next command to undo (or null if none)
    /// </summary>
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Command.Description : null;
    
    /// <summary>
    /// Description of the next command to redo (or null if none)
    /// </summary>
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Command.Description : null;

    /// <summary>
    /// Identifier of the current document state. It changes whenever a command is executed,
    /// merged, undone, redone or the history is cleared, and returns to a previous value when
    /// undo/redo returns to that exact state. Compare against a stored value to detect unsaved changes.
    /// </summary>
    public long StateVersion => _undoStack.Count > 0 ? _undoStack.Peek().Version : _baseVersion;

    public UndoRedoManager(int maxHistorySize = 100)
    {
        _maxHistorySize = maxHistorySize;
    }

    /// <summary>
    /// Execute a command and add it to the undo history
    /// </summary>
    public void ExecuteCommand(ICommand command)
    {
        if (_isExecutingCommand)
        {
            // Prevent recursive command execution
            return;
        }

        _isExecutingCommand = true;
        try
        {
            command.Execute();
            
            // Try to merge with the last command
            if (_undoStack.Count > 0 && _undoStack.Peek().Command.CanMergeWith(command))
            {
                // A merge changes the resulting state, so it gets a fresh version
                var top = _undoStack.Pop();
                top.Command.MergeWith(command);
                _undoStack.Push(top with { Version = ++_nextVersion });
            }
            else
            {
                _undoStack.Push(new Entry(command, ++_nextVersion));
                
                // Trim history if it exceeds max size
                TrimHistory();
            }
            
            // Clear redo stack when a new command is executed
            _redoStack.Clear();
            
            StateChanged?.Invoke();
        }
        finally
        {
            _isExecutingCommand = false;
        }
    }

    /// <summary>
    /// Undo the last command
    /// </summary>
    public void Undo()
    {
        if (!CanUndo || _isExecutingCommand)
            return;

        _isExecutingCommand = true;
        try
        {
            var entry = _undoStack.Pop();
            entry.Command.Undo();
            _redoStack.Push(entry);
            
            StateChanged?.Invoke();
        }
        finally
        {
            _isExecutingCommand = false;
        }
    }

    /// <summary>
    /// Redo the last undone command
    /// </summary>
    public void Redo()
    {
        if (!CanRedo || _isExecutingCommand)
            return;

        _isExecutingCommand = true;
        try
        {
            var entry = _redoStack.Pop();
            entry.Command.Execute();
            _undoStack.Push(entry);
            
            StateChanged?.Invoke();
        }
        finally
        {
            _isExecutingCommand = false;
        }
    }

    /// <summary>
    /// Clear all undo/redo history
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        _baseVersion = ++_nextVersion;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Get descriptions of recent undo commands (most recent first)
    /// </summary>
    public IEnumerable<string> GetUndoHistory(int maxCount = 10)
    {
        return _undoStack.Take(maxCount).Select(e => e.Command.Description);
    }

    /// <summary>
    /// Get descriptions of recent redo commands (most recent first)
    /// </summary>
    public IEnumerable<string> GetRedoHistory(int maxCount = 10)
    {
        return _redoStack.Take(maxCount).Select(e => e.Command.Description);
    }

    private void TrimHistory()
    {
        if (_undoStack.Count <= _maxHistorySize)
            return;

        // Stack enumerates newest first; keep the newest _maxHistorySize entries
        var entries = _undoStack.ToList();
        var dropped = entries.Skip(_maxHistorySize).ToList();

        // The oldest reachable state is now the one after the newest dropped command
        _baseVersion = dropped[0].Version;

        _undoStack.Clear();
        for (int i = _maxHistorySize - 1; i >= 0; i--)
        {
            _undoStack.Push(entries[i]);
        }
    }
}
