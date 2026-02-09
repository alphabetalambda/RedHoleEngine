using System.Numerics;
using ImGuiNET;
using RedHoleEngine.Game;
using RedHoleEngine.Terminal;

namespace RedHoleEngine.Editor.UI.Panels;

public class TerminalPanel : EditorPanel
{
    private readonly TerminalSession _session;
    private readonly Func<GameSaveManager?> _getSaveManager;
    private readonly List<string> _output = new();
    private string _commandBuffer = string.Empty;
    private string _slotName = "default";
    private bool _autoScroll = true;

    public TerminalPanel(TerminalSession session, Func<GameSaveManager?> getSaveManager)
    {
        _session = session;
        _getSaveManager = getSaveManager;
        AppendLine("Virtual terminal ready. Type 'help' for commands.");
    }

    public override string Title => "Terminal";

    protected override void OnDraw()
    {
        ImGui.TextUnformatted("Current directory: " + _session.CurrentDirectory);
        ImGui.Separator();

        ImGui.BeginChild("TerminalOutput", new Vector2(0, -90f), ImGuiChildFlags.Border, ImGuiWindowFlags.HorizontalScrollbar);
        foreach (var line in _output)
        {
            ImGui.TextUnformatted(line);
        }
        if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
        {
            ImGui.SetScrollHereY(1.0f);
        }
        ImGui.EndChild();

        if (ImGui.InputText("##TerminalInput", ref _commandBuffer, 1024, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            ExecuteCommand(_commandBuffer);
            _commandBuffer = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Run"))
        {
            ExecuteCommand(_commandBuffer);
            _commandBuffer = string.Empty;
        }

        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        ImGui.Separator();
        ImGui.Text("Save slot");
        ImGui.SameLine();
        ImGui.InputText("##TerminalSlot", ref _slotName, 128);

        if (ImGui.Button("Save FS"))
        {
            SaveFileSystem();
        }
        ImGui.SameLine();
        if (ImGui.Button("Load FS"))
        {
            LoadFileSystem();
        }
    }

    private void ExecuteCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return;

        AppendLine($"> {command}");
        var result = _session.Execute(command);
        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                AppendLine(line);
            }
        }
    }

    private void SaveFileSystem()
    {
        var saveManager = _getSaveManager();
        if (saveManager == null)
        {
            AppendLine("No save manager available.");
            return;
        }

        var save = new GameSaveData { VirtualFileSystem = _session.SaveState() };
        saveManager.Save(_slotName, save);
        AppendLine($"Saved filesystem to slot '{_slotName}'.");
    }

    private void LoadFileSystem()
    {
        var saveManager = _getSaveManager();
        if (saveManager == null)
        {
            AppendLine("No save manager available.");
            return;
        }

        var save = saveManager.Load(_slotName);
        if (save?.VirtualFileSystem == null)
        {
            AppendLine($"No filesystem found for slot '{_slotName}'.");
            return;
        }

        _session.LoadState(save.VirtualFileSystem);
        AppendLine($"Loaded filesystem from slot '{_slotName}'.");
    }

    private void AppendLine(string text)
    {
        _output.Add(text);
    }
}
