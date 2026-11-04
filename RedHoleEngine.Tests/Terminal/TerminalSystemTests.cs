using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;
using RedHoleEngine.Terminal;

namespace RedHoleEngine.Tests.Terminal;

public class TerminalSystemTests
{
    [Fact]
    public void Update_CreatesIndependentSessionsAndExecuteBeforeInitThrows()
    {
        using var world = new World();
        var system = world.AddSystem(new TerminalSystem());
        var first = world.CreateEntity();
        var second = world.CreateEntity();
        world.AddComponent(first, new TerminalComponent("one", autoLoad: false));
        world.AddComponent(second, new TerminalComponent("two", autoLoad: false));

        Assert.Null(system.GetSession(first));
        Assert.Throws<InvalidOperationException>(() => system.Execute(first, "pwd"));

        world.Update(0.016f);
        world.Update(0.016f);

        Assert.NotNull(system.GetSession(first));
        Assert.NotSame(system.GetSession(first), system.GetSession(second));
        Assert.Equal("/" + Environment.NewLine, system.Execute(first, "pwd").Output);
        Assert.Equal(0, system.Execute(second, "echo secret > /tmp/secret.txt").ExitCode);
        Assert.Contains("secret", system.Execute(second, "cat /tmp/secret.txt").Output);
        Assert.Contains("File not found", system.Execute(first, "cat /tmp/secret.txt").Output);
    }

    [Fact]
    public void SaveAndLoad_RoundTripThroughSaveManager()
    {
        using var world = new World();
        var saves = new GameSaveManager("terminal-" + Guid.NewGuid().ToString("N"));
        try
        {
            var system = world.AddSystem(new TerminalSystem());
            system.SetSaveManager(saves);
            var entity = world.CreateEntity();
            world.AddComponent(entity, new TerminalComponent("slot", autoLoad: false));
            world.Update(0.016f);

            Assert.Equal(0, system.Execute(entity, "echo stored > /tmp/note.txt").ExitCode);
            system.Save(entity);

            Assert.Equal(0, system.Execute(entity, "rm /tmp/note.txt").ExitCode);
            system.Load(entity);

            Assert.Equal("/", system.GetSession(entity)!.CurrentDirectory);
            Assert.Equal("stored" + Environment.NewLine + Environment.NewLine, system.Execute(entity, "cat /tmp/note.txt").Output);
        }
        finally
        {
            DeleteGame(saves);
        }
    }

    [Fact]
    public void AutoLoad_RestoresSlotWhenSessionStarts()
    {
        var saves = new GameSaveManager("terminal-auto-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var world = new World();
            var system = world.AddSystem(new TerminalSystem(saves));
            var writer = world.CreateEntity();
            world.AddComponent(writer, new TerminalComponent("slot", autoLoad: false));
            world.Update(0.016f);
            system.Execute(writer, "echo ready > /etc/motd");
            system.Save(writer);

            var reader = world.CreateEntity();
            world.AddComponent(reader, new TerminalComponent("slot", autoLoad: true));
            world.Update(0.016f);

            Assert.Equal("ready" + Environment.NewLine + Environment.NewLine, system.Execute(reader, "cat /etc/motd").Output);

            var skipped = world.CreateEntity();
            world.AddComponent(skipped, new TerminalComponent("slot", autoLoad: false));
            world.Update(0.016f);
            Assert.Contains("File not found", system.Execute(skipped, "cat /etc/motd").Output);
        }
        finally
        {
            DeleteGame(saves);
        }
    }

    [Fact]
    public void SaveAndLoad_WithoutManagerOrSession_DoNothing()
    {
        using var world = new World();
        var system = world.AddSystem(new TerminalSystem());
        var entity = world.CreateEntity();
        world.AddComponent(entity, new TerminalComponent("slot", autoLoad: true));

        system.Save(entity);
        system.Load(entity);
        world.Update(0.016f);
        system.Save(new Entity(90, 1));
        system.Load(new Entity(90, 1));

        Assert.Equal(string.Empty, system.Execute(entity, "ls /etc").Output);
    }

    [Fact]
    public void Update_DestroyedEntity_KeepsSessionForRecycledId()
    {
        using var world = new World();
        var system = world.AddSystem(new TerminalSystem());
        var first = world.CreateEntity();
        world.AddComponent(first, new TerminalComponent("slot", autoLoad: false));
        world.Update(0.016f);
        system.Execute(first, "mkdir /home/user/kept");
        var session = system.GetSession(first);

        world.DestroyEntity(first);
        world.Update(0.016f);

        Assert.Same(session, system.GetSession(first));

        var second = world.CreateEntity();
        Assert.Equal(first.Id, second.Id);
        world.AddComponent(second, new TerminalComponent("slot", autoLoad: false));
        world.Update(0.016f);

        Assert.Same(session, system.GetSession(second));
        Assert.Contains("kept/", system.Execute(second, "ls /home/user").Output);
    }

    [Fact(Skip = "BUG: expected destroying a terminal entity to drop its session, actual TryGetEntity stays true for a freed id so the recycled entity reuses the old filesystem")]
    public void Update_DestroyedEntity_DropsSession()
    {
        using var world = new World();
        var system = world.AddSystem(new TerminalSystem());
        var first = world.CreateEntity();
        world.AddComponent(first, new TerminalComponent("slot", autoLoad: false));
        world.Update(0.016f);
        system.Execute(first, "mkdir /home/user/kept");

        world.DestroyEntity(first);
        world.Update(0.016f);
        Assert.Null(system.GetSession(first));

        var second = world.CreateEntity();
        world.AddComponent(second, new TerminalComponent("slot", autoLoad: false));
        world.Update(0.016f);

        Assert.DoesNotContain("kept/", system.Execute(second, "ls /home/user").Output);
    }

    private static void DeleteGame(GameSaveManager manager)
    {
        var game = Path.GetDirectoryName(Path.GetDirectoryName(manager.GetSavePath("default")));
        if (game != null && Directory.Exists(game))
            Directory.Delete(game, true);
    }
}
