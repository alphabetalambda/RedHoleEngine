using ImGuiNET;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Editor.Selection;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Base class for all editor panels
/// </summary>
public abstract class EditorPanel
{
    /// <summary>
    /// Panel title (shown in window title bar)
    /// </summary>
    public abstract string Title { get; }

    /// <summary>
    /// Whether the panel is currently visible
    /// </summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// Default window flags for this panel
    /// </summary>
    protected virtual ImGuiWindowFlags WindowFlags => ImGuiWindowFlags.None;

    /// <summary>
    /// Reference to the ECS world
    /// </summary>
    protected World? World { get; private set; }

    /// <summary>
    /// Reference to the selection manager
    /// </summary>
    protected SelectionManager? Selection { get; private set; }

    /// <summary>
    /// Set references to shared editor state
    /// </summary>
    public void SetContext(World? world, SelectionManager? selection)
    {
        World = world;
        Selection = selection;
    }

    /// <summary>
    /// Draw the panel
    /// </summary>
    public void Draw()
    {
        if (!IsVisible) return;

        bool open = IsVisible;
        
        if (ImGui.Begin(Title, ref open, WindowFlags))
        {
            OnDraw();
        }
        ImGui.End();

        IsVisible = open;
    }

    /// <summary>
    /// Override to implement panel content
    /// </summary>
    protected abstract void OnDraw();
}
