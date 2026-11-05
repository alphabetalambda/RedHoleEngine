using System.Numerics;
using RedHoleEngine.AI.BehaviorTrees;
using RedHoleEngine.AI.Perception;
using RedHoleEngine.Components;
using RedHoleEngine.Core.ECS;

namespace RedHoleEngine.AI.Director;

/// <summary>
/// Encounter pacing. Tracks one menace gauge for the directed stalkers, then writes
/// a phase and a hint onto each stalker's blackboard.
/// The hint is a search area or a withdraw point. This system never writes
/// <see cref="BlackboardKeys.HasVisualContact"/>; seeing the subject is the sensor's job.
/// Runs after perception and before the behavior tree.
/// </summary>
public class DirectorSystem : GameSystem
{
    private readonly List<StalkerSnapshot> _stalkers = new();
    private bool _refreshHints = true;
    private bool _hadSubject;
    private float _hintCooldown;

    public override int Priority => 150;

    public DirectorConfig Config { get; } = new();

    public float Menace { get; private set; }

    public DirectorPhase Phase { get; private set; } = DirectorPhase.Frontstage;

    /// <summary>The stalker currently driving the gauge: visual contact first, otherwise the nearest.</summary>
    public Entity FocusStalker { get; private set; } = Entity.Null;

    /// <summary>
    /// Scripting and tests can drop the gauge on a value. Phase is re-evaluated immediately;
    /// blackboards pick the change up on the next update.
    /// </summary>
    public void SetMenace(float menace)
    {
        Menace = Math.Clamp(menace, 0f, 1f);
        var next = MenaceModel.NextPhase(Phase, Menace, Config);
        if (next != Phase)
        {
            Phase = next;
            _refreshHints = true;
        }
    }

    public override void Update(float deltaTime)
    {
        bool hasSubject = TryFindSubject(out var subjectPosition);
        if (hasSubject != _hadSubject)
        {
            _hadSubject = hasSubject;
            _refreshHints = true;
        }

        CollectStalkers(hasSubject, subjectPosition);
        var stimulus = ChooseStimulus();
        Menace = MenaceModel.Step(Menace, deltaTime, stimulus, Config);

        var next = MenaceModel.NextPhase(Phase, Menace, Config);
        if (next != Phase)
        {
            Phase = next;
            _refreshHints = true;
        }

        _hintCooldown -= deltaTime;
        if (_hintCooldown <= 0f)
            _refreshHints = true;

        for (int i = 0; i < _stalkers.Count; i++)
            Apply(_stalkers[i], hasSubject, subjectPosition);

        if (_refreshHints)
            _hintCooldown = Config.HintInterval;
        _refreshHints = false;
    }

    private void CollectStalkers(bool hasSubject, Vector3 subjectPosition)
    {
        _stalkers.Clear();
        foreach (var entity in World.Query<DirectorStalkerComponent>())
        {
            if (!TryGetPosition(entity, out var position))
                continue;

            bool visual = World.TryGetComponent<SensoryComponent>(entity, out var sensor) && sensor.HasVisualContact;
            float distance = hasSubject ? Vector3.Distance(position, subjectPosition) : float.PositiveInfinity;
            _stalkers.Add(new StalkerSnapshot(entity, position, distance, visual));
        }
    }

    private MenaceStimulus ChooseStimulus()
    {
        if (_stalkers.Count == 0 || !_hadSubject)
        {
            FocusStalker = Entity.Null;
            return new MenaceStimulus(false, float.PositiveInfinity, false, Phase == DirectorPhase.Backstage);
        }

        int best = 0;
        for (int i = 1; i < _stalkers.Count; i++)
        {
            if (IsMoreOnStage(_stalkers[i], _stalkers[best]))
                best = i;
        }

        var chosen = _stalkers[best];
        FocusStalker = chosen.Entity;
        return new MenaceStimulus(true, chosen.Distance, chosen.VisualContact, Phase == DirectorPhase.Backstage);
    }

    private static bool IsMoreOnStage(in StalkerSnapshot candidate, in StalkerSnapshot current)
    {
        if (candidate.VisualContact != current.VisualContact)
            return candidate.VisualContact;
        return candidate.Distance < current.Distance;
    }

    private void Apply(in StalkerSnapshot stalker, bool hasSubject, Vector3 subjectPosition)
    {
        ref var tag = ref World.GetComponent<DirectorStalkerComponent>(stalker.Entity);
        if (_refreshHints || !tag.HasHint)
        {
            if (!hasSubject)
            {
                tag.HintKind = DirectorHintKind.None;
                tag.HintPosition = stalker.Position;
                tag.HintRadius = 0f;
            }
            else if (Phase == DirectorPhase.Backstage)
            {
                tag.HintKind = DirectorHintKind.Withdraw;
                tag.HintPosition = MenaceModel.WithdrawPoint(stalker.Position, subjectPosition, Config.WithdrawDistance);
                tag.HintRadius = 0f;
            }
            else
            {
                tag.HintKind = DirectorHintKind.SearchArea;
                tag.HintPosition = MenaceModel.SearchAreaCenter(subjectPosition, stalker.Position, Config.SearchHintRadius);
                tag.HintRadius = Config.SearchHintRadius;
            }

            tag.HasHint = true;
        }

        if (!World.HasComponent<BehaviorAgentComponent>(stalker.Entity))
            return;

        ref var agent = ref World.GetComponent<BehaviorAgentComponent>(stalker.Entity);
        var board = agent.Blackboard ?? new Blackboard();
        agent.Blackboard = board;
        board.Set(BlackboardKeys.Menace, Menace);
        board.Set(BlackboardKeys.DirectorPhase, Phase);
        board.Set(BlackboardKeys.HintKind, tag.HintKind);
        board.Set(BlackboardKeys.HintPosition, tag.HintPosition);
        board.Set(BlackboardKeys.HintRadius, tag.HintRadius);
    }

    private bool TryFindSubject(out Vector3 position)
    {
        foreach (var entity in World.Query<DirectorSubjectComponent>())
        {
            if (TryGetPosition(entity, out position))
                return true;
        }

        position = default;
        return false;
    }

    private bool TryGetPosition(Entity entity, out Vector3 position)
    {
        if (World.TryGetComponent<TransformComponent>(entity, out var transform))
        {
            position = transform.Position;
            return true;
        }

        if (World.TryGetComponent<SensoryComponent>(entity, out var sensor))
        {
            position = sensor.Position;
            return true;
        }

        position = default;
        return false;
    }

    private readonly record struct StalkerSnapshot(Entity Entity, Vector3 Position, float Distance, bool VisualContact);
}
