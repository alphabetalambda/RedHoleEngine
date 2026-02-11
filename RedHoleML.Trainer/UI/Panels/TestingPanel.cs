using System.Numerics;
using ImGuiNET;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace RedHoleML.Trainer.UI.Panels;

/// <summary>
/// Panel for testing trained models with predictions
/// </summary>
public class TestingPanel
{
    private readonly TrainerState _state;
    
    // Test data input
    private readonly Dictionary<string, string> _testInputs = new();
    private readonly List<PredictionResult> _predictionHistory = new();
    private bool _autoPredict;
    
    // Batch testing
    private string _batchTestPath = "";
    private List<Dictionary<string, object>>? _batchResults;
    private bool _showBatchDialog;
    private int _batchPreviewRows = 50;

    public bool IsVisible { get; set; } = true;

    public TestingPanel(TrainerState state)
    {
        _state = state;
    }

    public void Draw()
    {
        if (!IsVisible) return;

        bool visible = IsVisible;
        if (ImGui.Begin("Testing", ref visible))
        {
            IsVisible = visible;
            if (_state.TrainedModel == null)
            {
                DrawNoModelState();
            }
            else
            {
                DrawTabs();
            }
        }
        else
        {
            IsVisible = visible;
        }
        ImGui.End();

        DrawBatchTestDialog();
    }

    private void DrawNoModelState()
    {
        var windowSize = ImGui.GetContentRegionAvail();
        var textSize = ImGui.CalcTextSize("No trained model available");
        
        ImGui.SetCursorPos(new Vector2(
            (windowSize.X - textSize.X) / 2,
            (windowSize.Y - textSize.Y) / 2
        ));
        
        ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No trained model available");
        
        ImGui.SetCursorPos(new Vector2(
            (windowSize.X - 200) / 2,
            (windowSize.Y + textSize.Y) / 2 + 10
        ));
        
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Train a model first to test predictions");
    }

    private void DrawTabs()
    {
        if (ImGui.BeginTabBar("TestingTabs"))
        {
            if (ImGui.BeginTabItem("Single Prediction"))
            {
                DrawSinglePrediction();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Batch Testing"))
            {
                DrawBatchTesting();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("History"))
            {
                DrawPredictionHistory();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawSinglePrediction()
    {
        ImGui.Text("Enter values for each feature:");
        ImGui.Spacing();

        // Initialize inputs if needed
        foreach (var col in _state.FeatureColumns)
        {
            if (!_testInputs.ContainsKey(col))
            {
                _testInputs[col] = "";
            }
        }

        // Draw input fields for each feature
        ImGui.BeginChild("FeatureInputs", new Vector2(0, 250), ImGuiChildFlags.Border);
        
        ImGui.Columns(2, "InputColumns", false);
        ImGui.SetColumnWidth(0, 200);

        foreach (var col in _state.FeatureColumns)
        {
            var type = _state.ColumnTypes.GetValueOrDefault(col, typeof(string));
            var typeStr = type == typeof(float) ? "num" : type == typeof(int) ? "int" : type == typeof(bool) ? "bool" : "str";
            
            ImGui.Text($"{col} [{typeStr}]");
            ImGui.NextColumn();

            var value = _testInputs[col];
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText($"##{col}", ref value, 256))
            {
                _testInputs[col] = value;
            }
            ImGui.NextColumn();
        }

        ImGui.Columns(1);
        ImGui.EndChild();

        ImGui.Spacing();

        // Predict button
        if (ImGui.Button("Predict", new Vector2(100, 30)))
        {
            RunSinglePrediction();
        }

        ImGui.SameLine();

        if (ImGui.Button("Clear Inputs", new Vector2(100, 30)))
        {
            foreach (var key in _testInputs.Keys.ToList())
            {
                _testInputs[key] = "";
            }
        }

        ImGui.SameLine();
        ImGui.Checkbox("Auto-predict on change", ref _autoPredict);

        // Show last prediction result
        ImGui.Separator();
        DrawLastPredictionResult();
    }

    private void DrawLastPredictionResult()
    {
        ImGui.Text("Prediction Result");

        if (_predictionHistory.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No predictions yet");
            return;
        }

        var lastResult = _predictionHistory[^1];

        ImGui.BeginChild("PredictionResult", new Vector2(0, 120), ImGuiChildFlags.Border);

        ImGui.Columns(2, "ResultColumns", false);
        ImGui.SetColumnWidth(0, 150);

        // Show prediction based on model type
        if (_state.ModelType.ToString().Contains("Classification"))
        {
            ImGui.Text("Predicted Label:");
            ImGui.NextColumn();
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), lastResult.PredictedLabel);
            ImGui.NextColumn();

            if (lastResult.Probability > 0)
            {
                ImGui.Text("Confidence:");
                ImGui.NextColumn();
                
                var confidence = (float)lastResult.Probability;
                var color = GetConfidenceColor(confidence);
                ImGui.TextColored(color, $"{confidence:P2}");
                ImGui.NextColumn();
            }

            if (lastResult.Score != 0)
            {
                ImGui.Text("Score:");
                ImGui.NextColumn();
                ImGui.Text($"{lastResult.Score:F4}");
                ImGui.NextColumn();
            }
        }
        else if (_state.ModelType.ToString().Contains("Regression"))
        {
            ImGui.Text("Predicted Value:");
            ImGui.NextColumn();
            ImGui.TextColored(new Vector4(0.4f, 0.7f, 1f, 1f), $"{lastResult.Score:F4}");
            ImGui.NextColumn();
        }
        else if (_state.ModelType.ToString().Contains("Clustering"))
        {
            ImGui.Text("Cluster ID:");
            ImGui.NextColumn();
            ImGui.TextColored(new Vector4(0.9f, 0.6f, 0.2f, 1f), lastResult.PredictedLabel);
            ImGui.NextColumn();

            ImGui.Text("Distance:");
            ImGui.NextColumn();
            ImGui.Text($"{lastResult.Score:F4}");
            ImGui.NextColumn();
        }
        else if (_state.ModelType.ToString().Contains("Anomaly"))
        {
            ImGui.Text("Is Anomaly:");
            ImGui.NextColumn();
            var isAnomaly = lastResult.PredictedLabel == "True";
            ImGui.TextColored(isAnomaly ? new Vector4(1f, 0.3f, 0.3f, 1f) : new Vector4(0.4f, 0.9f, 0.4f, 1f), 
                isAnomaly ? "ANOMALY" : "Normal");
            ImGui.NextColumn();

            ImGui.Text("Score:");
            ImGui.NextColumn();
            ImGui.Text($"{lastResult.Score:F4}");
            ImGui.NextColumn();
        }

        ImGui.Text("Timestamp:");
        ImGui.NextColumn();
        ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), lastResult.Timestamp.ToString("HH:mm:ss.fff"));
        ImGui.NextColumn();

        ImGui.Columns(1);
        ImGui.EndChild();
    }

    private static Vector4 GetConfidenceColor(float confidence)
    {
        if (confidence >= 0.9f)
            return new Vector4(0.3f, 0.9f, 0.3f, 1f); // Green
        if (confidence >= 0.7f)
            return new Vector4(0.9f, 0.9f, 0.3f, 1f); // Yellow
        if (confidence >= 0.5f)
            return new Vector4(0.9f, 0.6f, 0.3f, 1f); // Orange
        return new Vector4(0.9f, 0.3f, 0.3f, 1f); // Red
    }

    private void RunSinglePrediction()
    {
        try
        {
            var mlContext = _state.MLContext;
            var model = _state.TrainedModel!;

            // Create a single-row data view from inputs
            var inputData = new List<Dictionary<string, object>> { new(_testInputs.ToDictionary(x => x.Key, x => (object)x.Value)) };
            
            // Build the schema from feature columns
            var schemaBuilder = new DataViewSchema.Builder();
            foreach (var col in _state.FeatureColumns)
            {
                schemaBuilder.AddColumn(col, Microsoft.ML.Data.TextDataViewType.Instance);
            }

            // Create input data view using text values
            var columns = new List<TextLoader.Column>();
            for (int i = 0; i < _state.FeatureColumns.Count; i++)
            {
                columns.Add(new TextLoader.Column(_state.FeatureColumns[i], DataKind.String, i));
            }

            // Write temp CSV for prediction
            var tempFile = Path.GetTempFileName();
            try
            {
                using (var writer = new StreamWriter(tempFile))
                {
                    // Header
                    writer.WriteLine(string.Join(",", _state.FeatureColumns));
                    // Values
                    var values = _state.FeatureColumns.Select(c => EscapeCsvValue(_testInputs.GetValueOrDefault(c, "")));
                    writer.WriteLine(string.Join(",", values));
                }

                var textLoader = mlContext.Data.CreateTextLoader(new TextLoader.Options
                {
                    HasHeader = true,
                    Separators = new[] { ',' },
                    Columns = columns.ToArray()
                });

                var testData = textLoader.Load(tempFile);
                var predictions = model.Transform(testData);

                // Extract prediction
                var result = ExtractPrediction(predictions);
                result.Timestamp = DateTime.Now;
                result.Inputs = new Dictionary<string, string>(_testInputs);
                
                _predictionHistory.Add(result);
                
                // Limit history size
                while (_predictionHistory.Count > 100)
                {
                    _predictionHistory.RemoveAt(0);
                }
            }
            finally
            {
                if (File.Exists(tempFile))
                    File.Delete(tempFile);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Prediction error: {ex.Message}");
            
            _predictionHistory.Add(new PredictionResult
            {
                Timestamp = DateTime.Now,
                PredictedLabel = $"Error: {ex.Message}",
                Inputs = new Dictionary<string, string>(_testInputs)
            });
        }
    }

    private static string EscapeCsvValue(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }

    private PredictionResult ExtractPrediction(IDataView predictions)
    {
        var result = new PredictionResult();

        try
        {
            var schema = predictions.Schema;

            // Try to get prediction columns based on model type
            if (_state.ModelType.ToString().Contains("Classification"))
            {
                // Binary classification outputs
                var labelCol = schema.GetColumnOrNull("PredictedLabel");
                if (labelCol != null)
                {
                    using var cursor = predictions.GetRowCursor(new[] { labelCol.Value });
                    var getter = cursor.GetGetter<bool>(labelCol.Value);
                    if (cursor.MoveNext())
                    {
                        bool label = default;
                        getter(ref label);
                        result.PredictedLabel = label.ToString();
                    }
                }

                var probCol = schema.GetColumnOrNull("Probability");
                if (probCol != null)
                {
                    using var cursor = predictions.GetRowCursor(new[] { probCol.Value });
                    var getter = cursor.GetGetter<float>(probCol.Value);
                    if (cursor.MoveNext())
                    {
                        float prob = default;
                        getter(ref prob);
                        result.Probability = prob;
                    }
                }

                var scoreCol = schema.GetColumnOrNull("Score");
                if (scoreCol != null)
                {
                    using var cursor = predictions.GetRowCursor(new[] { scoreCol.Value });
                    var getter = cursor.GetGetter<float>(scoreCol.Value);
                    if (cursor.MoveNext())
                    {
                        float score = default;
                        getter(ref score);
                        result.Score = score;
                    }
                }
            }
            else if (_state.ModelType.ToString().Contains("Regression"))
            {
                var scoreCol = schema.GetColumnOrNull("Score");
                if (scoreCol != null)
                {
                    using var cursor = predictions.GetRowCursor(new[] { scoreCol.Value });
                    var getter = cursor.GetGetter<float>(scoreCol.Value);
                    if (cursor.MoveNext())
                    {
                        float score = default;
                        getter(ref score);
                        result.Score = score;
                        result.PredictedLabel = score.ToString("F4");
                    }
                }
            }
            else if (_state.ModelType.ToString().Contains("Clustering"))
            {
                var labelCol = schema.GetColumnOrNull("PredictedLabel");
                if (labelCol != null)
                {
                    using var cursor = predictions.GetRowCursor(new[] { labelCol.Value });
                    var getter = cursor.GetGetter<uint>(labelCol.Value);
                    if (cursor.MoveNext())
                    {
                        uint label = default;
                        getter(ref label);
                        result.PredictedLabel = $"Cluster {label}";
                    }
                }
            }
            else if (_state.ModelType.ToString().Contains("Anomaly"))
            {
                var labelCol = schema.GetColumnOrNull("PredictedLabel");
                if (labelCol != null)
                {
                    using var cursor = predictions.GetRowCursor(new[] { labelCol.Value });
                    var getter = cursor.GetGetter<bool>(labelCol.Value);
                    if (cursor.MoveNext())
                    {
                        bool label = default;
                        getter(ref label);
                        result.PredictedLabel = label.ToString();
                    }
                }

                var scoreCol = schema.GetColumnOrNull("Score");
                if (scoreCol != null)
                {
                    using var cursor = predictions.GetRowCursor(new[] { scoreCol.Value });
                    var getter = cursor.GetGetter<float>(scoreCol.Value);
                    if (cursor.MoveNext())
                    {
                        float score = default;
                        getter(ref score);
                        result.Score = score;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            result.PredictedLabel = $"Parse error: {ex.Message}";
        }

        return result;
    }

    private void DrawBatchTesting()
    {
        ImGui.Text("Test model on a CSV file with multiple samples");
        ImGui.Spacing();

        if (ImGui.Button("Load Test File"))
        {
            _showBatchDialog = true;
        }

        ImGui.SameLine();

        ImGui.BeginDisabled(_batchResults == null);
        if (ImGui.Button("Clear Results"))
        {
            _batchResults = null;
        }
        ImGui.EndDisabled();

        if (_batchResults != null)
        {
            ImGui.Separator();
            ImGui.Text($"Results: {_batchResults.Count} predictions");
            ImGui.Spacing();

            DrawBatchResultsTable();
        }
    }

    private void DrawBatchResultsTable()
    {
        if (_batchResults == null || _batchResults.Count == 0) return;

        // Determine columns to show
        var columns = _batchResults[0].Keys.ToList();
        
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("BatchResults", columns.Count, flags, new Vector2(0, 350)))
        {
            // Headers
            foreach (var col in columns)
            {
                ImGui.TableSetupColumn(col, ImGuiTableColumnFlags.None, 100);
            }
            ImGui.TableHeadersRow();

            // Data rows
            var rowsToShow = Math.Min(_batchResults.Count, _batchPreviewRows);
            for (int i = 0; i < rowsToShow; i++)
            {
                ImGui.TableNextRow();
                var row = _batchResults[i];

                for (int j = 0; j < columns.Count; j++)
                {
                    ImGui.TableSetColumnIndex(j);
                    var value = row.GetValueOrDefault(columns[j])?.ToString() ?? "";
                    
                    // Highlight prediction columns
                    if (columns[j].Contains("Predicted") || columns[j] == "Score" || columns[j] == "Probability")
                    {
                        ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), value);
                    }
                    else
                    {
                        ImGui.Text(value);
                    }
                }
            }

            ImGui.EndTable();
        }

        if (_batchResults.Count > _batchPreviewRows)
        {
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), 
                $"Showing {_batchPreviewRows} of {_batchResults.Count} rows");
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            ImGui.SliderInt("Show rows", ref _batchPreviewRows, 10, Math.Min(500, _batchResults.Count));
        }
    }

    private void DrawPredictionHistory()
    {
        ImGui.Text($"Prediction History ({_predictionHistory.Count} entries)");
        
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _predictionHistory.Clear();
        }

        if (_predictionHistory.Count == 0)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No predictions in history");
            return;
        }

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable |
                    ImGuiTableFlags.ScrollY;

        if (ImGui.BeginTable("HistoryTable", 4, flags, new Vector2(0, 350)))
        {
            ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Prediction", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Score/Confidence", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Inputs", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            // Show in reverse order (newest first)
            for (int i = _predictionHistory.Count - 1; i >= 0; i--)
            {
                var result = _predictionHistory[i];
                
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                ImGui.Text(result.Timestamp.ToString("HH:mm:ss"));

                ImGui.TableSetColumnIndex(1);
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), result.PredictedLabel);

                ImGui.TableSetColumnIndex(2);
                if (result.Probability > 0)
                {
                    ImGui.Text($"{result.Probability:P1}");
                }
                else if (result.Score != 0)
                {
                    ImGui.Text($"{result.Score:F3}");
                }
                else
                {
                    ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "-");
                }

                ImGui.TableSetColumnIndex(3);
                if (result.Inputs != null)
                {
                    var inputStr = string.Join(", ", result.Inputs.Take(3).Select(kv => $"{kv.Key}={kv.Value}"));
                    if (result.Inputs.Count > 3)
                        inputStr += $" ... (+{result.Inputs.Count - 3} more)";
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), inputStr);
                }
            }

            ImGui.EndTable();
        }
    }

    private void DrawBatchTestDialog()
    {
        if (!_showBatchDialog) return;

        ImGui.OpenPopup("Load Test File");

        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(500, 150));

        if (ImGui.BeginPopupModal("Load Test File", ref _showBatchDialog, ImGuiWindowFlags.NoResize))
        {
            ImGui.Text("CSV File Path:");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputText("##BatchPath", ref _batchTestPath, 1024);

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.Button("Run Predictions", new Vector2(120, 0)))
            {
                if (!string.IsNullOrEmpty(_batchTestPath) && File.Exists(_batchTestPath))
                {
                    RunBatchPredictions(_batchTestPath);
                    _showBatchDialog = false;
                }
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                _showBatchDialog = false;
            }

            ImGui.EndPopup();
        }
    }

    private void RunBatchPredictions(string filePath)
    {
        try
        {
            var mlContext = _state.MLContext;
            var model = _state.TrainedModel!;

            // Load test data
            var columns = new List<TextLoader.Column>();
            for (int i = 0; i < _state.FeatureColumns.Count; i++)
            {
                columns.Add(new TextLoader.Column(_state.FeatureColumns[i], DataKind.String, i));
            }

            // Read file to get all columns (not just features)
            var allColumns = new List<string>();
            using (var reader = new StreamReader(filePath))
            {
                var headerLine = reader.ReadLine();
                if (headerLine != null)
                {
                    allColumns.AddRange(headerLine.Split(',').Select(c => c.Trim().Trim('"')));
                }
            }

            // Create columns for all input columns
            var allTextColumns = new List<TextLoader.Column>();
            for (int i = 0; i < allColumns.Count; i++)
            {
                allTextColumns.Add(new TextLoader.Column(allColumns[i], DataKind.String, i));
            }

            var textLoader = mlContext.Data.CreateTextLoader(new TextLoader.Options
            {
                HasHeader = true,
                Separators = new[] { ',' },
                AllowQuoting = true,
                Columns = allTextColumns.ToArray()
            });

            var testData = textLoader.Load(filePath);
            var predictions = model.Transform(testData);

            // Extract results
            _batchResults = new List<Dictionary<string, object>>();

            var schema = predictions.Schema;
            var columnGetters = new Dictionary<string, Func<Dictionary<string, object>>>();

            using var cursor = predictions.GetRowCursor(schema);
            while (cursor.MoveNext())
            {
                var row = new Dictionary<string, object>();

                // Get input columns
                foreach (var col in allColumns)
                {
                    var column = schema.GetColumnOrNull(col);
                    if (column != null)
                    {
                        var colType = column.Value.Type;
                        if (colType == TextDataViewType.Instance)
                        {
                            var getter = cursor.GetGetter<ReadOnlyMemory<char>>(column.Value);
                            ReadOnlyMemory<char> value = default;
                            getter(ref value);
                            row[col] = value.ToString();
                        }
                    }
                }

                // Get prediction columns
                AddPredictionColumnsToRow(cursor, schema, row);

                _batchResults.Add(row);
            }

            Console.WriteLine($"Batch predictions complete: {_batchResults.Count} rows");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Batch prediction error: {ex.Message}");
            _batchResults = new List<Dictionary<string, object>>
            {
                new() { ["Error"] = ex.Message }
            };
        }
    }

    private void AddPredictionColumnsToRow(DataViewRowCursor cursor, DataViewSchema schema, Dictionary<string, object> row)
    {
        // Add common prediction columns
        var labelCol = schema.GetColumnOrNull("PredictedLabel");
        if (labelCol != null)
        {
            try
            {
                if (labelCol.Value.Type.RawType == typeof(bool))
                {
                    var getter = cursor.GetGetter<bool>(labelCol.Value);
                    bool value = default;
                    getter(ref value);
                    row["PredictedLabel"] = value;
                }
                else if (labelCol.Value.Type.RawType == typeof(uint))
                {
                    var getter = cursor.GetGetter<uint>(labelCol.Value);
                    uint value = default;
                    getter(ref value);
                    row["PredictedLabel"] = value;
                }
            }
            catch { }
        }

        var scoreCol = schema.GetColumnOrNull("Score");
        if (scoreCol != null)
        {
            try
            {
                var getter = cursor.GetGetter<float>(scoreCol.Value);
                float value = default;
                getter(ref value);
                row["Score"] = value;
            }
            catch { }
        }

        var probCol = schema.GetColumnOrNull("Probability");
        if (probCol != null)
        {
            try
            {
                var getter = cursor.GetGetter<float>(probCol.Value);
                float value = default;
                getter(ref value);
                row["Probability"] = value;
            }
            catch { }
        }
    }
}

/// <summary>
/// Represents a single prediction result
/// </summary>
public class PredictionResult
{
    public DateTime Timestamp { get; set; }
    public string PredictedLabel { get; set; } = "";
    public double Score { get; set; }
    public double Probability { get; set; }
    public Dictionary<string, string>? Inputs { get; set; }
}
