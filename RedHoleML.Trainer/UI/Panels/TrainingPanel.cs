using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Microsoft.ML;

namespace RedHoleML.Trainer.UI.Panels;

/// <summary>
/// Panel for training models and viewing progress/metrics
/// </summary>
public class TrainingPanel
{
    private readonly TrainerState _state;
    private readonly List<string> _trainingLog = new();
    private bool _autoScroll = true;

    public bool IsVisible { get; set; } = true;

    public TrainingPanel(TrainerState state)
    {
        _state = state;
    }

    public void Update(float deltaTime)
    {
        // Update training progress if needed
    }

    public void Draw()
    {
        if (!IsVisible) return;

        bool visible = IsVisible;
        if (ImGui.Begin("Training", ref visible))
        {
            IsVisible = visible;
            DrawControls();
            ImGui.Separator();
            DrawProgress();
            ImGui.Separator();
            DrawMetrics();
            ImGui.Separator();
            DrawLog();
        }
        else
        {
            IsVisible = visible;
        }
        ImGui.End();
    }

    private void DrawControls()
    {
        bool canTrain = _state.HasDataset && 
                        !string.IsNullOrEmpty(_state.LabelColumn) && 
                        _state.FeatureColumns.Count > 0 &&
                        _state.ModelType != ModelType.None &&
                        !_state.IsTraining;

        ImGui.BeginDisabled(!canTrain);
        if (ImGui.Button("Train Model", new Vector2(120, 30)))
        {
            StartTraining();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(!_state.IsTraining);
        if (ImGui.Button("Cancel", new Vector2(80, 30)))
        {
            CancelTraining();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();

        ImGui.BeginDisabled(_state.TrainedModel == null);
        if (ImGui.Button("Clear Model", new Vector2(100, 30)))
        {
            _state.ClearModel();
            _trainingLog.Clear();
        }
        ImGui.EndDisabled();
    }

    private void DrawProgress()
    {
        if (_state.IsTraining)
        {
            ImGui.ProgressBar(_state.TrainingProgress, new Vector2(-1, 20), $"{_state.TrainingProgress:P0}");
            ImGui.TextColored(new Vector4(1f, 0.9f, 0.3f, 1f), _state.TrainingStatus);
        }
        else if (_state.TrainedModel != null)
        {
            ImGui.ProgressBar(1f, new Vector2(-1, 20), "Complete");
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Training complete!");
        }
        else
        {
            ImGui.ProgressBar(0f, new Vector2(-1, 20), "Not started");
            ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1f), "Configure dataset and model, then click Train");
        }
    }

    private void DrawMetrics()
    {
        ImGui.Text("Training Metrics");

        if (_state.Metrics == null)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No metrics available");
            return;
        }

        var m = _state.Metrics;

        ImGui.Columns(2, "MetricsColumns", false);
        ImGui.SetColumnWidth(0, 180);

        // Show relevant metrics based on model type
        if (_state.ModelType.ToString().Contains("Classification"))
        {
            DrawMetric("Accuracy", m.Accuracy, 0, 1, true);
            DrawMetric("F1 Score", m.F1Score, 0, 1, true);
            DrawMetric("Precision", m.Precision, 0, 1, true);
            DrawMetric("Recall", m.Recall, 0, 1, true);
            if (m.AreaUnderCurve > 0)
                DrawMetric("AUC", m.AreaUnderCurve, 0, 1, true);
            DrawMetric("Log Loss", m.LogLoss, 0, 2, false);
        }
        else if (_state.ModelType.ToString().Contains("Regression"))
        {
            DrawMetric("R-Squared", m.RSquared, 0, 1, true);
            DrawMetric("MAE", m.MeanAbsoluteError, 0, double.MaxValue, false);
            DrawMetric("MSE", m.MeanSquaredError, 0, double.MaxValue, false);
            DrawMetric("RMSE", m.RootMeanSquaredError, 0, double.MaxValue, false);
        }
        else if (_state.ModelType.ToString().Contains("Clustering"))
        {
            DrawMetric("Avg Distance", m.AverageDistance, 0, double.MaxValue, false);
            DrawMetric("Davies-Bouldin", m.DaviesBouldinIndex, 0, double.MaxValue, false);
        }

        ImGui.Columns(1);

        ImGui.Spacing();
        ImGui.Text($"Training Time: {m.TrainingTime.TotalSeconds:F2}s");
    }

    private void DrawMetric(string name, double value, double min, double max, bool higherIsBetter)
    {
        ImGui.Text(name);
        ImGui.NextColumn();

        var normalized = (value - min) / (max - min);
        var color = higherIsBetter 
            ? new Vector4((float)(1 - normalized), (float)normalized, 0.2f, 1f)
            : new Vector4((float)normalized, (float)(1 - normalized), 0.2f, 1f);

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "N/A");
        }
        else
        {
            ImGui.TextColored(color, $"{value:F4}");
        }
        ImGui.NextColumn();
    }

    private void DrawLog()
    {
        ImGui.Text("Training Log");
        ImGui.SameLine();
        ImGui.Checkbox("Auto-scroll", ref _autoScroll);
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear"))
        {
            _trainingLog.Clear();
        }

        ImGui.BeginChild("LogScroll", new Vector2(0, 150), ImGuiChildFlags.Border);
        
        foreach (var line in _trainingLog)
        {
            if (line.Contains("Error") || line.Contains("error"))
                ImGui.TextColored(new Vector4(1f, 0.4f, 0.4f, 1f), line);
            else if (line.Contains("Warning") || line.Contains("warning"))
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.3f, 1f), line);
            else if (line.Contains("Complete") || line.Contains("Success"))
                ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), line);
            else
                ImGui.Text(line);
        }

        if (_autoScroll && ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1.0f);

        ImGui.EndChild();
    }

    private void StartTraining()
    {
        _trainingLog.Clear();
        Log("Starting training...");
        Log($"Model: {_state.ModelType}");
        Log($"Dataset: {_state.DatasetRowCount} rows");
        Log($"Features: {string.Join(", ", _state.FeatureColumns)}");
        Log($"Label: {_state.LabelColumn}");

        _state.IsTraining = true;
        _state.TrainingProgress = 0f;
        _state.TrainingStatus = "Preparing data...";
        _state.TrainingCts = new CancellationTokenSource();

        Task.Run(() => TrainAsync(_state.TrainingCts.Token));
    }

    private async Task TrainAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var mlContext = _state.MLContext;

            // Update progress
            _state.TrainingProgress = 0.1f;
            _state.TrainingStatus = "Building pipeline...";
            Log("Building data pipeline...");

            // Build the pipeline
            var pipeline = BuildPipeline(mlContext);
            if (pipeline == null)
            {
                Log("Error: Failed to build pipeline");
                return;
            }

            ct.ThrowIfCancellationRequested();

            // Split data
            _state.TrainingProgress = 0.2f;
            _state.TrainingStatus = "Splitting data...";
            Log($"Splitting data ({_state.TrainTestSplit:P0} train / {1 - _state.TrainTestSplit:P0} test)...");

            var split = mlContext.Data.TrainTestSplit(_state.Dataset!, testFraction: 1 - _state.TrainTestSplit);

            ct.ThrowIfCancellationRequested();

            // Train
            _state.TrainingProgress = 0.3f;
            _state.TrainingStatus = "Training model...";
            Log("Training model...");

            var model = pipeline.Fit(split.TrainSet);

            ct.ThrowIfCancellationRequested();

            // Evaluate
            _state.TrainingProgress = 0.8f;
            _state.TrainingStatus = "Evaluating model...";
            Log("Evaluating model on test set...");

            var predictions = model.Transform(split.TestSet);
            var metrics = EvaluateModel(mlContext, predictions);

            stopwatch.Stop();
            metrics.TrainingTime = stopwatch.Elapsed;

            // Store results
            _state.TrainedModel = model;
            _state.ModelSchema = _state.Dataset!.Schema;
            _state.Metrics = metrics;

            _state.TrainingProgress = 1f;
            _state.TrainingStatus = "Complete!";
            Log($"Training complete in {stopwatch.Elapsed.TotalSeconds:F2}s");
            Log($"Accuracy: {metrics.Accuracy:P2}");
        }
        catch (OperationCanceledException)
        {
            Log("Training cancelled");
            _state.TrainingStatus = "Cancelled";
        }
        catch (Exception ex)
        {
            Log($"Error: {ex.Message}");
            _state.TrainingStatus = $"Error: {ex.Message}";
        }
        finally
        {
            _state.IsTraining = false;
        }
    }

    private IEstimator<ITransformer>? BuildPipeline(MLContext mlContext)
    {
        // Concatenate features
        var featureCols = _state.FeatureColumns.ToArray();
        
        // Convert string columns to numeric if needed
        var transforms = new List<IEstimator<ITransformer>>();
        
        foreach (var col in featureCols)
        {
            var colType = _state.ColumnTypes.GetValueOrDefault(col, typeof(string));
            if (colType == typeof(string))
            {
                // Hash string columns to numeric
                transforms.Add(mlContext.Transforms.Conversion.MapValueToKey(col, col));
            }
        }

        // Build initial pipeline
        IEstimator<ITransformer> pipeline = transforms.Count > 0 
            ? transforms.Aggregate((a, b) => a.Append(b))
            : mlContext.Transforms.CopyColumns("temp", featureCols[0]);

        // Concatenate all features
        pipeline = pipeline.Append(mlContext.Transforms.Concatenate("Features", featureCols));

        // Add trainer based on model type
        pipeline = _state.ModelType switch
        {
            ModelType.BinaryClassification_FastTree => pipeline.Append(
                mlContext.BinaryClassification.Trainers.FastTree(_state.LabelColumn, "Features",
                    numberOfLeaves: _state.Hyperparameters.NumberOfLeaves,
                    numberOfTrees: _state.Hyperparameters.NumberOfTrees,
                    minimumExampleCountPerLeaf: _state.Hyperparameters.MinDataPointsInLeaves,
                    learningRate: _state.Hyperparameters.LearningRate)),
            
            ModelType.BinaryClassification_LightGbm => pipeline.Append(
                mlContext.BinaryClassification.Trainers.LightGbm(_state.LabelColumn, "Features",
                    numberOfLeaves: _state.Hyperparameters.NumberOfLeaves,
                    numberOfIterations: _state.Hyperparameters.NumberOfTrees,
                    minimumExampleCountPerLeaf: _state.Hyperparameters.MinDataPointsInLeaves,
                    learningRate: _state.Hyperparameters.LearningRate)),
            
            ModelType.BinaryClassification_LogisticRegression => pipeline.Append(
                mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(_state.LabelColumn, "Features")),
            
            ModelType.Regression_FastTree => pipeline.Append(
                mlContext.Regression.Trainers.FastTree(_state.LabelColumn, "Features",
                    numberOfLeaves: _state.Hyperparameters.NumberOfLeaves,
                    numberOfTrees: _state.Hyperparameters.NumberOfTrees,
                    minimumExampleCountPerLeaf: _state.Hyperparameters.MinDataPointsInLeaves,
                    learningRate: _state.Hyperparameters.LearningRate)),
            
            ModelType.Regression_LightGbm => pipeline.Append(
                mlContext.Regression.Trainers.LightGbm(_state.LabelColumn, "Features",
                    numberOfLeaves: _state.Hyperparameters.NumberOfLeaves,
                    numberOfIterations: _state.Hyperparameters.NumberOfTrees,
                    minimumExampleCountPerLeaf: _state.Hyperparameters.MinDataPointsInLeaves,
                    learningRate: _state.Hyperparameters.LearningRate)),
            
            ModelType.Regression_Sdca => pipeline.Append(
                mlContext.Regression.Trainers.Sdca(_state.LabelColumn, "Features")),
            
            ModelType.Clustering_KMeans => pipeline.Append(
                mlContext.Clustering.Trainers.KMeans("Features", 
                    numberOfClusters: _state.Hyperparameters.NumberOfClusters)),
            
            ModelType.AnomalyDetection_RandomizedPca => pipeline.Append(
                mlContext.AnomalyDetection.Trainers.RandomizedPca("Features",
                    rank: _state.Hyperparameters.Rank)),
            
            _ => null
        };

        return pipeline;
    }

    private TrainingMetrics EvaluateModel(MLContext mlContext, IDataView predictions)
    {
        var metrics = new TrainingMetrics();

        try
        {
            if (_state.ModelType.ToString().Contains("BinaryClassification"))
            {
                var eval = mlContext.BinaryClassification.Evaluate(predictions, _state.LabelColumn);
                metrics.Accuracy = eval.Accuracy;
                metrics.F1Score = eval.F1Score;
                metrics.Precision = eval.PositivePrecision;
                metrics.Recall = eval.PositiveRecall;
                metrics.AreaUnderCurve = eval.AreaUnderRocCurve;
                metrics.LogLoss = eval.LogLoss;
            }
            else if (_state.ModelType.ToString().Contains("Multiclass"))
            {
                var eval = mlContext.MulticlassClassification.Evaluate(predictions, _state.LabelColumn);
                metrics.Accuracy = eval.MacroAccuracy;
                metrics.F1Score = eval.MicroAccuracy;
                metrics.LogLoss = eval.LogLoss;
            }
            else if (_state.ModelType.ToString().Contains("Regression"))
            {
                var eval = mlContext.Regression.Evaluate(predictions, _state.LabelColumn);
                metrics.RSquared = eval.RSquared;
                metrics.MeanAbsoluteError = eval.MeanAbsoluteError;
                metrics.MeanSquaredError = eval.MeanSquaredError;
                metrics.RootMeanSquaredError = eval.RootMeanSquaredError;
            }
            else if (_state.ModelType.ToString().Contains("Clustering"))
            {
                var eval = mlContext.Clustering.Evaluate(predictions);
                metrics.AverageDistance = eval.AverageDistance;
                metrics.DaviesBouldinIndex = eval.DaviesBouldinIndex;
            }
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not evaluate model - {ex.Message}");
        }

        return metrics;
    }

    private void CancelTraining()
    {
        _state.TrainingCts?.Cancel();
        Log("Cancelling training...");
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _trainingLog.Add($"[{timestamp}] {message}");
    }
}
