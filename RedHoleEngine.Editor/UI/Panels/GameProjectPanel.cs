using ImGuiNET;

namespace RedHoleEngine.Editor.UI.Panels;

public class GameProjectPanel : EditorPanel
{
    private readonly Func<string> _getPath;
    private readonly Action<string> _setPath;
    private readonly Func<string> _getStatus;
    private readonly Action _loadAction;
    private string _pathBuffer = string.Empty;

    public GameProjectPanel(Func<string> getPath, Action<string> setPath, Func<string> getStatus, Action loadAction)
    {
        _getPath = getPath;
        _setPath = setPath;
        _getStatus = getStatus;
        _loadAction = loadAction;
        _pathBuffer = _getPath();
    }

    public override string Title => "Project";

    protected override void OnDraw()
    {
        if (_pathBuffer != _getPath())
        {
            _pathBuffer = _getPath();
        }

        ImGui.Text("Game Project Folder");
        ImGui.InputText("##GameProjectPath", ref _pathBuffer, 512);

        if (ImGui.Button("Load Project"))
        {
            _setPath(_pathBuffer);
            _loadAction();
        }

        var status = _getStatus();
        if (!string.IsNullOrWhiteSpace(status))
        {
            ImGui.Separator();
            ImGui.TextWrapped(status);
        }
    }
}
