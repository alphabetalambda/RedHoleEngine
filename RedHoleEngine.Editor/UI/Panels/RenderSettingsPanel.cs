using ImGuiNET;
using RedHoleEngine.Rendering;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Panel for render mode selection
/// </summary>
public class RenderSettingsPanel : EditorPanel
{
    private readonly RenderSettings _settings;

    public RenderSettingsPanel(RenderSettings settings)
    {
        _settings = settings;
    }

    public override string Title => "Render";

    protected override void OnDraw()
    {
        if (_settings == null)
        {
            ImGui.TextDisabled("No render settings available");
            return;
        }

        var modes = new[] { "Raytraced", "Rasterized" };
        int current = _settings.Mode == RenderMode.Rasterized ? 1 : 0;
        if (ImGui.Combo("Mode", ref current, modes, modes.Length))
        {
            _settings.Mode = current == 1 ? RenderMode.Rasterized : RenderMode.Raytraced;
        }

        ImGui.Separator();
        ImGui.TextDisabled("Rasterized mode is for fast iteration and debugging.");
    }
}
