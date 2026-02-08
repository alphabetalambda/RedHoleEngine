using RedHoleEngine.Core.Scene;

namespace RedHoleEngine.Core;

/// <summary>
/// Manages the main game loop with fixed timestep physics and variable rendering.
/// </summary>
public class GameLoop
{
    private Scene.Scene? _activeScene;
    private readonly List<Scene.Scene> _loadedScenes = new();
    
    /// <summary>
    /// Maximum number of fixed updates per frame to prevent spiral of death
    /// </summary>
    public int MaxFixedUpdatesPerFrame { get; set; } = 5;
    
    /// <summary>
    /// Event fired before update
    /// </summary>
    public event Action<float>? OnPreUpdate;
    
    /// <summary>
    /// Event fired after update
    /// </summary>
    public event Action<float>? OnPostUpdate;
    
    /// <summary>
    /// Event fired before rendering
    /// </summary>
    public event Action? OnPreRender;
    
    /// <summary>
    /// Event fired after rendering
    /// </summary>
    public event Action? OnPostRender;

    /// <summary>
    /// The currently active scene
    /// </summary>
    public Scene.Scene? ActiveScene
    {
        get => _activeScene;
        set
        {
            if (_activeScene != value)
            {
                _activeScene = value;
                if (value != null && !_loadedScenes.Contains(value))
                {
                    _loadedScenes.Add(value);
                }
            }
        }
    }

    /// <summary>
    /// All loaded scenes
    /// </summary>
    public IReadOnlyList<Scene.Scene> LoadedScenes => _loadedScenes;

    /// <summary>
    /// Create a new scene and optionally set it as active
    /// </summary>
    public Scene.Scene CreateScene(string name = "Untitled", bool setActive = true)
    {
        var scene = new Scene.Scene(name);
        _loadedScenes.Add(scene);
        
        if (setActive)
            _activeScene = scene;
        
        return scene;
    }

    /// <summary>
    /// Unload a scene
    /// </summary>
    public void UnloadScene(Scene.Scene scene)
    {
        if (_activeScene == scene)
            _activeScene = null;
        
        _loadedScenes.Remove(scene);
        scene.Dispose();
    }

    /// <summary>
    /// Called each frame to update game logic
    /// </summary>
    public void Update(float deltaTime)
    {
        Time.Update(deltaTime);
        
        OnPreUpdate?.Invoke(deltaTime);
        
        // Run fixed updates (physics)
        int fixedUpdateCount = 0;
        while (Time.ShouldRunFixedUpdate() && fixedUpdateCount < MaxFixedUpdatesPerFrame)
        {
            _activeScene?.FixedUpdate(Time.FixedDeltaTime);
            fixedUpdateCount++;
        }
        
        // Run regular updates
        _activeScene?.Update(Time.DeltaTime);
        
        OnPostUpdate?.Invoke(deltaTime);
    }

    /// <summary>
    /// Called each frame to render
    /// </summary>
    public void Render()
    {
        OnPreRender?.Invoke();
        
        // Rendering is handled externally for now (VulkanBackend)
        // This is where we'd call renderer.Render(scene)
        
        OnPostRender?.Invoke();
    }

    /// <summary>
    /// Shutdown and cleanup
    /// </summary>
    public void Shutdown()
    {
        foreach (var scene in _loadedScenes.ToList())
        {
            scene.Dispose();
        }
        _loadedScenes.Clear();
        _activeScene = null;
    }
}
