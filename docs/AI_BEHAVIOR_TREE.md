# AI behavior tree, director, and perception

This is the decision architecture for stalker-style enemies. It is the skeleton (tree, memory, senses, pacing, and getting from A to B), not a finished creature: there is no animation or attack trace here. The tree writes `BlackboardKeys.Intent` and an intent position; the [navigation](#navigation) module walks the body there.

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
  NavigationSystem     priority 250   plans paths and moves agents that have a destination
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

## Navigation

Namespace: `RedHoleEngine.AI.Navigation`. Three layers: a grid that says where an agent can stand, a pathfinder over it, and a system that walks agents along the result.

### Grid

`NavGrid` is 2.5D: a uniform grid on the XZ plane where each cell is walkable or blocked and has one ground height. That matches what the engine can collide with (planes, boxes, spheres, capsules; no triangle meshes to build a navmesh from) and keeps path queries free of allocation. Ramps and steps work through `MaxStepHeight`, the largest height difference an agent may cross between neighbouring cells. Floors stacked above each other do not fit in one grid; use one grid per storey.

Build it by hand:

```csharp
var grid = new NavGrid(origin: new Vector3(-20f, 0f, -20f), width: 80, depth: 80, cellSize: 0.5f);
grid.SetWalkable(new Vector3(-2f, 0f, -20f), new Vector3(2f, 0f, 10f), walkable: false); // a wall
```

Or bake it from physics colliders:

```csharp
// After at least one world update: PhysicsSystem registers colliders when it first sees their entity.
var grid = NavGridBaker.Bake(physicsSystem.PhysicsWorld, min, max, cellSize: 0.5f,
    new NavBakeSettings { AgentRadius = 0.4f, AgentHeight = 1.8f, MaxStepHeight = 0.4f });
```

For each cell the baker casts a ray down to find the ground (the highest surface under the cell center), rejects ground steeper than `MaxSlopeDegrees`, then stacks overlap spheres of `AgentRadius + Padding` over that point to check the agent fits. Static bodies count by default; `IncludeKinematic`, `IncludeDynamic` and `LayerMask` change that. Triggers never count.

Things to know:

- Clearance is tested at the cell center, so a smoothed path can pass up to half a cell closer to an obstacle than `AgentRadius`. Keep cells no larger than the agent, or add `Padding`.
- The top of an obstacle is baked as a raised cell. It reads as walkable but is cut off from the floor by `MaxStepHeight`; ask `grid.IsReachable` or the pathfinder, not `IsWalkable`, whether an agent can get there.
- Rays start `ScanHeight` above the grid origin. In a room with a ceiling collider, set it below the ceiling.
- Capsule colliders block cells but are never ground (the physics raycast skips them).
- Every edit bumps `grid.Version`. Moving agents replan when it changes, so opening a door is `grid.SetWalkable(...)`.

### Paths

`NavPathfinder.FindPath(grid, start, goal, path)` runs A* (8 neighbours, no cutting across the corner of a blocked cell) and then pulls the string tight: from each kept point it jumps to the furthest later point in line of sight. The result starts at `start` and ends at `goal`.

- A start or goal on a blocked cell is snapped to the nearest walkable cell within `SnapDistance` (default 2). Further than that is `InvalidStart` / `InvalidGoal`.
- The grid labels connected regions once per version, so an unreachable goal returns `Unreachable` without a search.
- Buffers are reused. After warm-up a query allocates nothing. One pathfinder per thread.

### Agents

```csharp
var navigation = world.AddSystem(new NavigationSystem { Grid = grid });

world.AddComponent(entity, new TransformComponent(spawn));
world.AddComponent(entity, NavAgentComponent.Create(speed: 3.5f, acceleration: 20f, stoppingDistance: 0.1f, radius: 0.4f));

bool reachable = navigation.SetDestination(entity, target); // plans now
// or: world.GetComponent<NavAgentComponent>(entity).SetDestination(target); // plans on the next update
```

`NavAgentComponent.Status` goes `Pending` -> `Moving` -> `Arrived`, or `Unreachable`. `Stop()` drops the order and the agent brakes.

Steering is deliberately simple: accelerate along the path, brake so the agent stops at the goal instead of overshooting ("arrive"), and push away from agents whose radii overlap. Separation compares every pair of agents; set `NavigationSystem.EnableSeparation = false` for a crowd, or `SeparationWeight = 0` on one agent. With a low `Acceleration` relative to `Speed` an agent swings wide on corners, so keep it high for tight corridors.

How the body moves depends on the entity:

| Entity has | Moved by |
| --- | --- |
| No rigid body, or a kinematic/static one | The transform is moved directly (and `PhysicsSystem` copies it onto a kinematic body). Y follows the grid height plus `BaseOffset` when `SnapToGround` is set, and the transform turns to face its heading when `FaceMovement` is set. |
| A dynamic rigid body | Only the horizontal velocity of the body is set. Physics moves it, gravity owns Y, and collisions still apply. |

### Behavior-tree leaves

Two forms of the same leaves. Both return `Running` on the way, `Success` on arrival, and `Failure` when the tree is ticked without a world, the agent has no `NavAgentComponent`, or there is no path (reported on the first tick when a `NavigationSystem` is present).

Node classes, for trees built in code. They also cancel their own order when the branch is preempted, so the agent does not keep walking somewhere the tree stopped caring about:

```csharp
using RedHoleEngine.AI.Navigation;

var tree = new BehaviorTreeBuilder("guard")
    .Selector("root")
        .Guard("scared", ctx => ctx.Blackboard.GetOr("scared", false))
            .Flee("run", BlackboardKeys.LastSeenPosition, fleeDistance: 8f, safeDistance: 12f)
        .End()
        .Guard("has-lead", ctx => ctx.Blackboard.Has(BlackboardKeys.LastKnownPosition))
            .MoveTo("check-it-out", BlackboardKeys.LastKnownPosition)
        .End()
        .Patrol("rounds", pointA, pointB, pointC)
    .End()
    .Build();
```

| Node | Builder | Behaviour |
| --- | --- | --- |
| `MoveToNode` | `.MoveTo(name, key)` / `.MoveTo(name, position)` | Walks to a blackboard `Vector3` or a fixed point. Fails while the key is missing. Replans when the target moves more than 0.5. |
| `FleeNode` | `.Flee(name, threatKey, fleeDistance, safeDistance)` | Picks reachable points away from the threat. Succeeds once `safeDistance` away, fails when cornered. |
| `PatrolNode` | `.Patrol(name, points...)` | One point per success, so put it under `Repeat` (and `Cooldown` to pause at each stop). An unreachable point fails and is skipped. |
| `WanderNode` | `.Wander(name, radius, center, seed)` | Random reachable point within the radius; success on arrival. |

`BehaviorTreeBuilder.Leaf(node)` adds any other custom leaf. These node types are code-only: `BehaviorTreeDocument.FromTree` cannot export them.

Stateless actions, for JSON trees. `NavigationActions.Register(registry)` adds:

| Action id | Does |
| --- | --- |
| `nav.moveToIntent` | Walks to `agent.intentPosition` |
| `nav.moveToLastKnown` | Walks to `sense.lastKnownPosition` |
| `nav.fleeLastSeen` | Flees from `sense.lastSeenPosition` |
| `nav.patrol` | Patrols the `Vector3[]` stored under `nav.patrolPoints` (index in `nav.patrolIndex`) |
| `nav.wander` | Wanders within 6 units |
| `nav.stop` | Drops the destination |

For other keys or distances, register a factory result under your own id: `registry.RegisterAction("pack.goToDen", NavigationActions.MoveToKey("pack.den"))`. The static methods (`NavigationActions.MoveTo(context, target)`, `Flee`, `Patrol`, `Wander`, `Stop`) can also be called from inside your own actions. A registered action cannot see its branch being preempted; follow it with a branch that gives a new order or calls `nav.stop`.

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

Give the stalker entity a `NavAgentComponent` (and add a `NavigationSystem` with a grid) and it walks: after each decision the sample actions call `NavigationActions.FollowIntent`, which heads for `agent.intentPosition` or stops when the intent has no place. While patrolling it walks the `Vector3[]` under `nav.patrolPoints` if you set one. The statuses the tree returns are the same with or without a nav agent, so the decision logic is unchanged.

## Extending it

1. Add a stateless condition or action and register it (`registry.RegisterCondition("pack.flank", FlankReady)`). Keep timers and targets on the blackboard, keyed with your own strings or a new constant next to `BlackboardKeys`.
2. Drop the leaf into the sample with the fluent builder, or add it to JSON and recompile per agent.
3. Put a `gate` on the new branch and `Lock` it in the gate set you pass to `BehaviorAgentComponent.Create`. Unlock it from gameplay (the player used a tactic, a story flag, a director beat).
4. If the branch should preempt older ones, place it earlier in the reactive selector. If it should only run while some fact is true, wrap the action in a condition guard rather than a bare condition in front of a long action: a sequence with memory will not re-check a condition it has already passed.
5. Custom composite types are code. The JSON compiler only knows the built-in node set; new decisions should be new registered leaves, not new JSON types, unless you extend `BehaviorTreeDocument.Compile`.
6. Line of sight: assign `PerceptionSystem.Vision`. Do not bake a physics query into the cone test; `SensoryMath` stays pure so the tests do not need a world collider.

Attack traces and "which locker to check" are deliberately not in this layer. When those exist, implement them like the navigation leaves: actions that return `Running` until the body is done and `Success` or `Failure` when it is.
