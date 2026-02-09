using System.Numerics;
using System.Text;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Profiling;

/// <summary>
/// Component for displaying profiler statistics as an overlay.
/// </summary>
public struct ProfilerOverlayComponent : IComponent
{
    /// <summary>
    /// Position of the overlay (top-left corner)
    /// </summary>
    public Vector2 Position;
    
    /// <summary>
    /// Text color
    /// </summary>
    public Vector4 TextColor;
    
    /// <summary>
    /// Background color (with alpha for transparency)
    /// </summary>
    public Vector4 BackgroundColor;
    
    /// <summary>
    /// Text scale
    /// </summary>
    public float Scale;
    
    /// <summary>
    /// UI layer for rendering order
    /// </summary>
    public int Layer;
    
    /// <summary>
    /// Show detailed timer breakdown
    /// </summary>
    public bool ShowTimers;
    
    /// <summary>
    /// Show counter values
    /// </summary>
    public bool ShowCounters;
    
    /// <summary>
    /// Show frame time graph
    /// </summary>
    public bool ShowGraph;
    
    /// <summary>
    /// Width of the frame time graph
    /// </summary>
    public float GraphWidth;
    
    /// <summary>
    /// Height of the frame time graph
    /// </summary>
    public float GraphHeight;

    // Internal state
    internal Entity TextEntity;
    internal Entity BackgroundEntity;
    internal bool Initialized;

    public static ProfilerOverlayComponent Create(
        Vector2? position = null,
        Vector4? textColor = null,
        Vector4? backgroundColor = null,
        float scale = 0.8f,
        int layer = 100,
        bool showTimers = true,
        bool showCounters = false,
        bool showGraph = false)
    {
        return new ProfilerOverlayComponent
        {
            Position = position ?? new Vector2(10, 10),
            TextColor = textColor ?? new Vector4(1f, 1f, 1f, 1f),
            BackgroundColor = backgroundColor ?? new Vector4(0f, 0f, 0f, 0.7f),
            Scale = scale,
            Layer = layer,
            ShowTimers = showTimers,
            ShowCounters = showCounters,
            ShowGraph = showGraph,
            GraphWidth = 200,
            GraphHeight = 60,
            Initialized = false
        };
    }
}

/// <summary>
/// System that updates the profiler overlay each frame.
/// </summary>
public class ProfilerOverlaySystem : GameSystem
{
    public override int Priority => 1000; // Run late

    public override void Update(float deltaTime)
    {
        if (World == null) return;

        foreach (var entity in World.Query<ProfilerOverlayComponent>())
        {
            ref var overlay = ref World.GetComponent<ProfilerOverlayComponent>(entity);
            
            // Initialize UI entities if needed
            if (!overlay.Initialized)
            {
                InitializeOverlay(ref overlay);
            }
            
            // Update the text content
            UpdateOverlayText(ref overlay);
        }
    }

    private void InitializeOverlay(ref ProfilerOverlayComponent overlay)
    {
        if (World == null) return;

        // Create background rect
        overlay.BackgroundEntity = World.CreateEntity();
        World.AddComponent(overlay.BackgroundEntity, new UiRectComponent(
            overlay.Position - new Vector2(5, 5),
            new Vector2(300, 150), // Will be resized based on content
            overlay.BackgroundColor,
            overlay.Layer - 1
        ));

        // Create text entity
        overlay.TextEntity = World.CreateEntity();
        World.AddComponent(overlay.TextEntity, new UiTextComponent(
            overlay.Position,
            "",
            overlay.TextColor,
            overlay.Scale,
            overlay.Layer
        ));

        overlay.Initialized = true;
    }

    private void UpdateOverlayText(ref ProfilerOverlayComponent overlay)
    {
        if (World == null || !overlay.Initialized) return;
        if (!World.HasComponent<UiTextComponent>(overlay.TextEntity)) return;

        var profiler = Profiler.Instance;
        var sb = new StringBuilder();

        // Basic frame stats
        sb.AppendLine($"FPS: {profiler.CurrentFps:F0} (avg: {profiler.AverageFps:F0}, 1%: {profiler.OnePercentLowFps:F0})");
        sb.AppendLine($"Frame: {profiler.LastFrameTimeMs:F2}ms (avg: {profiler.AverageFrameTimeMs:F2}ms)");

        // Timers breakdown
        if (overlay.ShowTimers && profiler.Timers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Timers ---");
            
            var timersByCategory = profiler.Timers.Values
                .Where(t => t.SampleCount > 0)
                .GroupBy(t => t.Category)
                .OrderBy(g => g.Key);

            foreach (var category in timersByCategory)
            {
                foreach (var timer in category.OrderByDescending(t => t.AverageElapsedMs).Take(5))
                {
                    sb.AppendLine($"{timer.Name}: {timer.LastElapsedMs:F2}ms");
                }
            }
        }

        // Counters
        if (overlay.ShowCounters && profiler.Counters.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("--- Counters ---");
            
            foreach (var counter in profiler.Counters.Values.OrderBy(c => c.Name))
            {
                sb.AppendLine($"{counter.Name}: {counter.Value:N0}");
            }
        }

        // Update the text component
        ref var textComp = ref World.GetComponent<UiTextComponent>(overlay.TextEntity);
        textComp.Text = sb.ToString();

        // Update background size based on line count
        if (World.HasComponent<UiRectComponent>(overlay.BackgroundEntity))
        {
            ref var bgComp = ref World.GetComponent<UiRectComponent>(overlay.BackgroundEntity);
            int lineCount = sb.ToString().Split('\n').Length;
            bgComp.Size = new Vector2(280, lineCount * 14 * overlay.Scale + 10);
        }
    }

    public override void OnDestroy()
    {
        // Cleanup overlay entities
        if (World == null) return;

        foreach (var entity in World.Query<ProfilerOverlayComponent>())
        {
            ref var overlay = ref World.GetComponent<ProfilerOverlayComponent>(entity);
            
            if (overlay.Initialized)
            {
                if (World.IsAlive(overlay.TextEntity))
                    World.DestroyEntity(overlay.TextEntity);
                if (World.IsAlive(overlay.BackgroundEntity))
                    World.DestroyEntity(overlay.BackgroundEntity);
            }
        }
        
        base.OnDestroy();
    }
}
