using System.Numerics;
using ImGuiNET;

namespace RedHoleML.Trainer.UI.Panels;

/// <summary>
/// Panel for exporting trained models to files
/// </summary>
public class ExportPanel
{
    private readonly TrainerState _state;
    
    private string _exportPath = "";
    private string _modelName = "trained_model";
    private bool _showExportDialog;
    private bool _includeSchema = true;
    private string _lastExportPath = "";
    private DateTime _lastExportTime;
    private bool _lastExportSuccess;
    private string _lastExportError = "";

    public bool IsVisible { get; set; } = true;

    public ExportPanel(TrainerState state)
    {
        _state = state;
    }

    public void ShowExportDialog()
    {
        if (_state.TrainedModel != null)
        {
            _showExportDialog = true;
        }
    }

    public void Draw()
    {
        if (!IsVisible) return;

        bool visible = IsVisible;
        if (ImGui.Begin("Export", ref visible))
        {
            IsVisible = visible;
            if (_state.TrainedModel == null)
            {
                DrawNoModelState();
            }
            else
            {
                DrawExportOptions();
                ImGui.Separator();
                DrawModelInfo();
                ImGui.Separator();
                DrawExportHistory();
            }
        }
        else
        {
            IsVisible = visible;
        }
        ImGui.End();

        DrawExportDialog();
    }

    private void DrawNoModelState()
    {
        var windowSize = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize("No trained model to export");
        
        ImGui.SetCursorPos(new Vector2(
            (windowSize.X - textSize.X) / 2,
            (windowSize.Y - textSize.Y) / 2
        ));
        
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No trained model to export");
        
        ImGui.SetCursorPos(new Vector2(
            (windowSize.X - 200) / 2,
            (windowSize.Y + textSize.Y) / 2 + 10
        ));
        
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Train a model first to export it");
    }

    private void DrawExportOptions()
    {
        ImGui.Text("Export Options");
        ImGui.Spacing();

        // Model name
        ImGui.SetNextItemWidth(300);
        ImGui.InputText("Model Name", ref _modelName, 256);
        
        ImGui.SameLine();
        HelpMarker("The name for the exported model file (without extension)");

        // Export path
        ImGui.SetNextItemWidth(400);
        ImGui.InputText("Export Directory", ref _exportPath, 1024);
        
        ImGui.SameLine();
        if (ImGui.SmallButton("Browse..."))
        {
            // Default to current directory or user's documents
            if (string.IsNullOrEmpty(_exportPath))
            {
                _exportPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
        }

        // Options
        ImGui.Spacing();
        ImGui.Checkbox("Include schema for validation", ref _includeSchema);
        ImGui.SameLine();
        HelpMarker("Include the data schema to validate inputs when loading the model");

        // Export button
        ImGui.Spacing();
        ImGui.Spacing();

        var canExport = !string.IsNullOrWhiteSpace(_modelName) && 
                        !string.IsNullOrWhiteSpace(_exportPath) &&
                        Directory.Exists(_exportPath);

        ImGui.BeginDisabled(!canExport);
        if (ImGui.Button("Export Model", new Vector2(150, 35)))
        {
            ExportModel();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        if (ImGui.Button("Quick Export", new Vector2(120, 35)))
        {
            _showExportDialog = true;
        }
        ImGui.SameLine();
        HelpMarker("Open the quick export dialog with file path input");

        // Show validation messages
        if (string.IsNullOrWhiteSpace(_modelName))
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Please enter a model name");
        }
        else if (string.IsNullOrWhiteSpace(_exportPath))
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Please specify an export directory");
        }
        else if (!Directory.Exists(_exportPath))
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Export directory does not exist");
        }
    }

    private void DrawModelInfo()
    {
        ImGui.Text("Model Information");
        ImGui.Spacing();

        ImGui.Columns(2, "ModelInfoColumns", false);
        ImGui.SetColumnWidth(0, 150);

        ImGui.Text("Model Type:");
        ImGui.NextColumn();
        ImGui.TextColored(new Vector4(0.4f, 0.7f, 1f, 1f), GetModelTypeDisplayName(_state.ModelType));
        ImGui.NextColumn();

        ImGui.Text("Label Column:");
        ImGui.NextColumn();
        ImGui.Text(_state.LabelColumn);
        ImGui.NextColumn();

        ImGui.Text("Feature Count:");
        ImGui.NextColumn();
        ImGui.Text($"{_state.FeatureColumns.Count}");
        ImGui.NextColumn();

        ImGui.Text("Training Rows:");
        ImGui.NextColumn();
        ImGui.Text($"{_state.DatasetRowCount:N0}");
        ImGui.NextColumn();

        if (_state.Metrics != null)
        {
            ImGui.Text("Training Time:");
            ImGui.NextColumn();
            ImGui.Text($"{_state.Metrics.TrainingTime.TotalSeconds:F2}s");
            ImGui.NextColumn();

            // Show primary metric based on model type
            if (_state.ModelType.ToString().Contains("Classification"))
            {
                ImGui.Text("Accuracy:");
                ImGui.NextColumn();
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), $"{_state.Metrics.Accuracy:P2}");
                ImGui.NextColumn();
            }
            else if (_state.ModelType.ToString().Contains("Regression"))
            {
                ImGui.Text("R-Squared:");
                ImGui.NextColumn();
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), $"{_state.Metrics.RSquared:F4}");
                ImGui.NextColumn();
            }
        }

        ImGui.Columns(1);

        // Features list
        ImGui.Spacing();
        ImGui.Text("Features:");
        
        ImGui.BeginChild("FeaturesList", new Vector2(0, 80), ImGuiChildFlags.Border);
        foreach (var feature in _state.FeatureColumns)
        {
            var type = _state.ColumnTypes.GetValueOrDefault(feature, typeof(string));
            var typeStr = type == typeof(float) ? "num" : type == typeof(int) ? "int" : type == typeof(bool) ? "bool" : "str";
            ImGui.BulletText($"{feature} [{typeStr}]");
        }
        ImGui.EndChild();
    }

    private void DrawExportHistory()
    {
        ImGui.Text("Last Export");
        ImGui.Spacing();

        if (string.IsNullOrEmpty(_lastExportPath))
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No models exported yet");
            return;
        }

        if (_lastExportSuccess)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Export successful!");
            
            ImGui.Columns(2, "LastExportColumns", false);
            ImGui.SetColumnWidth(0, 100);

            ImGui.Text("Path:");
            ImGui.NextColumn();
            ImGui.TextWrapped(_lastExportPath);
            ImGui.NextColumn();

            ImGui.Text("Time:");
            ImGui.NextColumn();
            ImGui.Text(_lastExportTime.ToString("yyyy-MM-dd HH:mm:ss"));
            ImGui.NextColumn();

            ImGui.Columns(1);

            ImGui.Spacing();
            if (ImGui.SmallButton("Open Containing Folder"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_lastExportPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = dir,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to open folder: {ex.Message}");
                }
            }

            ImGui.SameLine();
            if (ImGui.SmallButton("Copy Path"))
            {
                ImGui.SetClipboardText(_lastExportPath);
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), "Export failed");
            ImGui.TextWrapped(_lastExportError);
        }
    }

    private void DrawExportDialog()
    {
        if (!_showExportDialog) return;

        ImGui.OpenPopup("Export Model");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(550, 220));

        if (ImGui.BeginPopupModal("Export Model", ref _showExportDialog, ImGuiWindowFlags.NoResize))
        {
            ImGui.Text("Export Path (full file path):");
            
            // Build default path suggestion
            if (string.IsNullOrEmpty(_exportPath))
            {
                _exportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"{_modelName}.zip");
            }
            
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##ExportPath", ref _exportPath, 1024);

            ImGui.Spacing();
            
            ImGui.Text("Model Name:");
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("##ModelName", ref _modelName, 256);

            ImGui.Spacing();
            ImGui.Checkbox("Include schema", ref _includeSchema);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Export", new Vector2(100, 0)))
            {
                // If path doesn't have extension, treat it as directory
                if (!_exportPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    _exportPath = Path.Combine(_exportPath, $"{_modelName}.zip");
                }
                
                ExportModel();
                
                if (_lastExportSuccess)
                {
                    _showExportDialog = false;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _showExportDialog = false;
            }

            ImGui.EndPopup();
        }
    }

    private void ExportModel()
    {
        try
        {
            var mlContext = _state.MLContext;
            var model = _state.TrainedModel!;

            // Determine full path
            string fullPath;
            if (Path.GetExtension(_exportPath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                fullPath = _exportPath;
            }
            else
            {
                fullPath = Path.Combine(_exportPath, $"{_modelName}.zip");
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Save the model
            if (_includeSchema && _state.ModelSchema != null)
            {
                mlContext.Model.Save(model, _state.ModelSchema, fullPath);
            }
            else
            {
                mlContext.Model.Save(model, null, fullPath);
            }

            // Record success
            _lastExportPath = fullPath;
            _lastExportTime = DateTime.Now;
            _lastExportSuccess = true;
            _lastExportError = "";

            Console.WriteLine($"Model exported successfully to: {fullPath}");

            // Also save model metadata as JSON
            SaveModelMetadata(Path.ChangeExtension(fullPath, ".json"));
        }
        catch (Exception ex)
        {
            _lastExportPath = _exportPath;
            _lastExportTime = DateTime.Now;
            _lastExportSuccess = false;
            _lastExportError = ex.Message;

            Console.WriteLine($"Failed to export model: {ex.Message}");
        }
    }

    private void SaveModelMetadata(string metadataPath)
    {
        try
        {
            var metadata = new ModelMetadata
            {
                ModelType = _state.ModelType.ToString(),
                LabelColumn = _state.LabelColumn,
                FeatureColumns = _state.FeatureColumns.ToList(),
                ColumnTypes = _state.ColumnTypes.ToDictionary(x => x.Key, x => x.Value.Name),
                TrainedOn = DateTime.Now,
                DatasetRowCount = _state.DatasetRowCount,
                Hyperparameters = new Dictionary<string, object>
                {
                    ["NumberOfLeaves"] = _state.Hyperparameters.NumberOfLeaves,
                    ["NumberOfTrees"] = _state.Hyperparameters.NumberOfTrees,
                    ["LearningRate"] = _state.Hyperparameters.LearningRate,
                    ["MinDataPointsInLeaves"] = _state.Hyperparameters.MinDataPointsInLeaves,
                    ["NumberOfClusters"] = _state.Hyperparameters.NumberOfClusters,
                    ["Rank"] = _state.Hyperparameters.Rank
                }
            };

            if (_state.Metrics != null)
            {
                metadata.Metrics = new Dictionary<string, double>
                {
                    ["Accuracy"] = _state.Metrics.Accuracy,
                    ["F1Score"] = _state.Metrics.F1Score,
                    ["Precision"] = _state.Metrics.Precision,
                    ["Recall"] = _state.Metrics.Recall,
                    ["RSquared"] = _state.Metrics.RSquared,
                    ["RMSE"] = _state.Metrics.RootMeanSquaredError,
                    ["TrainingTimeSeconds"] = _state.Metrics.TrainingTime.TotalSeconds
                };
            }

            var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(metadataPath, json);
            Console.WriteLine($"Model metadata saved to: {metadataPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save model metadata: {ex.Message}");
        }
    }

    private static string GetModelTypeDisplayName(ModelType type)
    {
        return type switch
        {
            ModelType.BinaryClassification_FastTree => "Binary Classification (FastTree)",
            ModelType.BinaryClassification_LightGbm => "Binary Classification (LightGBM)",
            ModelType.BinaryClassification_LogisticRegression => "Binary Classification (Logistic Regression)",
            ModelType.MulticlassClassification_LightGbm => "Multiclass Classification (LightGBM)",
            ModelType.MulticlassClassification_NaiveBayes => "Multiclass Classification (Naive Bayes)",
            ModelType.Regression_FastTree => "Regression (FastTree)",
            ModelType.Regression_LightGbm => "Regression (LightGBM)",
            ModelType.Regression_Sdca => "Regression (SDCA)",
            ModelType.Clustering_KMeans => "Clustering (K-Means)",
            ModelType.AnomalyDetection_RandomizedPca => "Anomaly Detection (Randomized PCA)",
            _ => type.ToString()
        };
    }

    private static void HelpMarker(string description)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.BeginItemTooltip())
        {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 20f);
            ImGui.TextUnformatted(description);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }
    }
}

/// <summary>
/// Model metadata for export
/// </summary>
internal class ModelMetadata
{
    public string ModelType { get; set; } = "";
    public string LabelColumn { get; set; } = "";
    public List<string> FeatureColumns { get; set; } = new();
    public Dictionary<string, string> ColumnTypes { get; set; } = new();
    public DateTime TrainedOn { get; set; }
    public int DatasetRowCount { get; set; }
    public Dictionary<string, object> Hyperparameters { get; set; } = new();
    public Dictionary<string, double>? Metrics { get; set; }
}
