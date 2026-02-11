using Microsoft.ML;
using Microsoft.ML.Data;

namespace RedHoleEngine.ML.Services;

/// <summary>
/// Central ML.NET service providing machine learning capabilities for games.
/// Supports training, prediction, model persistence, and various ML tasks.
/// </summary>
public sealed class MLService : IDisposable
{
    private readonly MLContext _mlContext;
    private readonly Dictionary<string, ITransformer> _loadedModels = new();
    private readonly Dictionary<string, IDisposable> _predictionEngines = new();
    private bool _disposed;

    /// <summary>
    /// The underlying ML.NET context for advanced operations
    /// </summary>
    public MLContext Context => _mlContext;

    /// <summary>
    /// Seed for reproducible results (null for random)
    /// </summary>
    public int? Seed { get; }

    public MLService(int? seed = null)
    {
        Seed = seed;
        _mlContext = new MLContext(seed);
    }

    #region Model Management

    /// <summary>
    /// Load a pre-trained model from file
    /// </summary>
    public void LoadModel(string modelId, string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Model file not found: {filePath}");

        var model = _mlContext.Model.Load(filePath, out _);
        _loadedModels[modelId] = model;
        Console.WriteLine($"[ML] Loaded model '{modelId}' from {filePath}");
    }

    /// <summary>
    /// Save a trained model to file
    /// </summary>
    public void SaveModel(string modelId, string filePath, DataViewSchema? schema = null)
    {
        if (!_loadedModels.TryGetValue(modelId, out var model))
            throw new KeyNotFoundException($"Model '{modelId}' not found");

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);

        _mlContext.Model.Save(model, schema, filePath);
        Console.WriteLine($"[ML] Saved model '{modelId}' to {filePath}");
    }

    /// <summary>
    /// Register an already-trained model
    /// </summary>
    public void RegisterModel(string modelId, ITransformer model)
    {
        _loadedModels[modelId] = model;
    }

    /// <summary>
    /// Check if a model is loaded
    /// </summary>
    public bool HasModel(string modelId) => _loadedModels.ContainsKey(modelId);

    /// <summary>
    /// Get a loaded model by ID
    /// </summary>
    public ITransformer GetModel(string modelId)
    {
        if (!_loadedModels.TryGetValue(modelId, out var model))
            throw new KeyNotFoundException($"Model '{modelId}' not found");
        return model;
    }

    /// <summary>
    /// Unload a model to free memory
    /// </summary>
    public void UnloadModel(string modelId)
    {
        if (_predictionEngines.TryGetValue(modelId, out var engine))
        {
            (engine as IDisposable)?.Dispose();
            _predictionEngines.Remove(modelId);
        }
        _loadedModels.Remove(modelId);
    }

    #endregion

    #region Prediction

    /// <summary>
    /// Create a typed prediction engine for fast repeated predictions
    /// </summary>
    public PredictionEngine<TInput, TOutput> CreatePredictionEngine<TInput, TOutput>(string modelId)
        where TInput : class
        where TOutput : class, new()
    {
        var model = GetModel(modelId);
        var engine = _mlContext.Model.CreatePredictionEngine<TInput, TOutput>(model);
        return engine;
    }

    /// <summary>
    /// Make a single prediction using a cached prediction engine
    /// </summary>
    public TOutput Predict<TInput, TOutput>(string modelId, TInput input)
        where TInput : class
        where TOutput : class, new()
    {
        var key = $"{modelId}:{typeof(TInput).Name}:{typeof(TOutput).Name}";
        
        if (!_predictionEngines.TryGetValue(key, out var engineBase))
        {
            var engine = CreatePredictionEngine<TInput, TOutput>(modelId);
            _predictionEngines[key] = engine;
            return engine.Predict(input);
        }

        return ((PredictionEngine<TInput, TOutput>)engineBase).Predict(input);
    }

    #endregion

    #region Training Helpers

    /// <summary>
    /// Create a data view from an enumerable collection
    /// </summary>
    public IDataView CreateDataView<T>(IEnumerable<T> data) where T : class
    {
        return _mlContext.Data.LoadFromEnumerable(data);
    }

    /// <summary>
    /// Train a binary classification model
    /// </summary>
    public ITransformer TrainBinaryClassifier<TData>(
        IEnumerable<TData> trainingData,
        string labelColumnName = "Label",
        string featureColumnName = "Features",
        BinaryClassifierType classifierType = BinaryClassifierType.FastTree) where TData : class
    {
        var dataView = CreateDataView(trainingData);
        
        ITransformer model = classifierType switch
        {
            BinaryClassifierType.FastTree => _mlContext.BinaryClassification.Trainers.FastTree(labelColumnName, featureColumnName).Fit(dataView),
            BinaryClassifierType.LightGbm => _mlContext.BinaryClassification.Trainers.LightGbm(labelColumnName, featureColumnName).Fit(dataView),
            BinaryClassifierType.SdcaLogisticRegression => _mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(labelColumnName, featureColumnName).Fit(dataView),
            BinaryClassifierType.AveragedPerceptron => _mlContext.BinaryClassification.Trainers.AveragedPerceptron(labelColumnName, featureColumnName).Fit(dataView),
            _ => throw new ArgumentOutOfRangeException(nameof(classifierType))
        };

        return model;
    }

    /// <summary>
    /// Train a multi-class classification model
    /// </summary>
    public ITransformer TrainMultiClassifier<TData>(
        IEnumerable<TData> trainingData,
        string labelColumnName = "Label",
        string featureColumnName = "Features",
        MultiClassifierType classifierType = MultiClassifierType.SdcaMaximumEntropy) where TData : class
    {
        var dataView = CreateDataView(trainingData);

        ITransformer model = classifierType switch
        {
            MultiClassifierType.SdcaMaximumEntropy => _mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy(labelColumnName, featureColumnName).Fit(dataView),
            MultiClassifierType.LightGbm => _mlContext.MulticlassClassification.Trainers.LightGbm(labelColumnName, featureColumnName).Fit(dataView),
            MultiClassifierType.NaiveBayes => _mlContext.MulticlassClassification.Trainers.NaiveBayes(labelColumnName, featureColumnName).Fit(dataView),
            _ => throw new ArgumentOutOfRangeException(nameof(classifierType))
        };

        return model;
    }

    /// <summary>
    /// Train a regression model for predicting continuous values
    /// </summary>
    public ITransformer TrainRegressor<TData>(
        IEnumerable<TData> trainingData,
        string labelColumnName = "Label",
        string featureColumnName = "Features",
        RegressorType regressorType = RegressorType.FastTree) where TData : class
    {
        var dataView = CreateDataView(trainingData);

        ITransformer model = regressorType switch
        {
            RegressorType.FastTree => _mlContext.Regression.Trainers.FastTree(labelColumnName, featureColumnName).Fit(dataView),
            RegressorType.LightGbm => _mlContext.Regression.Trainers.LightGbm(labelColumnName, featureColumnName).Fit(dataView),
            RegressorType.Sdca => _mlContext.Regression.Trainers.Sdca(labelColumnName, featureColumnName).Fit(dataView),
            RegressorType.OnlineGradientDescent => _mlContext.Regression.Trainers.OnlineGradientDescent(labelColumnName, featureColumnName).Fit(dataView),
            _ => throw new ArgumentOutOfRangeException(nameof(regressorType))
        };

        return model;
    }

    /// <summary>
    /// Train a clustering model for grouping similar data
    /// </summary>
    public ITransformer TrainClusterer<TData>(
        IEnumerable<TData> trainingData,
        int numberOfClusters = 3,
        string featureColumnName = "Features") where TData : class
    {
        var dataView = CreateDataView(trainingData);
        
        var pipeline = _mlContext.Clustering.Trainers.KMeans(featureColumnName, numberOfClusters: numberOfClusters);
        return pipeline.Fit(dataView);
    }

    /// <summary>
    /// Train an anomaly detection model
    /// </summary>
    public ITransformer TrainAnomalyDetector<TData>(
        IEnumerable<TData> trainingData,
        string featureColumnName = "Features",
        AnomalyDetectorType detectorType = AnomalyDetectorType.RandomizedPca) where TData : class
    {
        var dataView = CreateDataView(trainingData);

        var pipeline = detectorType switch
        {
            AnomalyDetectorType.RandomizedPca => _mlContext.AnomalyDetection.Trainers.RandomizedPca(featureColumnName),
            _ => throw new ArgumentOutOfRangeException(nameof(detectorType))
        };

        return pipeline.Fit(dataView);
    }

    #endregion

    #region Feature Engineering

    /// <summary>
    /// Create a pipeline that concatenates multiple columns into a feature vector
    /// </summary>
    public IEstimator<ITransformer> ConcatenateFeatures(string outputColumnName, params string[] inputColumnNames)
    {
        return _mlContext.Transforms.Concatenate(outputColumnName, inputColumnNames);
    }

    /// <summary>
    /// Normalize features to a standard range
    /// </summary>
    public IEstimator<ITransformer> NormalizeFeatures(string inputColumnName, string? outputColumnName = null)
    {
        return _mlContext.Transforms.NormalizeMinMax(outputColumnName ?? inputColumnName, inputColumnName);
    }

    /// <summary>
    /// Convert a text label column to a key (for classification)
    /// </summary>
    public IEstimator<ITransformer> MapLabelToKey(string inputColumnName, string? outputColumnName = null)
    {
        return _mlContext.Transforms.Conversion.MapValueToKey(outputColumnName ?? inputColumnName, inputColumnName);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var engine in _predictionEngines.Values)
        {
            (engine as IDisposable)?.Dispose();
        }
        _predictionEngines.Clear();
        _loadedModels.Clear();
    }
}

#region Enums

public enum BinaryClassifierType
{
    FastTree,
    LightGbm,
    SdcaLogisticRegression,
    AveragedPerceptron
}

public enum MultiClassifierType
{
    SdcaMaximumEntropy,
    LightGbm,
    NaiveBayes
}

public enum RegressorType
{
    FastTree,
    LightGbm,
    Sdca,
    OnlineGradientDescent
}

public enum AnomalyDetectorType
{
    RandomizedPca
}

#endregion
