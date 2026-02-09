using System.Linq;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;
using RedHoleEngine.Game;

namespace RedHoleEngine.Terminal;

public sealed class TerminalSystem : GameSystem
{
    private readonly Dictionary<int, TerminalSession> _sessions = new();
    private GameSaveManager? _saveManager;

    public TerminalSystem(GameSaveManager? saveManager = null)
    {
        _saveManager = saveManager;
    }

    public void SetSaveManager(GameSaveManager saveManager)
    {
        _saveManager = saveManager;
    }

    public override void Update(float deltaTime)
    {
        if (World == null)
            return;

        foreach (var entity in World.Query<TerminalComponent>())
        {
            if (_sessions.ContainsKey(entity.Id))
                continue;

            var fs = new VirtualFileSystem();
            var session = new TerminalSession(fs);
            _sessions[entity.Id] = session;

            ref var component = ref World.GetComponent<TerminalComponent>(entity);
            if (component.AutoLoad)
            {
                Load(entity);
            }
        }

        if (_sessions.Count == 0)
            return;

        var toRemove = _sessions.Keys.Where(id => !World.TryGetEntity(id, out _)).ToList();
        foreach (var id in toRemove)
        {
            _sessions.Remove(id);
        }
    }

    public TerminalSession? GetSession(Entity entity)
    {
        return _sessions.TryGetValue(entity.Id, out var session) ? session : null;
    }

    public TerminalCommandResult Execute(Entity entity, string commandLine)
    {
        if (!_sessions.TryGetValue(entity.Id, out var session))
            throw new InvalidOperationException("Terminal session not initialized");

        return session.Execute(commandLine);
    }

    public void Save(Entity entity)
    {
        if (_saveManager == null)
            return;

        if (!_sessions.TryGetValue(entity.Id, out var session))
            return;

        ref var component = ref World!.GetComponent<TerminalComponent>(entity);
        var save = new GameSaveData { VirtualFileSystem = session.SaveState() };
        _saveManager.Save(component.SaveSlot, save);
    }

    public void Load(Entity entity)
    {
        if (_saveManager == null)
            return;

        if (!_sessions.TryGetValue(entity.Id, out var session))
            return;

        ref var component = ref World!.GetComponent<TerminalComponent>(entity);
        var save = _saveManager.Load(component.SaveSlot);
        session.LoadState(save?.VirtualFileSystem);
    }
}
