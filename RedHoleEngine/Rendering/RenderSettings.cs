namespace RedHoleEngine.Rendering;

public enum RenderMode
{
    Raytraced,
    Rasterized
}

/// <summary>
/// Runtime render settings that control raytracing vs rasterization.
/// </summary>
public class RenderSettings
{
    public RenderMode Mode { get; set; } = RenderMode.Raytraced;
}
