using System.Numerics;
using ImGuiNET;

namespace RedHoleML.Trainer.UI.Panels;

/// <summary>
/// Panel for designing and configuring ML models
/// </summary>
public class ModelDesignerPanel
{
    private readonly TrainerState _state;
    private int _selectedCategory;
    private readonly string[] _categories = { "Classification", "Regression", "Clustering", "Anomaly Detection" };

    public bool IsVisible { get; set; } = true;

    public ModelDesignerPanel(TrainerState state)
    {
        _state = state;
    }

    public void Draw()
    {
        if (!IsVisible) return;

        bool visible = IsVisible;
        if (ImGui.Begin("Model Designer", ref visible))
        {
            IsVisible = visible;
            DrawModelTypeSelector();
            ImGui.Separator();
            DrawHyperparameters();
            ImGui.Separator();
            DrawModelSummary();
        }
        else
        {
            IsVisible = visible;
        }
        ImGui.End();
    }

    private void DrawModelTypeSelector()
    {
        ImGui.Text("Model Type");
        
        // Category tabs
        ImGui.SetNextItemWidth(-1);
        ImGui.Combo("Category", ref _selectedCategory, _categories, _categories.Length);
        
        ImGui.Spacing();

        switch (_selectedCategory)
        {
            case 0: // Classification
                DrawClassificationModels();
                break;
            case 1: // Regression
                DrawRegressionModels();
                break;
            case 2: // Clustering
                DrawClusteringModels();
                break;
            case 3: // Anomaly Detection
                DrawAnomalyModels();
                break;
        }
    }

    private void DrawClassificationModels()
    {
        ImGui.Text("Binary Classification:");
        if (DrawModelButton("FastTree", "Fast, accurate tree-based classifier", ModelType.BinaryClassification_FastTree))
            _state.ModelType = ModelType.BinaryClassification_FastTree;
        
        ImGui.SameLine();
        if (DrawModelButton("LightGBM", "Gradient boosting, handles large datasets", ModelType.BinaryClassification_LightGbm))
            _state.ModelType = ModelType.BinaryClassification_LightGbm;
        
        ImGui.SameLine();
        if (DrawModelButton("Logistic Regression", "Simple, interpretable", ModelType.BinaryClassification_LogisticRegression))
            _state.ModelType = ModelType.BinaryClassification_LogisticRegression;

        ImGui.Spacing();
        ImGui.Text("Multiclass Classification:");
        if (DrawModelButton("LightGBM Multi", "Multi-class gradient boosting", ModelType.MulticlassClassification_LightGbm))
            _state.ModelType = ModelType.MulticlassClassification_LightGbm;
        
        ImGui.SameLine();
        if (DrawModelButton("Naive Bayes", "Fast, good for text", ModelType.MulticlassClassification_NaiveBayes))
            _state.ModelType = ModelType.MulticlassClassification_NaiveBayes;
    }

    private void DrawRegressionModels()
    {
        if (DrawModelButton("FastTree", "Fast tree-based regression", ModelType.Regression_FastTree))
            _state.ModelType = ModelType.Regression_FastTree;
        
        ImGui.SameLine();
        if (DrawModelButton("LightGBM", "Gradient boosting regression", ModelType.Regression_LightGbm))
            _state.ModelType = ModelType.Regression_LightGbm;
        
        ImGui.SameLine();
        if (DrawModelButton("SDCA", "Stochastic dual coordinate ascent", ModelType.Regression_Sdca))
            _state.ModelType = ModelType.Regression_Sdca;
    }

    private void DrawClusteringModels()
    {
        if (DrawModelButton("K-Means", "Partition data into K clusters", ModelType.Clustering_KMeans))
            _state.ModelType = ModelType.Clustering_KMeans;
    }

    private void DrawAnomalyModels()
    {
        if (DrawModelButton("Randomized PCA", "Detect anomalies using PCA", ModelType.AnomalyDetection_RandomizedPca))
            _state.ModelType = ModelType.AnomalyDetection_RandomizedPca;
    }

    private bool DrawModelButton(string name, string description, ModelType type)
    {
        bool isSelected = _state.ModelType == type;
        
        if (isSelected)
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.3f, 0.5f, 0.7f, 1f));
        
        bool clicked = ImGui.Button(name, new Vector2(130, 40));
        
        if (isSelected)
            ImGui.PopStyleColor();
        
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(description);

        return clicked;
    }

    private void DrawHyperparameters()
    {
        ImGui.Text("Hyperparameters");

        if (_state.ModelType == ModelType.None)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "Select a model type first");
            return;
        }

        var hp = _state.Hyperparameters;

        switch (_state.ModelType)
        {
            case ModelType.BinaryClassification_FastTree:
            case ModelType.BinaryClassification_LightGbm:
            case ModelType.MulticlassClassification_LightGbm:
            case ModelType.Regression_FastTree:
            case ModelType.Regression_LightGbm:
                DrawTreeHyperparameters(hp);
                break;
            
            case ModelType.Clustering_KMeans:
                DrawClusteringHyperparameters(hp);
                break;
            
            case ModelType.AnomalyDetection_RandomizedPca:
                DrawAnomalyHyperparameters(hp);
                break;
            
            default:
                DrawGeneralHyperparameters(hp);
                break;
        }

        ImGui.Spacing();
        
        // Train/test split
        ImGui.SetNextItemWidth(200);
        float split = _state.TrainTestSplit * 100;
        if (ImGui.SliderFloat("Train/Test Split", ref split, 50, 95, "%.0f%% train"))
        {
            _state.TrainTestSplit = split / 100f;
        }
    }

    private void DrawTreeHyperparameters(ModelHyperparameters hp)
    {
        int numTrees = hp.NumberOfTrees;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Number of Trees", ref numTrees))
            hp.NumberOfTrees = Math.Max(1, Math.Min(1000, numTrees));

        int numLeaves = hp.NumberOfLeaves;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Number of Leaves", ref numLeaves))
            hp.NumberOfLeaves = Math.Max(2, Math.Min(1000, numLeaves));

        int minData = hp.MinDataPointsInLeaves;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Min Data Points in Leaves", ref minData))
            hp.MinDataPointsInLeaves = Math.Max(1, Math.Min(100, minData));

        float lr = hp.LearningRate;
        ImGui.SetNextItemWidth(150);
        if (ImGui.SliderFloat("Learning Rate", ref lr, 0.001f, 1.0f, "%.3f"))
            hp.LearningRate = lr;
    }

    private void DrawClusteringHyperparameters(ModelHyperparameters hp)
    {
        int numClusters = hp.NumberOfClusters;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Number of Clusters", ref numClusters))
            hp.NumberOfClusters = Math.Max(2, Math.Min(100, numClusters));
    }

    private void DrawAnomalyHyperparameters(ModelHyperparameters hp)
    {
        int rank = hp.Rank;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Rank (PCA components)", ref rank))
            hp.Rank = Math.Max(1, Math.Min(100, rank));
    }

    private void DrawGeneralHyperparameters(ModelHyperparameters hp)
    {
        int maxIter = hp.MaxIterations;
        ImGui.SetNextItemWidth(150);
        if (ImGui.InputInt("Max Iterations", ref maxIter))
            hp.MaxIterations = Math.Max(1, Math.Min(10000, maxIter));
    }

    private void DrawModelSummary()
    {
        ImGui.Text("Model Summary");

        if (_state.ModelType == ModelType.None)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.5f, 0.5f, 1f), "No model selected");
            return;
        }

        var typeColor = GetModelTypeColor();
        ImGui.TextColored(typeColor, $"Type: {GetModelTypeName()}");
        ImGui.Text($"Category: {_categories[_selectedCategory]}");

        if (_state.HasDataset)
        {
            ImGui.Text($"Label: {(string.IsNullOrEmpty(_state.LabelColumn) ? "(not set)" : _state.LabelColumn)}");
            ImGui.Text($"Features: {_state.FeatureColumns.Count}");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "No dataset loaded");
        }

        // Ready to train check
        bool canTrain = _state.HasDataset && 
                        !string.IsNullOrEmpty(_state.LabelColumn) && 
                        _state.FeatureColumns.Count > 0 &&
                        _state.ModelType != ModelType.None;

        ImGui.Spacing();
        if (canTrain)
        {
            ImGui.TextColored(new Vector4(0.4f, 0.9f, 0.4f, 1f), "Ready to train!");
        }
        else
        {
            ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Configure dataset and model to train");
        }
    }

    private Vector4 GetModelTypeColor()
    {
        return _selectedCategory switch
        {
            0 => new Vector4(0.4f, 0.8f, 1f, 1f),    // Classification - blue
            1 => new Vector4(0.4f, 1f, 0.6f, 1f),    // Regression - green
            2 => new Vector4(1f, 0.8f, 0.4f, 1f),    // Clustering - orange
            3 => new Vector4(1f, 0.5f, 0.5f, 1f),    // Anomaly - red
            _ => new Vector4(1f, 1f, 1f, 1f)
        };
    }

    private string GetModelTypeName()
    {
        return _state.ModelType switch
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
            _ => "None"
        };
    }
}
