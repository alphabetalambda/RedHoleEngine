using System.Text.Json;
using System.Text.Json.Serialization;

namespace RedHoleEngine.Resources;

/// <summary>
/// One serialized component inside a prefab node.
/// The payload is produced by the same component serializers used for scenes.
/// </summary>
public class PrefabComponentData
{
    /// <summary>
    /// Component type ID as registered with the scene serializer
    /// </summary>
    [JsonPropertyName("typeId")]
    public int TypeId { get; set; }

    /// <summary>
    /// Component type name (informational, for readability of the file)
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>
    /// Binary component payload (base64 in JSON)
    /// </summary>
    [JsonPropertyName("data")]
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

/// <summary>
/// One entity inside a prefab
/// </summary>
public class PrefabNode
{
    /// <summary>
    /// File-local ID of this node (positive, unique within the prefab).
    /// Entity references stored inside component data refer to these IDs.
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// ID of the parent node, or 0 for the root
    /// </summary>
    [JsonPropertyName("parent")]
    public int Parent { get; set; }

    /// <summary>
    /// Serialized components of this entity
    /// </summary>
    [JsonPropertyName("components")]
    public List<PrefabComponentData> Components { get; set; } = new();
}

/// <summary>
/// A reusable entity template: a root entity plus its child hierarchy, stored as JSON.
/// Create one with <see cref="Serialization.PrefabUtility.CreateFromEntity"/> and spawn it
/// with <see cref="Serialization.PrefabUtility.Instantiate"/>.
/// </summary>
public class Prefab
{
    /// <summary>
    /// Value of the "format" field identifying a prefab file
    /// </summary>
    public const string FormatName = "RedHolePrefab";

    /// <summary>
    /// Current prefab file format version
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// File extension used for prefab files (matches the editor's asset browser)
    /// </summary>
    public const string FileExtension = ".prefab";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    [JsonPropertyName("format")]
    public string Format { get; set; } = "";

    [JsonPropertyName("version")]
    public int Version { get; set; }

    /// <summary>
    /// Display name of the prefab
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "Prefab";

    /// <summary>
    /// All entities in the prefab. The first node is the root; parents precede their children.
    /// </summary>
    [JsonPropertyName("nodes")]
    public List<PrefabNode> Nodes { get; set; } = new();

    /// <summary>
    /// Create an empty prefab in the current format version
    /// </summary>
    public static Prefab CreateEmpty(string name)
    {
        return new Prefab { Format = FormatName, Version = CurrentVersion, Name = name };
    }

    #region JSON

    /// <summary>
    /// Serialize this prefab to JSON
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Parse a prefab from JSON.
    /// Throws <see cref="InvalidDataException"/> if the text is not a valid prefab.
    /// </summary>
    public static Prefab FromJson(string json)
    {
        Prefab? prefab;
        try
        {
            prefab = JsonSerializer.Deserialize<Prefab>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid prefab JSON: {ex.Message}", ex);
        }

        if (prefab == null)
            throw new InvalidDataException("Invalid prefab: document is empty");

        prefab.Validate();
        return prefab;
    }

    /// <summary>
    /// Check structural integrity (format tag, version, node IDs, hierarchy).
    /// Throws <see cref="InvalidDataException"/> on the first problem found.
    /// </summary>
    public void Validate()
    {
        if (Format != FormatName)
            throw new InvalidDataException($"Invalid prefab: expected format '{FormatName}'");

        if (Version < 1 || Version > CurrentVersion)
            throw new InvalidDataException($"Prefab version {Version} is not supported (current version {CurrentVersion})");

        if (Nodes == null || Nodes.Count == 0)
            throw new InvalidDataException("Invalid prefab: no nodes");

        // Parents must precede children, which also rules out cycles
        var seen = new HashSet<int>();
        for (int i = 0; i < Nodes.Count; i++)
        {
            var node = Nodes[i];
            if (node == null)
                throw new InvalidDataException($"Invalid prefab: node {i} is null");

            if (node.Id <= 0)
                throw new InvalidDataException($"Invalid prefab: node {i} has non-positive id {node.Id}");

            if (i == 0)
            {
                if (node.Parent != 0)
                    throw new InvalidDataException("Invalid prefab: first node must be the root (parent 0)");
            }
            else if (!seen.Contains(node.Parent))
            {
                throw new InvalidDataException($"Invalid prefab: node {node.Id} has unknown or out-of-order parent {node.Parent}");
            }

            if (!seen.Add(node.Id))
                throw new InvalidDataException($"Invalid prefab: duplicate node id {node.Id}");

            if (node.Components == null || node.Components.Any(c => c == null || c.Data == null))
                throw new InvalidDataException($"Invalid prefab: node {node.Id} has missing component data");
        }
    }

    #endregion

    #region Files

    /// <summary>
    /// Save this prefab to a file (creates the directory if needed)
    /// </summary>
    public void SaveToFile(string filePath)
    {
        Validate();

        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(filePath, ToJson());
    }

    /// <summary>
    /// Load a prefab from a file.
    /// Throws <see cref="FileNotFoundException"/> if missing, <see cref="InvalidDataException"/> if malformed.
    /// </summary>
    public static Prefab LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Prefab file not found: {filePath}", filePath);

        return FromJson(File.ReadAllText(filePath));
    }

    /// <summary>
    /// Load a prefab from a file without throwing on a missing or malformed file
    /// </summary>
    public static bool TryLoadFromFile(string filePath, out Prefab? prefab, out string error)
    {
        try
        {
            prefab = LoadFromFile(filePath);
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            prefab = null;
            error = ex.Message;
            return false;
        }
    }

    #endregion
}

/// <summary>
/// Loads prefab files through the <see cref="ResourceManager"/> (cached by path).
/// Returns null for missing or malformed files, like the other loaders.
/// </summary>
public class PrefabLoader : ResourceLoader<Prefab>
{
    public override Prefab? Load(string path)
    {
        if (Prefab.TryLoadFromFile(path, out var prefab, out var error))
            return prefab;

        Console.WriteLine($"Failed to load prefab '{path}': {error}");
        return null;
    }
}
