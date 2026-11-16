namespace RedHoleEngine.Resources.ModelImport;

/// <summary>
/// Resource loader for glTF 2.0 models (.gltf and .glb).
/// </summary>
public class ModelLoader : ResourceLoader<Model>
{
    /// <summary>
    /// Options applied to every model this loader imports
    /// </summary>
    public GltfImportOptions Options { get; set; } = new();

    /// <summary>
    /// Whether the file extension is one this loader can import
    /// </summary>
    public static bool IsSupported(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".glb", StringComparison.OrdinalIgnoreCase);
    }

    public override Model? Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Model file not found: {path}");
            return null;
        }

        if (!IsSupported(path))
        {
            Console.WriteLine($"Unsupported model format: {path}");
            return null;
        }

        try
        {
            var model = GltfImporter.Load(path, Options);
            foreach (var warning in model.Warnings)
                Console.WriteLine($"Model import warning ({path}): {warning}");
            return model;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load model {path}: {ex.Message}");
            return null;
        }
    }
}

/// <summary>
/// Model loading helpers for <see cref="ResourceManager"/>.
/// </summary>
public static class ModelResourceExtensions
{
    /// <summary>
    /// Load a glTF/GLB model by path (relative to <see cref="ResourceManager.BasePath"/>),
    /// registering the model loader on first use. Returns null if the file cannot be imported.
    /// </summary>
    public static Model? LoadModel(this ResourceManager resources, string path)
    {
        if (resources.GetLoader<Model>() == null)
            resources.RegisterLoader(new ModelLoader());

        return resources.Load<Model>(path);
    }
}
