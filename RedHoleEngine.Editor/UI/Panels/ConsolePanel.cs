using System.Numerics;
using ImGuiNET;

namespace RedHoleEngine.Editor.UI.Panels;

/// <summary>
/// Console panel for logging and debugging
/// </summary>
public class ConsolePanel : EditorPanel
{
    public override string Title => "Console";

    private readonly List<LogEntry> _logs = new();
    private readonly object _logsLock = new();
    private bool _autoScroll = true;
    private bool _showInfo = true;
    private bool _showWarning = true;
    private bool _showError = true;
    private string _filter = "";
    private int _maxLogs = 1000;

    public ConsolePanel()
    {
        // Capture console output
        Console.SetOut(new ConsoleRedirector(this));
    }

    /// <summary>
    /// Log an info message
    /// </summary>
    public void Log(string message)
    {
        AddLog(LogLevel.Info, message);
    }

    /// <summary>
    /// Log a warning message
    /// </summary>
    public void LogWarning(string message)
    {
        AddLog(LogLevel.Warning, message);
    }

    /// <summary>
    /// Log an error message
    /// </summary>
    public void LogError(string message)
    {
        AddLog(LogLevel.Error, message);
    }

    private void AddLog(LogLevel level, string message)
    {
        lock (_logsLock)
        {
            _logs.Add(new LogEntry
            {
                Level = level,
                Message = message,
                Time = DateTime.Now
            });

            // Trim old logs
            while (_logs.Count > _maxLogs)
            {
                _logs.RemoveAt(0);
            }
        }
    }

    protected override void OnDraw()
    {
        // Toolbar
        DrawToolbar();

        ImGui.Separator();

        // Log list
        DrawLogList();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Clear"))
        {
            lock (_logsLock)
            {
                _logs.Clear();
            }
        }

        ImGui.SameLine();

        // Filter toggles
        var infoColor = _showInfo ? new Vector4(0.4f, 0.8f, 1f, 1f) : new Vector4(0.5f, 0.5f, 0.5f, 1f);
        var warnColor = _showWarning ? new Vector4(1f, 0.9f, 0.4f, 1f) : new Vector4(0.5f, 0.5f, 0.5f, 1f);
        var errorColor = _showError ? new Vector4(1f, 0.4f, 0.4f, 1f) : new Vector4(0.5f, 0.5f, 0.5f, 1f);

        ImGui.PushStyleColor(ImGuiCol.Text, infoColor);
        if (ImGui.Button("Info"))
            _showInfo = !_showInfo;
        ImGui.PopStyleColor();

        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, warnColor);
        if (ImGui.Button("Warn"))
            _showWarning = !_showWarning;
        ImGui.PopStyleColor();

        ImGui.SameLine();

        ImGui.PushStyleColor(ImGuiCol.Text, errorColor);
        if (ImGui.Button("Error"))
            _showError = !_showError;
        ImGui.PopStyleColor();

        ImGui.SameLine();

        // Auto-scroll toggle
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);

        ImGui.SameLine();

        // Search filter
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##Filter", "Filter...", ref _filter, 256);
    }

    private void DrawLogList()
    {
        var windowSize = ImGui.GetContentRegionAvail();
        
        if (ImGui.BeginChild("LogList", windowSize, ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar))
        {
            lock (_logsLock)
            {
                foreach (var log in _logs)
                {
                    // Filter by level
                    if (!ShouldShowLog(log))
                        continue;

                    // Filter by search
                    if (!string.IsNullOrEmpty(_filter) && 
                        !log.Message.Contains(_filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    DrawLogEntry(log);
                }
            }

            if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            {
                ImGui.SetScrollHereY(1.0f);
            }
        }
        ImGui.EndChild();
    }

    private bool ShouldShowLog(LogEntry log)
    {
        return log.Level switch
        {
            LogLevel.Info => _showInfo,
            LogLevel.Warning => _showWarning,
            LogLevel.Error => _showError,
            _ => true
        };
    }

    private void DrawLogEntry(LogEntry log)
    {
        var color = log.Level switch
        {
            LogLevel.Info => new Vector4(0.8f, 0.8f, 0.8f, 1f),
            LogLevel.Warning => new Vector4(1f, 0.9f, 0.4f, 1f),
            LogLevel.Error => new Vector4(1f, 0.4f, 0.4f, 1f),
            _ => new Vector4(1f, 1f, 1f, 1f)
        };

        var prefix = log.Level switch
        {
            LogLevel.Info => "[INFO]",
            LogLevel.Warning => "[WARN]",
            LogLevel.Error => "[ERROR]",
            _ => ""
        };

        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextWrapped($"{log.Time:HH:mm:ss} {prefix} {log.Message}");
        ImGui.PopStyleColor();

        // Copy on click
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText(log.Message);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to copy");
        }
    }

    private enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    private struct LogEntry
    {
        public LogLevel Level;
        public string Message;
        public DateTime Time;
    }

    /// <summary>
    /// Redirects Console.WriteLine to the console panel
    /// </summary>
    private class ConsoleRedirector : TextWriter
    {
        private readonly ConsolePanel _console;
        private readonly TextWriter _original;

        public ConsoleRedirector(ConsolePanel console)
        {
            _console = console;
            _original = Console.Out;
        }

        public override System.Text.Encoding Encoding => System.Text.Encoding.UTF8;

        public override void WriteLine(string? value)
        {
            if (value == null) return;

            // Detect level from message prefix
            if (value.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("error:", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("exception", StringComparison.OrdinalIgnoreCase))
            {
                _console.LogError(value);
            }
            else if (value.Contains("[WARN]", StringComparison.OrdinalIgnoreCase) ||
                     value.Contains("warning:", StringComparison.OrdinalIgnoreCase))
            {
                _console.LogWarning(value);
            }
            else
            {
                _console.Log(value);
            }

            // Also write to original console
            _original.WriteLine(value);
        }

        public override void Write(char value)
        {
            _original.Write(value);
        }
    }
}
