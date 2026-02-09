using System.Text.Json;

namespace RedHoleEngine.Game;

public sealed class GameSaveManager
{
    private readonly string _basePath;

    public GameSaveManager(string gameId)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _basePath = Path.Combine(baseDir, "RedHoleEngine", gameId, "Saves");
        Directory.CreateDirectory(_basePath);
    }

    public string GetSavePath(string slot)
    {
        var safeSlot = string.IsNullOrWhiteSpace(slot) ? "default" : slot.Trim();
        return Path.Combine(_basePath, safeSlot + ".json");
    }

    public void Save(string slot, GameSaveData data)
    {
        var path = GetSavePath(slot);
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public GameSaveData? Load(string slot)
    {
        var path = GetSavePath(slot);
        if (!File.Exists(path))
            return null;

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<GameSaveData>(json);
    }
}
