using RedHoleEngine.Game;
using RedHoleEngine.Terminal;

namespace RedHoleEngine.Tests.Game;

public class GameSaveManagerTests
{
    [Fact]
    public void GetSavePath_TrimsSlotAndReplacesBlankWithDefault()
    {
        var manager = Create(out var gameDirectory);
        try
        {
            Assert.EndsWith($"{Path.DirectorySeparatorChar}alpha.json", manager.GetSavePath("  alpha  "));
            Assert.EndsWith($"{Path.DirectorySeparatorChar}default.json", manager.GetSavePath("   "));
            Assert.EndsWith($"{Path.DirectorySeparatorChar}default.json", manager.GetSavePath(null!));
            Assert.StartsWith(gameDirectory, manager.GetSavePath("alpha"));
        }
        finally
        {
            Directory.Delete(gameDirectory, true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsVirtualFileSystemAndMissingSlotReturnsNull()
    {
        var manager = Create(out var gameDirectory);
        try
        {
            Assert.Null(manager.Load("empty"));

            var data = new GameSaveData
            {
                VirtualFileSystem = new VirtualFileSystemState
                {
                    Root = new VirtualDirectoryState("/")
                    {
                        Files = { new VirtualFileState("note.txt", "hello") },
                        Directories =
                        {
                            new VirtualDirectoryState("logs")
                            {
                                Files = { new VirtualFileState("a.txt", "one") }
                            }
                        }
                    }
                }
            };

            manager.Save("slot-1", data);
            manager.Save("plain", new GameSaveData());

            var loaded = manager.Load("slot-1");
            Assert.NotNull(loaded);
            Assert.NotNull(loaded!.VirtualFileSystem);
            Assert.Equal("hello", loaded.VirtualFileSystem!.Root.Files.Single(file => file.Name == "note.txt").Contents);
            Assert.Equal("one", loaded.VirtualFileSystem.Root.Directories.Single().Files.Single().Contents);
            Assert.Null(manager.Load("plain")!.VirtualFileSystem);
            Assert.Contains("hello", File.ReadAllText(manager.GetSavePath("slot-1")));
        }
        finally
        {
            Directory.Delete(gameDirectory, true);
        }
    }

    private static GameSaveManager Create(out string gameDirectory)
    {
        var gameId = "save-tests-" + Guid.NewGuid().ToString("N");
        var manager = new GameSaveManager(gameId);
        gameDirectory = Path.GetDirectoryName(Path.GetDirectoryName(manager.GetSavePath("default")))!;
        return manager;
    }
}
