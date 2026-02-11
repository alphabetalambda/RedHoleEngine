using System.Numerics;
using RedHoleEngine.Assets.Branding;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Rendering.UI;

namespace RedHoleEngine.Core;

/// <summary>
/// Manages the engine splash screen displayed on startup for compiled games.
/// The splash screen shows the RedHole Engine logo and fades out after a configurable duration.
/// </summary>
public sealed class SplashScreen
{
    private readonly World _world;
    private readonly int _viewportWidth;
    private readonly int _viewportHeight;
    
    private Entity _backgroundEntity;
    private Entity _logoEntity;
    private Entity _fadeEntity;
    
    private float _elapsedTime;
    private SplashState _state = SplashState.FadeIn;
    
    /// <summary>
    /// Path to the splash logo image (PNG format recommended)
    /// </summary>
    public string LogoPath { get; set; } = "Assets/Branding/redhole_logo.png";
    
    /// <summary>
    /// Duration to display the splash screen (in seconds)
    /// </summary>
    public float DisplayDuration { get; set; } = 2.5f;
    
    /// <summary>
    /// Duration of fade in effect (in seconds)
    /// </summary>
    public float FadeInDuration { get; set; } = 0.5f;
    
    /// <summary>
    /// Duration of fade out effect (in seconds)
    /// </summary>
    public float FadeOutDuration { get; set; } = 0.5f;
    
    /// <summary>
    /// Background color of the splash screen
    /// </summary>
    public Vector4 BackgroundColor { get; set; } = new(0.05f, 0.05f, 0.05f, 1.0f); // Dark gray
    
    /// <summary>
    /// Size of the logo as a fraction of the viewport height (0.0 - 1.0)
    /// </summary>
    public float LogoScale { get; set; } = 0.4f;
    
    /// <summary>
    /// Whether the splash screen has completed
    /// </summary>
    public bool IsComplete => _state == SplashState.Complete;
    
    /// <summary>
    /// Whether the splash screen is currently active
    /// </summary>
    public bool IsActive => _state != SplashState.Complete && _state != SplashState.NotStarted;

    private enum SplashState
    {
        NotStarted,
        FadeIn,
        Display,
        FadeOut,
        Complete
    }

    public SplashScreen(World world, int viewportWidth, int viewportHeight)
    {
        _world = world;
        _viewportWidth = viewportWidth;
        _viewportHeight = viewportHeight;
    }

    /// <summary>
    /// Initialize and show the splash screen
    /// </summary>
    public void Show()
    {
        if (_state != SplashState.NotStarted)
            return;
            
        _elapsedTime = 0f;
        _state = SplashState.FadeIn;
        
        // Create background (full screen dark rectangle)
        _backgroundEntity = _world.CreateEntity();
        _world.AddComponent(_backgroundEntity, new UiRectComponent(
            position: Vector2.Zero,
            size: new Vector2(_viewportWidth, _viewportHeight),
            color: BackgroundColor,
            layer: 1000 // High layer to be on top of everything
        ));
        
        // Calculate logo size and position (centered)
        float logoHeight = _viewportHeight * LogoScale;
        float logoWidth = logoHeight; // Assuming square logo, will be adjusted by aspect ratio
        float logoX = (_viewportWidth - logoWidth) / 2f;
        float logoY = (_viewportHeight - logoHeight) / 2f;
        
        // Create logo image
        _logoEntity = _world.CreateEntity();
        _world.AddComponent(_logoEntity, new UiImageComponent(
            position: new Vector2(logoX, logoY),
            size: new Vector2(logoWidth, logoHeight),
            sourcePath: LogoPath,
            tint: new Vector4(1f, 1f, 1f, 0f), // Start transparent for fade in
            layer: 1001
        ));
        
        // Create fade overlay (for fade out effect)
        _fadeEntity = _world.CreateEntity();
        _world.AddComponent(_fadeEntity, new UiRectComponent(
            position: Vector2.Zero,
            size: new Vector2(_viewportWidth, _viewportHeight),
            color: new Vector4(0f, 0f, 0f, 0f), // Start transparent
            layer: 1002
        ));
    }

    /// <summary>
    /// Update the splash screen animation
    /// </summary>
    /// <param name="deltaTime">Time since last frame in seconds</param>
    public void Update(float deltaTime)
    {
        if (_state == SplashState.NotStarted || _state == SplashState.Complete)
            return;
            
        _elapsedTime += deltaTime;
        
        switch (_state)
        {
            case SplashState.FadeIn:
                UpdateFadeIn();
                break;
            case SplashState.Display:
                UpdateDisplay();
                break;
            case SplashState.FadeOut:
                UpdateFadeOut();
                break;
        }
    }

    private void UpdateFadeIn()
    {
        float progress = Math.Min(_elapsedTime / FadeInDuration, 1f);
        
        // Fade in the logo
        ref var logoImage = ref _world.GetComponent<UiImageComponent>(_logoEntity);
        logoImage.Tint = new Vector4(1f, 1f, 1f, progress);
        
        if (progress >= 1f)
        {
            _elapsedTime = 0f;
            _state = SplashState.Display;
        }
    }

    private void UpdateDisplay()
    {
        if (_elapsedTime >= DisplayDuration)
        {
            _elapsedTime = 0f;
            _state = SplashState.FadeOut;
        }
    }

    private void UpdateFadeOut()
    {
        float progress = Math.Min(_elapsedTime / FadeOutDuration, 1f);
        
        // Fade out everything by fading in a black overlay
        ref var fadeRect = ref _world.GetComponent<UiRectComponent>(_fadeEntity);
        fadeRect.Color = new Vector4(0f, 0f, 0f, progress);
        
        if (progress >= 1f)
        {
            // Clean up entities
            Cleanup();
            _state = SplashState.Complete;
        }
    }

    /// <summary>
    /// Skip the splash screen immediately
    /// </summary>
    public void Skip()
    {
        if (_state == SplashState.Complete || _state == SplashState.NotStarted)
            return;
            
        Cleanup();
        _state = SplashState.Complete;
    }

    private void Cleanup()
    {
        if (_world.IsAlive(_backgroundEntity))
            _world.DestroyEntity(_backgroundEntity);
        if (_world.IsAlive(_logoEntity))
            _world.DestroyEntity(_logoEntity);
        if (_world.IsAlive(_fadeEntity))
            _world.DestroyEntity(_fadeEntity);
    }
}
