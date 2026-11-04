# AI behavior tree, director, and perception

This is the decision architecture for stalker-style enemies. It is the skeleton (tree, memory, senses, pacing), not a finished creature: there is no navigation, animation, or attack trace here. A game reads `BlackboardKeys.Intent` and moves the body.

The shape follows the public accounts of the Alien in *Alien: Isolation*: a large behavior tree that only knows what it has sensed, behaviors that unlock over time, and a separate director that knows where the player is and spends a menace gauge so the creature is not on top of the player forever.

## Two brains

| Piece | Knows the player's position? | Writes |
| --- | --- | --- |
| Perception | Only if the player is inside the view cone or a noise reaches the agent | Sensory memory on the blackboard |
| Behavior tree | No. It reads the blackboard | `agent.intent` and an intent position |
| Director | Yes. That is its job | Menace, phase, and a **hint** |

A hint is a search area or a withdraw point. It is not permission to attack. The sample hunt branch runs only when `sense.hasVisualContact` is true, and the director never writes that key. Tests lock this in.

```
World.Update
  PerceptionSystem     priority 100   view cone + noise board -> blackboard
  DirectorSystem       priority 150   menace gauge + periodic hint -> blackboard
  BehaviorTreeSystem   priority 200   tree reads the blackboard, writes intent
```

Lower priority runs first. Keep that order if you replace a system.

## Behavior tree

Namespace: `RedHoleEngine.AI.BehaviorTrees` (plural, so the namespace does not hide the `BehaviorTree` type).

Every node returns `Success`, `Failure`, or `Running`.

- **Selector** tries children in order until one does not fail. The default is reactive: every tick starts at the highest-priority child, so retreat can preempt a hunt. A non-reactive selector stays on a running child until it finishes.
- **Sequence** runs children in order and fails fast. With memory (the default) it resumes a running child instead of repeating earlier steps.
- **Parallel** ticks every child. `RequireOne` / `RequireAll` say how many must succeed or fail. If both policies are met on the same tick, failure wins. A child that already finished is latched so a one-shot action does not fire again while a sibling is still running.
- **Inverter** swaps success and failure.
- **Cooldown** refuses to tick its child again for `seconds` after success (or after failure, if you ask). The timer survives preemption (`Abort`) and clears on a full `Reset`. A tick that finishes the timer still returns failure; the child is free on the next tick.
- **Repeat** runs the child again after each success. A positive count completes after that many successes. A negative count stops only when the child fails.
- **Condition guard** ticks its child only while a predicate is true, and aborts the child when the predicate becomes false. Use this around a long action so a sequence that is parked on that action still notices the condition.
- **Condition** and **action** are leaves. Actions registered for JSON must be stateless. Put per-agent progress on the blackboard.

A node may carry a **gate id**. While that id is locked on the agent's `BehaviorGateSet`, the node fails and does not tick its children. An id that was never defined is open, so trees keep working before anyone calls `Define`.

```csharp
gates.Lock("ambush");   // tactic exists but is not learned yet
gates.Unlock("ambush"); // player hid in lockers often enough, or a script says so
```

### Instances and assets

Node objects remember which child is running and how much cooldown is left. **One `BehaviorTree` instance per agent.** The shareable asset is a `BehaviorTreeDocument` (JSON). `Compile` stamps out a fresh tree.

Build from code:

```csharp
var tree = new BehaviorTreeBuilder("demo", registry)
    .Selector("root")
        .Sequence("hunt").Gate("hunt")
            .Guard("sees", "demo.seesPlayer")
                .Action("chase", "demo.chase")
            .End()
        .End()
        .Action("patrol", "demo.patrol")
    .End()
    .Build();
```

`End()` closes the innermost composite or decorator. `Gate()` applies to the node just created.

Build from JSON (leaf names come from a `BehaviorRegistry`):

```json
{
  "name": "demo",
  "root": {
    "type": "selector",
    "name": "root",
    "children": [
      {
        "type": "sequence",
        "name": "hunt",
        "gate": "hunt",
        "children": [
          {
            "type": "guard",
            "name": "sees",
            "condition": "demo.seesPlayer",
            "child": { "type": "action", "name": "chase", "action": "demo.chase" }
          }
        ]
      },
      { "type": "action", "name": "patrol", "action": "demo.patrol" }
    ]
  }
}
```

Built-in `type` values: `selector`, `sequence`, `parallel`, `inverter`, `cooldown`, `repeat`, `guard`, `condition`, `action`.

`selector` accepts `reactive` (default true). `sequence` accepts `memory` (default true). `parallel` accepts `successPolicy` and `failurePolicy` (`requireOne`, `requireAll`). `cooldown` requires `seconds`. `repeat` requires `count` (`-1` means until failure). Decorators take `child` (or a one-element `children` array). Composites take `children`.

`BehaviorTreeDocument.FromTree` exports a tree whose leaves were created with registry ids. A lambda leaf has nothing to write and throws.

`RecordTrace = true` on the tree (or `BehaviorAgentComponent.RecordTrace`) fills `LastTrace` / `LastTrace` text. `BehaviorDebug.Format` prints the shape and the last status. Both are plain strings, so they work in a headless test.

### ECS

```csharp
var agent = world.CreateEntity();
world.AddComponent(agent, BehaviorAgentComponent.Create(tree, gates, recordTrace: false));
world.AddSystem(new BehaviorTreeSystem());
```

`BehaviorTreeSystem` ticks every enabled agent. The component holds the tree, the blackboard, and the gates. Do not default-initialize the struct and expect a tree; use `Create`.

`BehaviorContext` carries the world when the system ticks, and can be constructed with only a blackboard in tests.

## Perception

`SensoryComponent` is the eyes and ears.

- Sight is a cone: `ViewDistance` and `ViewAngleDegrees`, from the transform (or `Position` when there is no transform). A non-zero `Facing` overrides transform forward. The engine's transform forward is used when facing is left at zero.
- Hearing treats `NoiseEvent.Intensity` as a radius in world units at `HearingSensitivity` 1. `PerceptionSystem.EmitNoise` drops a sound on a board that lives for `NoiseLifetime` seconds. The loudest in-range noise the agent has not already consumed wins.
- `IVisionQuery` is the occlusion hook. Leave it null in tests (the cone is enough). A real game can raycast and return `IsOccluded`.

Results are written onto the component and, when the entity has a `BehaviorAgentComponent`, onto the blackboard:

| Key | Meaning |
| --- | --- |
| `sense.hasVisualContact` | Saw the stimulus this tick |
| `sense.lastSeenPosition` | Where it was when seen |
| `sense.lastKnownPosition` | Best place to search (sight, else a fresh noise) |
| `sense.heardNoise` / `sense.noiseId` / `sense.noisePosition` | A not-yet-consumed noise |
| `sense.consumedNoiseId` | Set by the investigate action so the same sound does not loop |
| `sense.searchActive` / `sense.searchTimeRemaining` | Sensory search memory |

The thing being looked at is `SensoryComponent.StimulusSource`, or the first `DirectorSubjectComponent` in the world when the source is null.

## Director

`DirectorSubjectComponent` marks the player (or whoever the encounter is about). `DirectorStalkerComponent` marks agents the director may steer. One gauge is shared by every stalker; the stalker with visual contact drives it, otherwise the nearest.

While **frontstage** and inside `NearRadius`, menace rises. Real visual contact adds `ContactRisePerSecond`. Outside that radius it falls slowly. Crossing `BackstageThreshold` (default 0.80) enters **backstage**, and menace then falls even if the stalker is still in the player's face (`BackstageFallPerSecond`). It returns to frontstage only at or below `FrontstageReenterThreshold` (default 0.35), so the gauge does not flicker.

Hints refresh on `HintInterval` (default 3s), on a phase change, and when the subject appears or disappears. Between refreshes the previous order stands, which is what stops the director from puppeting the agent every frame.

- Frontstage hint: `SearchArea`, centered near the subject but shifted sideways by a quarter of `SearchHintRadius`. It is not the subject's coordinates.
- Backstage hint: `Withdraw`, placed `WithdrawDistance` beyond the stalker, away from the subject.
- No subject: `None`.

`MenaceModel` is the pure function behind the system. Call it from tests without a world. `DirectorSystem.SetMenace` is the scripting hook (save/load, a set piece).

Blackboard keys: `director.menace`, `director.phase`, `director.hintKind`, `director.hintPosition`, `director.hintRadius`.

## Sample stalker

`RedHoleEngine.AI.Sample.StalkerTree` is a small priority list:

1. **Retreat** when the phase is backstage or menace is at least `StalkerTuning.RetreatMenace` (0.80).
2. **Ambush** when the `ambush` gate is unlocked, `agent.preferAmbush` is set, and the agent can see the target. The gate starts locked.
3. **Hunt** on visual contact. Gate `hunt` starts unlocked; lock it and the agent searches instead of committing.
4. **Investigate** a fresh noise for `StalkerTuning.InvestigateSeconds`, then remembers that spot.
5. **Search** the last known sensory position for `SearchMemorySeconds`, or the director's search area if there is nothing sensory to go on.
6. **Patrol** otherwise.

`StalkerTree.CreateAgent()` builds a new tree, the default gate set, and a blackboard. `StalkerTree.CreateJson()` is the same tree as data; compile it with `StalkerBehaviors.SharedRegistry`.

Intent values live in `AgentIntent`: `Patrol`, `Investigate`, `Search`, `Hunt`, `Ambush`, `Retreat`. `agent.intentPosition` is where that intent is aimed, when it has a place.

## Extending it

1. Add a stateless condition or action and register it (`registry.RegisterCondition("pack.flank", FlankReady)`). Keep timers and targets on the blackboard, keyed with your own strings or a new constant next to `BlackboardKeys`.
2. Drop the leaf into the sample with the fluent builder, or add it to JSON and recompile per agent.
3. Put a `gate` on the new branch and `Lock` it in the gate set you pass to `BehaviorAgentComponent.Create`. Unlock it from gameplay (the player used a tactic, a story flag, a director beat).
4. If the branch should preempt older ones, place it earlier in the reactive selector. If it should only run while some fact is true, wrap the action in a condition guard rather than a bare condition in front of a long action: a sequence with memory will not re-check a condition it has already passed.
5. Custom composite types are code. The JSON compiler only knows the built-in node set; new decisions should be new registered leaves, not new JSON types, unless you extend `BehaviorTreeDocument.Compile`.
6. Line of sight: assign `PerceptionSystem.Vision`. Do not bake a physics query into the cone test; `SensoryMath` stays pure so the tests do not need a world collider.

Movement, attack traces, and "which locker to check" are deliberately not in this layer. When those exist, implement them as actions that return `Running` until the body arrives and `Success` or `Failure` when it does, reading `agent.intentPosition`.
