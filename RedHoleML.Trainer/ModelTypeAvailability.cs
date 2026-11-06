namespace RedHoleML.Trainer;

/// <summary>
/// Pure helpers deciding whether a model type is usable on this machine and what to fall back to.
/// LightGBM needs a native library that is missing on e.g. Apple Silicon (osx-arm64).
/// </summary>
public static class ModelTypeAvailability
{
    public const string LightGbmUnavailableReason = "LightGBM native library not available on this platform";

    public static bool IsLightGbm(ModelType type) => type is
        ModelType.BinaryClassification_LightGbm or
        ModelType.MulticlassClassification_LightGbm or
        ModelType.Regression_LightGbm;

    public static bool IsAvailable(ModelType type, bool lightGbmAvailable) =>
        lightGbmAvailable || !IsLightGbm(type);

    /// <summary>
    /// Returns the supported model type to use instead of <paramref name="type"/>
    /// (unchanged when already available) and a user-visible message when a fallback happened.
    /// </summary>
    public static (ModelType Type, string? Message) Resolve(ModelType type, bool lightGbmAvailable)
    {
        if (IsAvailable(type, lightGbmAvailable))
            return (type, null);

        var fallback = type switch
        {
            ModelType.BinaryClassification_LightGbm => ModelType.BinaryClassification_FastTree,
            ModelType.Regression_LightGbm => ModelType.Regression_FastTree,
            // FastTree has no multiclass trainer here; NaiveBayes is the supported alternative.
            _ => ModelType.MulticlassClassification_NaiveBayes
        };
        return (fallback, $"{LightGbmUnavailableReason}; switched to {fallback}.");
    }
}
