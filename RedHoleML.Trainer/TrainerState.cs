using Microsoft.ML;
using Microsoft.ML.Data;

namespace RedHoleML.Trainer;

/// <summary>
/// Shared state for the ML Trainer application
/// </summary>
public sealed class TrainerState : IDisposable
{
    private readonly MLContext _mlContext;

    public TrainerState()
    {
        _mlContext = new MLContext(seed: 42);
    }

    /// <summary>
    /// The ML.NET context
    /// </summary>
    public MLContext MLContext => _mlContext;

    #region Dataset State

    /// <summary>
    /// Current loaded dataset
    /// </summary>
    public IDataView? Dataset { get; private set; }

    /// <summary>
    /// Dataset schema
    /// </summary>
    public DataViewSchema? DatasetSchema { get; private set; }

    /// <summary>
    /// Raw data as list of dictionaries (for preview)
    /// </summary>
    public List<Dictionary<string, object>> RawData { get; } = new();

    /// <summary>
    /// Column names in the dataset
    /// </summary>
    public List<string> ColumnNames { get; } = new();

    /// <summary>
    /// Column types in the dataset
    /// </summary>
    public Dictionary<string, Type> ColumnTypes { get; } = new();

    /// <summary>
    /// Whether a dataset is loaded
    /// </summary>
    public bool HasDataset => Dataset != null;

    /// <summary>
    /// Number of rows in the dataset
    /// </summary>
    public int DatasetRowCount { get; private set; }

    /// <summary>
    /// Number of columns in the dataset
    /// </summary>
    public int DatasetColumnCount => ColumnNames.Count;

    /// <summary>
    /// Path to the loaded dataset file
    /// </summary>
    public string DatasetPath { get; private set; } = "";

    /// <summary>
    /// Selected label column for training
    /// </summary>
    public string LabelColumn { get; set; } = "";

    /// <summary>
    /// Selected feature columns for training
    /// </summary>
    public List<string> FeatureColumns { get; } = new();

    /// <summary>
    /// Load dataset from CSV file
    /// </summary>
    public void LoadDatasetFromCsv(string filePath, bool hasHeader = true, char separator = ',')
    {
        ClearDataset();

        DatasetPath = filePath;

        // Load with TextLoader
        var loader = _mlContext.Data.CreateTextLoader(new TextLoader.Options
        {
            HasHeader = hasHeader,
            Separators = new[] { separator },
            AllowQuoting = true,
            TrimWhitespace = true
        });

        // First pass: detect columns
        using (var reader = new StreamReader(filePath))
        {
            var headerLine = reader.ReadLine();
            if (headerLine != null)
            {
                var headerColumns = headerLine.Split(separator);
                foreach (var col in headerColumns)
                {
                    var colName = col.Trim().Trim('"');
                    ColumnNames.Add(colName);
                    ColumnTypes[colName] = typeof(string); // Default to string, will refine
                }
            }
        }

        // Load dataset with auto-detected schema
        var textLoaderColumns = new List<TextLoader.Column>();
        for (int i = 0; i < ColumnNames.Count; i++)
        {
            textLoaderColumns.Add(new TextLoader.Column(ColumnNames[i], DataKind.String, i));
        }

        var textLoader = _mlContext.Data.CreateTextLoader(new TextLoader.Options
        {
            HasHeader = hasHeader,
            Separators = new[] { separator },
            AllowQuoting = true,
            TrimWhitespace = true,
            Columns = textLoaderColumns.ToArray()
        });

        Dataset = textLoader.Load(filePath);
        DatasetSchema = Dataset.Schema;

        // Load raw data for preview (limit to first 1000 rows)
        LoadRawDataPreview(filePath, hasHeader, separator, 1000);

        // Infer column types from data
        InferColumnTypes();

        Console.WriteLine($"Loaded dataset: {DatasetRowCount} rows, {ColumnNames.Count} columns");
    }

    private void LoadRawDataPreview(string filePath, bool hasHeader, char separator, int maxRows)
    {
        RawData.Clear();
        
        using var reader = new StreamReader(filePath);
        
        // Skip header if present
        if (hasHeader)
            reader.ReadLine();

        int rowCount = 0;
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            rowCount++;
            if (RawData.Count < maxRows)
            {
                var values = ParseCsvLine(line, separator);
                var row = new Dictionary<string, object>();
                
                for (int i = 0; i < Math.Min(values.Length, ColumnNames.Count); i++)
                {
                    row[ColumnNames[i]] = values[i];
                }
                
                RawData.Add(row);
            }
        }

        DatasetRowCount = rowCount;
    }

    private string[] ParseCsvLine(string line, char separator)
    {
        var result = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == separator && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        
        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private void InferColumnTypes()
    {
        foreach (var colName in ColumnNames)
        {
            var values = RawData.Take(100).Select(r => r.GetValueOrDefault(colName)?.ToString() ?? "").ToList();
            ColumnTypes[colName] = InferType(values);
        }
    }

    private Type InferType(List<string> values)
    {
        bool allInt = true;
        bool allFloat = true;
        bool allBool = true;

        foreach (var val in values.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            if (!int.TryParse(val, out _)) allInt = false;
            if (!float.TryParse(val, out _)) allFloat = false;
            if (!bool.TryParse(val, out _)) allBool = false;
        }

        if (allBool) return typeof(bool);
        if (allInt) return typeof(int);
        if (allFloat) return typeof(float);
        return typeof(string);
    }

    /// <summary>
    /// Clear the loaded dataset
    /// </summary>
    public void ClearDataset()
    {
        Dataset = null;
        DatasetSchema = null;
        RawData.Clear();
        ColumnNames.Clear();
        ColumnTypes.Clear();
        DatasetRowCount = 0;
        DatasetPath = "";
        LabelColumn = "";
        FeatureColumns.Clear();
    }

    #endregion

    #region Model State

    /// <summary>
    /// Selected model type
    /// </summary>
    public ModelType ModelType { get; set; } = ModelType.None;

    /// <summary>
    /// Trained model
    /// </summary>
    public ITransformer? TrainedModel { get; set; }

    /// <summary>
    /// Model schema (for saving)
    /// </summary>
    public DataViewSchema? ModelSchema { get; set; }

    /// <summary>
    /// Training metrics
    /// </summary>
    public TrainingMetrics? Metrics { get; set; }

    /// <summary>
    /// Model hyperparameters
    /// </summary>
    public ModelHyperparameters Hyperparameters { get; } = new();

    /// <summary>
    /// Clear the trained model
    /// </summary>
    public void ClearModel()
    {
        TrainedModel = null;
        ModelSchema = null;
        Metrics = null;
    }

    #endregion

    #region Training State

    /// <summary>
    /// Whether training is in progress
    /// </summary>
    public bool IsTraining { get; set; }

    /// <summary>
    /// Training progress (0-1)
    /// </summary>
    public float TrainingProgress { get; set; }

    /// <summary>
    /// Training status message
    /// </summary>
    public string TrainingStatus { get; set; } = "";

    /// <summary>
    /// Training cancellation token source
    /// </summary>
    public CancellationTokenSource? TrainingCts { get; set; }

    /// <summary>
    /// Train/test split ratio (0-1, portion for training)
    /// </summary>
    public float TrainTestSplit { get; set; } = 0.8f;

    #endregion

    /// <summary>
    /// Reset all state
    /// </summary>
    public void Reset()
    {
        ClearDataset();
        ClearModel();
        ModelType = ModelType.None;
        IsTraining = false;
        TrainingProgress = 0f;
        TrainingStatus = "";
    }

    public void Dispose()
    {
        TrainingCts?.Cancel();
        TrainingCts?.Dispose();
    }
}

/// <summary>
/// Available model types
/// </summary>
public enum ModelType
{
    None,
    
    // Classification
    BinaryClassification_FastTree,
    BinaryClassification_LightGbm,
    BinaryClassification_LogisticRegression,
    MulticlassClassification_LightGbm,
    MulticlassClassification_NaiveBayes,
    
    // Regression
    Regression_FastTree,
    Regression_LightGbm,
    Regression_Sdca,
    
    // Clustering
    Clustering_KMeans,
    
    // Anomaly Detection
    AnomalyDetection_RandomizedPca
}

/// <summary>
/// Model hyperparameters
/// </summary>
public class ModelHyperparameters
{
    // Tree-based models
    public int NumberOfLeaves { get; set; } = 20;
    public int NumberOfTrees { get; set; } = 100;
    public int MinDataPointsInLeaves { get; set; } = 10;
    public float LearningRate { get; set; } = 0.1f;

    // Clustering
    public int NumberOfClusters { get; set; } = 3;
    
    // Anomaly detection
    public int Rank { get; set; } = 20;

    // General
    public int MaxIterations { get; set; } = 100;
}

/// <summary>
/// Training metrics
/// </summary>
public class TrainingMetrics
{
    // Classification metrics
    public double Accuracy { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double AreaUnderCurve { get; set; }
    public double LogLoss { get; set; }

    // Regression metrics
    public double RSquared { get; set; }
    public double MeanAbsoluteError { get; set; }
    public double MeanSquaredError { get; set; }
    public double RootMeanSquaredError { get; set; }

    // Clustering metrics
    public double AverageDistance { get; set; }
    public double DaviesBouldinIndex { get; set; }

    // General
    public TimeSpan TrainingTime { get; set; }
}
