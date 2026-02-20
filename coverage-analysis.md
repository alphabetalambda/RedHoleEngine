# Coverage Report Analysis - RedHoleEngine

**Generated:** February 16, 2026  
**Tool:** DotCover 2025.3.2

---

## Overall Summary

| Metric | Value |
|--------|-------|
| **Total Coverage** | 22% |
| **Covered Statements** | 6,191 |
| **Total Statements** | 28,539 |
| **RedHoleEngine Project** | 19% (3,030 / 16,319) |

---

## Critical Weak Areas (0% Coverage)

### 1. Audio Namespace - 6% Coverage (101/1,801 statements)

This is one of the weakest areas with most classes completely untested:

| Class | Statements | Coverage |
|-------|------------|----------|
| `OpenALBackend` | 355 | 0% |
| `AudioDebugVisualizer` | 292 | 0% |
| `AcousticRaytracer` | 269 | 0% |
| `AudioEngine` | 232 | 0% |
| `CollisionAudioSystem` | 202 | 9% |
| `AudioSystem` | 150 | 0% |
| `AudioMixer` | 88 | 0% |
| `FrequencyResponse` | 40 | 0% |
| `AudioExtensions` | 17 | 0% |
| `AcousticQualitySettings` | 13 | 0% |
| `AcousticMaterial` | 20 | 0% |

**Key untested methods:**
- `AcousticRaytracer.TracePaths()` - Ray tracing for audio propagation
- `AudioEngine.Initialize()` - Core initialization
- `OpenALBackend.LoadWavFile()` - 54 statements, file I/O
- `AudioMixer.ProcessPaths()` - 40 statements, audio processing

**Risk:** Audio features may have undetected bugs in production.

---

### 2. ML (Machine Learning) Namespace - 0% Coverage (567 statements)

The entire ML subsystem is completely untested:

| Class | Statements | Coverage |
|-------|------------|----------|
| `MLService` | 122 | 0% |
| `PlayerAnalyticsSystem` | 116 | 0% |
| `AINavigationSystem` | 113 | 0% |
| `AnomalyDetectionSystem` | 110 | 0% |
| `DifficultyAdapterSystem` | 89 | 0% |
| `MLAgentSystem` | 92 | 0% |
| ML Components | 20 | 0% |

**Key untested functionality:**
- Model training and prediction pipelines
- Player behavior analysis
- Anomaly detection
- Dynamic difficulty adjustment
- AI navigation pathfinding

**Risk:** ML features are unreliable without test coverage.

---

### 3. Core.Scene Namespace - 35% Coverage (177/503 statements)

| Class | Statements | Coverage |
|-------|------------|----------|
| `Scene` | 144 | 0% |
| `SceneNode` | Partial | ~35% |
| `SceneSerializer` | Unknown | 0% |

**Untested in Scene class:**
- `CreateNode()` - Scene graph manipulation
- `Clear()` - Scene cleanup
- All serialization/deserialization

---

### 4. Physics - BlackHole Class - 0% Coverage (77 statements)

The relativistic black hole physics simulation is completely untested:

| Method | Statements |
|--------|------------|
| Constructor | 12 |
| `CalculatePhotonSphereRadius()` | 7 |
| `CalculateProgradeISCO()` | 9 |
| `CalculateRetrogradeISCO()` | 9 |
| `FrameDraggingAngularVelocity()` | 14 |
| Properties (Inner/Outer Horizon, etc.) | 26 |

**Risk:** Kerr black hole physics calculations may be incorrect.

---

### 5. Physics.GravitySystem - 12% Coverage (15/124 statements)

Core gravity calculations are poorly tested:

| Method | Coverage |
|--------|----------|
| Constructor | 0% |
| `CalculateEscapeVelocity()` | 83% |
| `Update()` | Low |
| Most physics methods | 0% |

---

### 6. UI Components - 0% Coverage

| Component | Statements |
|-----------|------------|
| `UiVideoComponent` | 20 |
| `UiImageComponent` | 16 |
| `UiTextComponent` | 7 |
| `UiRectComponent` | 6 |
| `TerminalComponent` | 4 |

---

### 7. Rendering Components - 0% Coverage

| Component | Statements |
|-----------|------------|
| `RenderSettingsComponent` | 23 |
| `MeshRelComponent` | 25 |
| `CameraComponent` | 19 |
| `UnpixComponent` | 16 |
| `MaterialComponent` | 4 |
| `AccretionDiskComponent` | 7 |
| `LaserEmitterComponent` | 9 |

---

## Moderately Weak Areas (< 50% Coverage)

### 1. Components Namespace - 26% Coverage (101/389 statements)

| Component | Statements | Coverage |
|-----------|------------|----------|
| `GravitySourceComponent` | 86 | 45% |
| `ColliderComponent` | 43 | 51% |
| `TransformComponent` | 30 | 43% |
| `RigidBodyComponent` | 27 | 59% |

**Gaps in TransformComponent:**
- `LocalPosition` setter: 0%
- `LocalRotation` setter: 0%
- `LocalScale` setter: 0%
- `Forward`, `Right`, `Up` properties: 0%
- `WorldMatrix` property: 0%

**Gaps in GravitySourceComponent:**
- `ErgosphereRadius()`: 0%
- `FrameDraggingAngularVelocity()`: 0%
- `ProgradeISCO` / `RetrogradeISCO`: 0%
- `InnerHorizonRadius`: 0%

---

### 2. Physics.Constraints - 57% Coverage (405/714 statements)

| Class | Statements | Coverage |
|-------|------------|----------|
| `ConstraintSolver` | 348 | 66% |
| `LinkMesh` | 148 | 52% |
| `LinkConstraint` | 108 | 67% |
| `LinkChain` | 98 | 29% |
| `AngleLimits` | 12 | 0% |

**Key gaps:**
- `LinkChain.CreateChain()`: 0%
- `LinkChain.CreateRope()`: 0%
- `LinkChain.GetPoints()`: 0%
- `LinkMesh.ApplyImpact()`: 0%
- `ConstraintSolver.SolveRopeLink()`: 35%
- All `AngleLimits` factory methods: 0%

---

## Well-Tested Areas (>70% Coverage)

### Core.ECS - 76% Coverage (271/356 statements)

| Class | Statements | Coverage |
|-------|------------|----------|
| `World` | 194 | 92% |
| `ComponentPool<T>` | 95 | 78% |
| `GameSystem` | 13 | 85% |
| `Entity` | 12 | 67% |

### Particles - 66% Coverage (536/810 statements)

| Class | Statements | Coverage |
|-------|------------|----------|
| `NoiseModule` | 45 | 100% |
| `EmissionShape` | 94 | 88% |
| `FloatRange` | 11 | 100% |
| `ColorOverLifetimeModule` | 4 | 100% |
| `ColorGradient` | 41 | 61% |
| `AnimationCurve` | 44 | 55% |

### Audio (Partial)

| Class | Statements | Coverage |
|-------|------------|----------|
| `ImpactSoundLibrary` | 67 | 88% |
| `CollisionAudioExtensions` | 16 | 100% |
| `CollisionSoundComponent` | 6 | 100% |

---

## Priority Recommendations

### High Priority (Critical Path)

1. **AudioEngine & OpenALBackend**
   - Core audio functionality, 0% coverage
   - ~587 statements need testing
   - Test initialization, playback, cleanup

2. **GravitySystem**
   - Core physics simulation, 12% coverage
   - Test gravity calculations, entity updates

3. **Scene Class**
   - Scene management, 0% coverage
   - Test node creation, hierarchy, cleanup

4. **BlackHole Physics**
   - Relativistic calculations, 0% coverage
   - Test horizon calculations, frame dragging

### Medium Priority

5. **CollisionAudioSystem** (9% coverage)
   - Test collision sound triggering
   - Test cooldown logic

6. **TransformComponent** (43% coverage)
   - Test all setters
   - Test matrix calculations

7. **Physics.Constraints Edge Cases**
   - `LinkChain` rope/chain creation
   - `SolveRopeLink` method

8. **ComponentSystem<T>** variants (0% coverage)
   - Generic system update loops

### Lower Priority

9. **ML Namespace** (0% coverage)
   - Test if features are in active use
   - Otherwise defer until feature is production-ready

10. **UI Components** (0% coverage)
    - Often integration-tested manually
    - Add tests before major UI changes

---

## Suggested Test Files to Create

```
RedHoleEngine.Tests/
├── Audio/
│   ├── AudioEngineTests.cs
│   ├── OpenALBackendTests.cs
│   ├── AcousticRaytracerTests.cs
│   └── AudioMixerTests.cs
├── Physics/
│   ├── BlackHoleTests.cs
│   ├── GravitySystemTests.cs
│   └── Constraints/
│       ├── LinkChainTests.cs
│       └── RopeConstraintTests.cs
├── Core/
│   └── Scene/
│       ├── SceneTests.cs
│       └── SceneSerializerTests.cs
└── Components/
    ├── TransformComponentTests.cs
    └── GravitySourceComponentTests.cs
```

---

## Coverage Goals

| Timeframe | Target | Focus Areas |
|-----------|--------|-------------|
| Short-term | 35% | Audio, GravitySystem, Scene |
| Medium-term | 50% | Physics, Components, Constraints |
| Long-term | 70% | Full coverage including ML, UI |

---

## Notes

- The ECS core (`World`, `ComponentPool<T>`) is well-tested at 76-92%
- Particle system has decent coverage at 66%
- Most 0% coverage areas appear to be newer features or integration-heavy code
- Consider mocking `OpenALBackend` for unit testing audio logic
- Physics calculations should have mathematical verification tests
