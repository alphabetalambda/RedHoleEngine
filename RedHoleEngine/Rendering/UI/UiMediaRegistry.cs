using System.Collections.Concurrent;

namespace RedHoleEngine.Rendering.UI;

public static class UiMediaRegistry
{
    private static int _nextId = 1;
    private static readonly ConcurrentDictionary<int, UiMediaEntry> _entries = new();

    public static int RegisterImage(string path)
    {
        var id = Interlocked.Increment(ref _nextId);
        _entries[id] = new UiMediaEntry(id, path, MediaType.Image);
        return id;
    }

    public static int RegisterVideo(string path, float framesPerSecond = 12f, bool loop = true)
    {
        var id = Interlocked.Increment(ref _nextId);
        _entries[id] = new UiMediaEntry(id, path, MediaType.Video)
        {
            FramesPerSecond = framesPerSecond,
            Loop = loop
        };
        return id;
    }

    public static UiMediaEntry? Get(int id)
    {
        return _entries.TryGetValue(id, out var entry) ? entry : null;
    }

    public sealed class UiMediaEntry
    {
        public UiMediaEntry(int id, string path, MediaType type)
        {
            Id = id;
            Path = path;
            Type = type;
        }

        public int Id { get; }
        public string Path { get; set; }
        public MediaType Type { get; }
        public float FramesPerSecond { get; set; } = 12f;
        public bool Loop { get; set; } = true;
    }

    public enum MediaType
    {
        Image,
        Video
    }
}
