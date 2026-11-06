using RedHoleML.Trainer;
using Xunit;

namespace RedHoleEngine.Tests.ML;

public class ModelTypeAvailabilityTests
{
    [Theory]
    [InlineData(ModelType.BinaryClassification_LightGbm, ModelType.BinaryClassification_FastTree)]
    [InlineData(ModelType.Regression_LightGbm, ModelType.Regression_FastTree)]
    [InlineData(ModelType.MulticlassClassification_LightGbm, ModelType.MulticlassClassification_NaiveBayes)]
    public void LightGbm_Unavailable_FallsBackWithMessage(ModelType input, ModelType expected)
    {
        var (type, message) = ModelTypeAvailability.Resolve(input, lightGbmAvailable: false);
        Assert.Equal(expected, type);
        Assert.False(ModelTypeAvailability.IsAvailable(input, false));
        Assert.False(string.IsNullOrEmpty(message));
        Assert.True(ModelTypeAvailability.IsAvailable(type, false));
    }

    [Theory]
    [InlineData(ModelType.BinaryClassification_LightGbm)]
    [InlineData(ModelType.Regression_LightGbm)]
    [InlineData(ModelType.MulticlassClassification_LightGbm)]
    [InlineData(ModelType.Regression_FastTree)]
    [InlineData(ModelType.None)]
    public void LightGbm_Available_KeepsSelection(ModelType input)
    {
        var (type, message) = ModelTypeAvailability.Resolve(input, lightGbmAvailable: true);
        Assert.Equal(input, type);
        Assert.Null(message);
    }

    [Theory]
    [InlineData(ModelType.None)]
    [InlineData(ModelType.BinaryClassification_FastTree)]
    [InlineData(ModelType.Clustering_KMeans)]
    [InlineData(ModelType.AnomalyDetection_RandomizedPca)]
    public void NonLightGbm_Unaffected_WhenUnavailable(ModelType input)
    {
        var (type, message) = ModelTypeAvailability.Resolve(input, lightGbmAvailable: false);
        Assert.Equal(input, type);
        Assert.Null(message);
    }
}
