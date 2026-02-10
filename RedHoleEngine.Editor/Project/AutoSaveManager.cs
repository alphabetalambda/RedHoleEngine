namespace RedHoleEngine.Editor.Project;

/// <summary>
/// Manages automatic saving of scenes and projects
/// </summary>
public class AutoSaveManager
{
    private readonly ProjectManager _projectManager;
    private readonly Func<string> _getCurrentScenePath;
    private readonly Func<bool> _hasUnsavedChanges;
    private readonly Action<string> _saveScene;
    
    private DateTime _lastSaveTime;
    private DateTime _lastChangeTime;
    private bool _hasChanges;
    private bool _enabled = true;

    /// <summary>
    /// Whether auto-save is enabled (can be temporarily disabled)
    /// </summary>
    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    /// <summary>
    /// Time of last auto-save
    /// </summary>
    public DateTime LastSaveTime => _lastSaveTime;

    /// <summary>
    /// Whether there are unsaved changes
    /// </summary>
    public bool HasUnsavedChanges => _hasChanges;

    /// <summary>
    /// Seconds until next auto-save (0 if disabled or no changes)
    /// </summary>
    public int SecondsUntilNextSave
    {
        get
        {
            if (!_enabled || !_hasChanges) return 0;
            
            var interval = GetAutoSaveInterval();
            if (interval <= 0) return 0;
            
            var elapsed = (DateTime.UtcNow - _lastSaveTime).TotalSeconds;
            var remaining = interval - elapsed;
            return remaining > 0 ? (int)remaining : 0;
        }
    }

    /// <summary>
    /// Event raised when auto-save occurs
    /// </summary>
    public event Action<string>? AutoSaved;

    /// <summary>
    /// Event raised when auto-save fails
    /// </summary>
    public event Action<string>? AutoSaveFailed;

    public AutoSaveManager(
        ProjectManager projectManager,
        Func<string> getCurrentScenePath,
        Func<bool> hasUnsavedChanges,
        Action<string> saveScene)
    {
        _projectManager = projectManager;
        _getCurrentScenePath = getCurrentScenePath;
        _hasUnsavedChanges = hasUnsavedChanges;
        _saveScene = saveScene;
        _lastSaveTime = DateTime.UtcNow;
        _lastChangeTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Call when changes are made to the scene
    /// </summary>
    public void MarkDirty()
    {
        _hasChanges = true;
        _lastChangeTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Call when the scene is saved manually
    /// </summary>
    public void MarkSaved()
    {
        _hasChanges = false;
        _lastSaveTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Update auto-save timer - call every frame
    /// </summary>
    public void Update()
    {
        if (!_enabled || !_hasChanges)
            return;

        var interval = GetAutoSaveInterval();
        if (interval <= 0)
            return;

        var elapsed = (DateTime.UtcNow - _lastSaveTime).TotalSeconds;
        if (elapsed >= interval)
        {
            PerformAutoSave();
        }
    }

    private int GetAutoSaveInterval()
    {
        if (!_projectManager.HasProject)
            return 0;

        return _projectManager.CurrentProject?.Editor.AutoSaveInterval ?? 0;
    }

    private void PerformAutoSave()
    {
        var scenePath = _getCurrentScenePath();
        
        try
        {
            if (!string.IsNullOrEmpty(scenePath))
            {
                // Save scene
                _saveScene(scenePath);
                Console.WriteLine($"[AUTO-SAVE] Scene saved: {System.IO.Path.GetFileName(scenePath)}");
                AutoSaved?.Invoke(scenePath);
            }
            else
            {
                // No scene path - save to auto-save location
                var autoSavePath = GetAutoSavePath();
                if (!string.IsNullOrEmpty(autoSavePath))
                {
                    _saveScene(autoSavePath);
                    Console.WriteLine($"[AUTO-SAVE] Scene saved to: {System.IO.Path.GetFileName(autoSavePath)}");
                    AutoSaved?.Invoke(autoSavePath);
                }
            }

            // Also save project
            if (_projectManager.HasProject)
            {
                _projectManager.SaveProject(out _);
            }

            _hasChanges = false;
            _lastSaveTime = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AUTO-SAVE] Failed: {ex.Message}");
            AutoSaveFailed?.Invoke(ex.Message);
            
            // Don't reset _lastSaveTime so it tries again next interval
        }
    }

    private string GetAutoSavePath()
    {
        if (!_projectManager.HasProject)
            return "";

        var scenesPath = _projectManager.GetScenesPath();
        if (string.IsNullOrEmpty(scenesPath))
            return "";

        // Create auto-save directory
        var autoSaveDir = System.IO.Path.Combine(scenesPath, ".autosave");
        if (!System.IO.Directory.Exists(autoSaveDir))
        {
            System.IO.Directory.CreateDirectory(autoSaveDir);
        }

        // Generate filename with timestamp
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        return System.IO.Path.Combine(autoSaveDir, $"autosave_{timestamp}.rhes");
    }

    /// <summary>
    /// Force an immediate auto-save
    /// </summary>
    public void SaveNow()
    {
        if (_hasChanges)
        {
            PerformAutoSave();
        }
    }

    /// <summary>
    /// Get status text for UI display
    /// </summary>
    public string GetStatusText()
    {
        if (!_enabled)
            return "Auto-save disabled";

        var interval = GetAutoSaveInterval();
        if (interval <= 0)
            return "Auto-save off";

        if (!_hasChanges)
            return "No unsaved changes";

        var remaining = SecondsUntilNextSave;
        if (remaining <= 0)
            return "Saving...";

        if (remaining < 60)
            return $"Auto-save in {remaining}s";

        return $"Auto-save in {remaining / 60}m {remaining % 60}s";
    }
}
