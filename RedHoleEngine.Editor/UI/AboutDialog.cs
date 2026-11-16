using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using ImGuiNET;

namespace RedHoleEngine.Editor.UI;

/// <summary>
/// Modal "About" dialog showing application/engine versions, license and credits.
/// Call <see cref="Show"/> to open it and <see cref="Draw"/> every frame.
/// This file is also compiled into RedHoleML.Trainer.
/// </summary>
public class AboutDialog
{
    private const string PopupId = "About###AboutDialog";

    private static readonly string[] Credits =
    {
        "Silk.NET (Vulkan, OpenGL, OpenAL, windowing)",
        "ImGui.NET / Dear ImGui",
        "Microsoft.ML, FastTree, LightGBM",
        "SharpGLTF, AssimpNet",
        "SixLabors.ImageSharp",
    };

    private readonly string _applicationName;
    private readonly string _description;
    private readonly Assembly _applicationAssembly;
    private readonly Assembly? _engineAssembly;

    private bool _openRequested;
    private string? _licenseSummary;
    private bool _licenseLoaded;
    private string _status = "";

    /// <param name="applicationName">Name shown in the header, e.g. "RedHole Engine Editor"</param>
    /// <param name="description">One-line description</param>
    /// <param name="applicationAssembly">Assembly whose version is shown as the app version</param>
    /// <param name="engineAssembly">Optional engine assembly to show its version too</param>
    public AboutDialog(string applicationName, string description, Assembly applicationAssembly, Assembly? engineAssembly = null)
    {
        _applicationName = applicationName;
        _description = description;
        _applicationAssembly = applicationAssembly;
        _engineAssembly = engineAssembly;
    }

    /// <summary>
    /// Open the dialog on the next <see cref="Draw"/>
    /// </summary>
    public void Show()
    {
        _openRequested = true;
        _status = "";
    }

    /// <summary>
    /// Draw the dialog if it is open
    /// </summary>
    public void Draw()
    {
        if (_openRequested)
        {
            ImGui.OpenPopup(PopupId);
            _openRequested = false;

            if (!_licenseLoaded)
            {
                _licenseSummary = AppInfo.ReadLicenseSummary(AppInfo.FindRepositoryFile("LICENSE"));
                _licenseLoaded = true;
            }
        }

        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

        bool open = true;
        if (!ImGui.BeginPopupModal(PopupId, ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.35f, 0.3f, 1f));
        ImGui.Text(_applicationName);
        ImGui.PopStyleColor();
        ImGui.TextDisabled(_description);
        ImGui.Separator();

        ImGui.Text($"Version: {AppInfo.GetVersion(_applicationAssembly)}");
        if (_engineAssembly != null && _engineAssembly != _applicationAssembly)
        {
            ImGui.Text($"Engine: {_engineAssembly.GetName().Name} {AppInfo.GetVersion(_engineAssembly)}");
        }
        ImGui.TextDisabled($"{RuntimeInformation.FrameworkDescription} on {RuntimeInformation.OSDescription}");

        ImGui.Spacing();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 420f);
        ImGui.Text($"License: {_licenseSummary ?? "see the LICENSE file in the repository"}");
        ImGui.PopTextWrapPos();

        ImGui.Spacing();
        ImGui.Text("Built with:");
        foreach (var credit in Credits)
        {
            ImGui.BulletText(credit);
        }

        ImGui.Spacing();
        ImGui.Separator();

        if (ImGui.Button("Repository"))
        {
            _status = AppInfo.TryOpenWithShell(AppInfo.RepositoryUrl, out var error) ? "" : error;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(AppInfo.RepositoryUrl);

        ImGui.SameLine();
        if (ImGui.Button("Documentation"))
        {
            _status = AppInfo.TryOpenDocumentation(out var message) ? "" : message;
        }

        ImGui.SameLine();
        if (ImGui.Button("Copy Version"))
        {
            ImGui.SetClipboardText($"{_applicationName} {AppInfo.GetVersion(_applicationAssembly)}");
        }

        ImGui.SameLine();
        if (ImGui.Button("Close") || ImGui.IsKeyPressed(ImGuiKey.Escape) || !open)
        {
            ImGui.CloseCurrentPopup();
        }

        if (!string.IsNullOrEmpty(_status))
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.4f, 1f), _status);
        }

        ImGui.EndPopup();
    }
}
