using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.Components;

public struct TerminalComponent : IComponent
{
    public string SaveSlot;
    public bool AutoLoad;

    public TerminalComponent(string saveSlot = "default", bool autoLoad = true)
    {
        SaveSlot = saveSlot;
        AutoLoad = autoLoad;
    }
}
