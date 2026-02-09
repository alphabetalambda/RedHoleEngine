namespace RedHoleEngine.Game;

public interface IGameModule
{
    string Name { get; }
    void BuildScene(GameContext context);
}
