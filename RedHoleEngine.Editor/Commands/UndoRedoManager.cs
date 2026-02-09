namespace RedHoleEngine.Editor.Commands;

/// <summary>
/// Manages undo/redo command history for the editor
/// </summary>
public class UndoRedoManager
{
    private readonly Stack<ICommand> _undoStack = new();
    private readonly Stack<ICommand> _redoStack = new();
    private readonly int _maxHistorySize;
    private bool _isExecutingCommand;
    
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
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;
    
    /// <summary>
    /// Description of the next command to redo (or null if none)
    /// </summary>
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

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
            if (_undoStack.Count > 0 && _undoStack.Peek().CanMergeWith(command))
            {
                _undoStack.Peek().MergeWith(command);
            }
            else
            {
                _undoStack.Push(command);
                
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
            var command = _undoStack.Pop();
            command.Undo();
            _redoStack.Push(command);
            
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
            var command = _redoStack.Pop();
            command.Execute();
            _undoStack.Push(command);
            
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
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Get descriptions of recent undo commands (most recent first)
    /// </summary>
    public IEnumerable<string> GetUndoHistory(int maxCount = 10)
    {
        return _undoStack.Take(maxCount).Select(c => c.Description);
    }

    /// <summary>
    /// Get descriptions of recent redo commands (most recent first)
    /// </summary>
    public IEnumerable<string> GetRedoHistory(int maxCount = 10)
    {
        return _redoStack.Take(maxCount).Select(c => c.Description);
    }

    private void TrimHistory()
    {
        // Convert to list, trim, and rebuild stack (keeps oldest commands)
        if (_undoStack.Count > _maxHistorySize)
        {
            var commands = _undoStack.ToList();
            commands.Reverse(); // Oldest first
            commands = commands.Skip(commands.Count - _maxHistorySize).ToList();
            commands.Reverse(); // Newest first again
            
            _undoStack.Clear();
            foreach (var cmd in commands.AsEnumerable().Reverse())
            {
                _undoStack.Push(cmd);
            }
        }
    }
}
