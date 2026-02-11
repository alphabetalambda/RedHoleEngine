using System.Numerics;
using ImGuiNET;

namespace RedHoleML.Trainer.UI.Panels;

/// <summary>
/// Panel for loading and previewing datasets
/// </summary>
public class DatasetPanel
{
    private readonly TrainerState _state;
    private string _importPath = "";
    private bool _showImportDialog;
    private bool _hasHeader = true;
    private int _separatorIndex; // 0=comma, 1=tab, 2=semicolon
    private readonly string[] _separators = { "Comma (,)", "Tab", "Semicolon (;)" };

    public bool IsVisible { get; set; } = true;

    public DatasetPanel(TrainerState state)
    {
        _state = state;
    }

    public void ShowImportDialog()
    {
        _showImportDialog = true;
    }

    public void Draw()
    {
        if (!IsVisible) return;

        bool visible = IsVisible;
        if (ImGui.Begin("Dataset", ref visible))
        {
            IsVisible = visible;
            DrawToolbar();
            ImGui.Separator();

            if (_state.HasDataset)
            {
                DrawDatasetInfo();
                ImGui.Separator();
                DrawColumnConfig();
                ImGui.Separator();
                DrawDataPreview();
            }
            else
            {
                DrawEmptyState();
            }
        }
        else
        {
            IsVisible = visible;
        }
        ImGui.End();

        DrawImportDialog();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Import CSV"))
        {
            _showImportDialog = true;
        }

        ImGui.SameLine();

        ImGui.BeginDisabled(!_state.HasDataset);
        if (ImGui.Button("Clear"))
        {
            _state.ClearDataset();
        }
        ImGui.EndDisabled();
    }

    private void DrawEmptyState()
    {
        var windowSize = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize("No dataset loaded");
        
        ImGui.SetCursorPos(new Vector2(
            (windowSize.X - textSize.X) / 2,
            (windowSize.Y - textSize.Y) / 2
        ));
        
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No dataset loaded");
        
        ImGui.SetCursorPos(new Vector2(
            (windowSize.X - 150) / 2,
            (windowSize.Y + textSize.Y) / 2 + 10
        ));
        
        if (ImGui.Button("Import CSV File", new Vector2(150, 30)))
        {
            _showImportDialog = true;
        }
    }

    private void DrawDatasetInfo()
    {
        ImGui.Text($"File: {Path.GetFileName(_state.DatasetPath)}");
        ImGui.Text($"Rows: {_state.DatasetRowCount:N0}");
        ImGui.Text($"Columns: {_state.DatasetColumnCount}");
    }

    private void DrawColumnConfig()
    {
        ImGui.Text("Column Configuration");
        
        // Label column selector
        ImGui.SetNextItemWidth(200);
        if (ImGui.BeginCombo("Label Column", string.IsNullOrEmpty(_state.LabelColumn) ? "(Select)" : _state.LabelColumn))
        {
            foreach (var col in _state.ColumnNames)
            {
                bool isSelected = col == _state.LabelColumn;
                if (ImGui.Selectable(col, isSelected))
                {
                    _state.LabelColumn = col;
                    // Remove from features if it was selected
                    _state.FeatureColumns.Remove(col);
                }
                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }

        // Feature columns
        ImGui.Text("Feature Columns:");
        ImGui.SameLine();
        if (ImGui.SmallButton("Select All"))
        {
            _state.FeatureColumns.Clear();
            _state.FeatureColumns.AddRange(_state.ColumnNames.Where(c => c != _state.LabelColumn));
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear All"))
        {
            _state.FeatureColumns.Clear();
        }

        ImGui.BeginChild("FeatureColumns", new Vector2(0, 120), ImGuiChildFlags.Border);
        foreach (var col in _state.ColumnNames)
        {
            if (col == _state.LabelColumn) continue;

            bool isSelected = _state.FeatureColumns.Contains(col);
            var type = _state.ColumnTypes.GetValueOrDefault(col, typeof(string));
            var typeStr = type == typeof(float) ? "num" : type == typeof(int) ? "int" : type == typeof(bool) ? "bool" : "str";
            
            if (ImGui.Checkbox($"{col} [{typeStr}]", ref isSelected))
            {
                if (isSelected)
                    _state.FeatureColumns.Add(col);
                else
                    _state.FeatureColumns.Remove(col);
            }
        }
        ImGui.EndChild();

        // Show selected count
        ImGui.TextColored(
            _state.FeatureColumns.Count > 0 ? new Vector4(0.4f, 0.9f, 0.4f, 1f) : new Vector4(1f, 0.6f, 0.3f, 1f),
            $"{_state.FeatureColumns.Count} features selected"
        );
    }

    private void DrawDataPreview()
    {
        ImGui.Text("Data Preview (first 100 rows)");

        if (_state.RawData.Count == 0)
            return;

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("DataPreview", _state.ColumnNames.Count, flags, new Vector2(0, 250)))
        {
            // Headers
            foreach (var col in _state.ColumnNames)
            {
                var isLabel = col == _state.LabelColumn;
                var isFeature = _state.FeatureColumns.Contains(col);
                var headerFlags = ImGuiTableColumnFlags.None;
                
                ImGui.TableSetupColumn(col, headerFlags, 100);
            }
            ImGui.TableHeadersRow();

            // Data rows
            var rowsToShow = Math.Min(_state.RawData.Count, 100);
            for (int i = 0; i < rowsToShow; i++)
            {
                ImGui.TableNextRow();
                var row = _state.RawData[i];

                for (int j = 0; j < _state.ColumnNames.Count; j++)
                {
                    ImGui.TableSetColumnIndex(j);
                    var col = _state.ColumnNames[j];
                    var value = row.GetValueOrDefault(col)?.ToString() ?? "";
                    
                    // Highlight label column
                    if (col == _state.LabelColumn)
                    {
                        ImGui.TextColored(new Vector4(0.4f, 0.8f, 1f, 1f), value);
                    }
                    else if (_state.FeatureColumns.Contains(col))
                    {
                        ImGui.Text(value);
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), value);
                    }
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawImportDialog()
    {
        if (!_showImportDialog) return;

        ImGui.OpenPopup("Import Dataset");
        
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(500, 200));

        if (ImGui.BeginPopupModal("Import Dataset", ref _showImportDialog, ImGuiWindowFlags.NoResize))
        {
            ImGui.Text("CSV File Path:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##Path", ref _importPath, 1024);

            ImGui.Spacing();

            ImGui.Checkbox("First row is header", ref _hasHeader);

            ImGui.SetNextItemWidth(150);
            ImGui.Combo("Separator", ref _separatorIndex, _separators, _separators.Length);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Import", new Vector2(100, 0)))
            {
                if (!string.IsNullOrEmpty(_importPath) && File.Exists(_importPath))
                {
                    try
                    {
                        char sep = _separatorIndex switch
                        {
                            1 => '\t',
                            2 => ';',
                            _ => ','
                        };
                        _state.LoadDatasetFromCsv(_importPath, _hasHeader, sep);
                        _showImportDialog = false;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to load dataset: {ex.Message}");
                    }
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _showImportDialog = false;
            }

            ImGui.EndPopup();
        }
    }
}
