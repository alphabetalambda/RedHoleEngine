using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;

namespace RedHoleEngine.Editor.UI;

/// <summary>
/// Manages ImGui initialization, input handling, and rendering
/// </summary>
public class ImGuiController : IDisposable
{
    private readonly GL _gl;
    private readonly IWindow _window;
    private readonly IInputContext _input;
    private readonly ImGuiController _imguiController;
    
    private bool _disposed;

    public ImGuiController(GL gl, IWindow window, IInputContext input)
    {
        _gl = gl;
        _window = window;
        _input = input;
        
        // Create ImGui controller from Silk.NET
        _imguiController = new ImGuiController(gl, window, input);
        
        // Configure ImGui style
        ConfigureStyle();
    }

    private void ConfigureStyle()
    {
        var io = ImGui.GetIO();
        
        // Enable docking
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
        
        // Enable keyboard navigation
        io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
        
        // Set dark theme
        ImGui.StyleColorsDark();
        
        var style = ImGui.GetStyle();
        
        // Rounding
        style.WindowRounding = 4f;
        style.FrameRounding = 2f;
        style.PopupRounding = 4f;
        style.ScrollbarRounding = 4f;
        style.GrabRounding = 2f;
        style.TabRounding = 4f;
        
        // Padding
        style.WindowPadding = new Vector2(8, 8);
        style.FramePadding = new Vector2(4, 3);
        style.ItemSpacing = new Vector2(8, 4);
        style.ItemInnerSpacing = new Vector2(4, 4);
        
        // Colors - Dark theme with accent
        var colors = style.Colors;
        
        // Background colors
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1f, 0.1f, 0.12f, 1f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.1f, 0.1f, 0.12f, 1f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.12f, 0.12f, 0.14f, 1f);
        
        // Headers
        colors[(int)ImGuiCol.Header] = new Vector4(0.2f, 0.2f, 0.25f, 1f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.3f, 0.3f, 0.35f, 1f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.25f, 0.25f, 0.3f, 1f);
        
        // Buttons
        colors[(int)ImGuiCol.Button] = new Vector4(0.2f, 0.2f, 0.25f, 1f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.3f, 0.3f, 0.4f, 1f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.15f, 0.15f, 0.2f, 1f);
        
        // Frame (input fields)
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.15f, 0.15f, 0.18f, 1f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.2f, 0.2f, 0.24f, 1f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.18f, 0.18f, 0.22f, 1f);
        
        // Tabs
        colors[(int)ImGuiCol.Tab] = new Vector4(0.15f, 0.15f, 0.18f, 1f);
        colors[(int)ImGuiCol.TabHovered] = new Vector4(0.38f, 0.38f, 0.45f, 1f);
        colors[(int)ImGuiCol.TabActive] = new Vector4(0.28f, 0.28f, 0.35f, 1f);
        colors[(int)ImGuiCol.TabUnfocused] = new Vector4(0.15f, 0.15f, 0.18f, 1f);
        colors[(int)ImGuiCol.TabUnfocusedActive] = new Vector4(0.2f, 0.2f, 0.25f, 1f);
        
        // Title
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.1f, 0.1f, 0.12f, 1f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.15f, 0.15f, 0.18f, 1f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.1f, 0.1f, 0.12f, 1f);
        
        // Docking
        colors[(int)ImGuiCol.DockingPreview] = new Vector4(0.4f, 0.4f, 0.9f, 0.7f);
        colors[(int)ImGuiCol.DockingEmptyBg] = new Vector4(0.1f, 0.1f, 0.12f, 1f);
        
        // Accent color (for selection, checkmarks, etc.)
        var accent = new Vector4(0.4f, 0.6f, 1f, 1f);
        colors[(int)ImGuiCol.CheckMark] = accent;
        colors[(int)ImGuiCol.SliderGrab] = accent;
        colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.5f, 0.7f, 1f, 1f);
        colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.3f, 0.3f, 0.35f, 1f);
        colors[(int)ImGuiCol.ResizeGripHovered] = accent;
        colors[(int)ImGuiCol.ResizeGripActive] = accent;
        
        // Separator
        colors[(int)ImGuiCol.Separator] = new Vector4(0.25f, 0.25f, 0.3f, 1f);
        colors[(int)ImGuiCol.SeparatorHovered] = accent;
        colors[(int)ImGuiCol.SeparatorActive] = accent;
    }

    /// <summary>
    /// Update ImGui input state. Call at the beginning of each frame.
    /// </summary>
    public void Update(float deltaTime)
    {
        _imguiController.Update(deltaTime);
    }

    /// <summary>
    /// Render ImGui draw data. Call at the end of each frame.
    /// </summary>
    public void Render()
    {
        _imguiController.Render();
    }

    /// <summary>
    /// Check if ImGui wants to capture keyboard input
    /// </summary>
    public bool WantCaptureKeyboard => ImGui.GetIO().WantCaptureKeyboard;

    /// <summary>
    /// Check if ImGui wants to capture mouse input
    /// </summary>
    public bool WantCaptureMouse => ImGui.GetIO().WantCaptureMouse;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _imguiController.Dispose();
    }
}
