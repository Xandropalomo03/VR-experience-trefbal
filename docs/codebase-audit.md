# Codebase Audit — Multi-Brain Setup

Snapshot van de ML-Agents kant van het project. Doel: voor elke bestaande
agent vastleggen wat zijn brain er feitelijk in/uit krijgt, welke scripts en
scene-objecten erbij horen, en welke inconsistenties opvallen voor we naar
een proof-of-concept multi-brain opstelling toewerken.

## 1. Map structuur

```
Assets/
├── Scripts/
│   ├── ArenaDuplicator.cs
│   ├── Ball.cs                    (dodgeball)
│   ├── BallSpawner.cs             (dodgeball)
│   ├── Ball_catching.cs           (catching variant)
│   ├── BallSpawner_catching.cs    (catching variant)
│   ├── CatchAgent .cs             (let op spatie in filename)
│   ├── DodgeballAgent.cs
│   └── Throwing/
│       ├── Target.cs
│       ├── ThrowableBall.cs
│       └── ThrowingAgent.cs
├── Scenes/
│   ├── SampleScene.unity          (geen agents)
│   ├── TrainingScene.unity        (DodgeballAgent training)
│   ├── ThrowingTrainingScene.unity(ThrowingAgent training)
│   └── catching training.unity    (CatchAgent training)
└── Models/
    ├── DodgeballAgent.onnx
    ├── ThrowingAgent_v2.onnx
    └── CatchAgent.onnx
```

## 2. Agents

### 2.1 DodgeballAgent

- **Path:** `Assets/Scripts/DodgeballAgent.cs`
- **Class:** `DodgeballAgent : Agent`
- **Behavior Name (scene):** `DodgeballAgent` (TrainingScene.unity)
- **BehaviorType:** 2 (Inference Only)
- **Model:** `Assets/Models/DodgeballAgent.onnx`
- **MaxStep (scene):** 1000

#### Observations

`CollectObservations` adds **2** vector observations:

| # | Observation | Normalisatie |
|---|---|---|
| 1 | `rb.linearVelocity.x` | `/ moveSpeed` |
| 2 | `rb.linearVelocity.z` | `/ moveSpeed` |

Plus een `RayPerceptionSensorComponent3D` op het agent GameObject:

| Param | Value |
|---|---|
| RaysPerDirection | 5 (→ 11 rays totaal) |
| MaxRayDegrees | 90 |
| DetectableTags | `Wall`, `Ball` (2 tags) |
| RayLength | 15 |
| SphereCastRadius | 0.3 |
| ObservationStacks | 3 |
| StartVerticalOffset / EndVerticalOffset | 0 / 0 |

Ray-observation count (ML-Agents formule
`(2*rays+1) * (tags+2) * stacks`) = `11 * 4 * 3` = **132 floats**.
Totaal observation feature dimensie ≈ **2 vector + 132 ray = 134**.

BehaviorParameters in scene bevestigen `VectorObservationSize: 2`,
`NumStackedVectorObservations: 1` (geen vector stacking — de stacking
gebeurt alleen in de ray sensor).

#### Action space

- Continuous: **2** (moveX, moveZ — beide geclampt door agent zelf? Nee, niet
  geclampt in script, ruwe waarde × `moveSpeed`)
- Discrete: scene zet `BranchSizes: 01000000` (= 1 branch met grootte 1).
  Dat is effectief geen discrete actie; alleen een artefact van de inspector
  default. Code leest geen `DiscreteActions`.

#### Rewards

| Event | Reward |
|---|---|
| Per step (survival) | `+0.01` |
| Geraakt door bal (`OnHitByBall()`) | `-1` + `EndEpisode()` |

#### Episode termination

`EndEpisode()` alleen bij `OnHitByBall()` (collision callback vanuit
`Ball.cs`), en impliciet wanneer `MaxStep = 1000` bereikt wordt.

#### Dependencies

- `Rigidbody` op agent
- `BallSpawner` (geserialiseerd veld) → wordt gereset per episode
- `Ball.cs` script op alle balprefabs (voor de hit-callback)
- Tag `Ball` op balobjecten (gebruikt door `DestroyAllBalls`)
- Parent transform = arena (gebruikt voor lokale positie + filter in
  `DestroyAllBalls`)
- `ArenaDuplicator` op de arena root voor parallel training (×4 arenas)

---

### 2.2 ThrowingAgent

- **Path:** `Assets/Scripts/Throwing/ThrowingAgent.cs`
- **Class:** `ThrowingAgent : Agent`
- **Behavior Name (scene):** `Throwing` (ThrowingTrainingScene.unity)
- **BehaviorType:** 2 (Inference Only)
- **Model:** `Assets/Models/ThrowingAgent_v2.onnx`
- **MaxStep (scene):** 500

#### Observations

`CollectObservations` in code adds **12** observations:

| # | Observation | Normalisatie |
|---|---|---|
| 1 | `toTarget.x` | `/ 10` |
| 2 | `toTarget.z` | `/ 10` |
| 3 | `target.CurrentVelocity.x` | `/ 5` |
| 4 | `target.CurrentVelocity.z` | `/ 5` |
| 5 | `sin(yaw)` | — |
| 6 | `cos(yaw)` | — |
| 7 | `hasBall ? 1 : 0` | — |
| 8 | `StepCount / MaxStep` | — |
| 9 | `toTarget.magnitude` | `/ 10` |
| 10 | `curTargetSize` | `/ 2` |
| 11 | `sin(angleToTarget)` | — |
| 12 | `cos(angleToTarget)` | — |

**Geen** RayPerceptionSensor op de Throwing agent.

> ⚠️ **Inconsistentie:** BehaviorParameters in scene zegt
> `VectorObservationSize: 10`, maar de code schrijft 12 obs. De comment in
> de code (`// Vector observation size = 12`) erkent dit. Dit zal bij
> training of inference foutmelden, of het model dat nu in slot zit
> (`ThrowingAgent_v2.onnx`) is getraind op een oudere obs-vorm. Moet recht
> getrokken worden voor de multi-brain build.

#### Action space

- Continuous: **5** — `moveX, moveZ, aimYaw, throwAngle, throwPower` (alle
  geclampt op [-1, 1] in `OnActionReceived`)
- Discrete: **1 branch van grootte 2** (`BranchSizes: 02000000`) — trigger
  voor gooien (`actions.DiscreteActions[0] == 1`)

Scene bevestigt: `NumContinuousActions: 5`, BranchSizes = `[2]`.

#### Rewards

| Event / conditie | Reward |
|---|---|
| Elke step | `-0.0002` (time penalty) |
| Per step, als `hasBall` en hoek tot target `< 30°` | `+0.002 * (1 - |angle|/30)` (aim bonus) |
| Doel geraakt, `distToCenter < 0.2` (`NotifyTargetHit`) | `+1.5` |
| Doel geraakt, anders (`NotifyTargetHit`) | `+0.7` |
| Doel geraakt, proximity bonus | `+0.3 * (1 - min(distToCenter/1, 1))` |
| Miss op grond/muur (`NotifyMiss`) | `-0.1` + bonus `0.5 * (1 - min(closestApproach/5, 1))` |
| MaxStep bereikt zonder ooit te gooien | `-0.1` + `EndEpisode()` |

#### Episode termination

- `NotifyTargetHit` → `EndEpisode()`
- `NotifyMiss` → `EndEpisode()`
- `StepCount >= MaxStep - 1 && !hasThrown` → `EndEpisode()`
- Impliciete MaxStep timeout

#### Curriculum (EnvironmentParameters)

`target_distance` (default 4), `target_size` (default 1), `target_speed`
(default 0), `throw_noise` (default 0). Gelezen per episode.

#### Dependencies

- `Rigidbody` op agent (forced non-kinematic)
- `Collider` op agent (voor `Physics.IgnoreCollision` met de bal)
- `Transform ballHolder` (hand-positie)
- `Target` component op target GameObject (positie + reset + bewegen +
  velocity)
- `GameObject ballPrefab` met `ThrowableBall` (of het script wordt
  toegevoegd via `AddComponent` als fallback)
- Parent transform = arena (bal wordt onder parent gespawned)

---

### 2.3 CatchAgent

- **Path:** `Assets/Scripts/CatchAgent .cs` (let op spatie in bestandsnaam)
- **Class:** `CatchAgent : Agent`
- **Behavior Name (scene):** `CatchAgent` (catching training.unity)
- **BehaviorType:** 2 (Inference Only)
- **Model:** `Assets/Models/CatchAgent.onnx`
- **MaxStep (scene):** 0 (oneindig — episode eindigt alleen via events)

#### Observations

`CollectObservations` adds **1** placeholder (`0f`), met
`NumStackedVectorObservations: 3` → effectief 3 vector floats. Echte
perceptie komt van `RayPerceptionSensorComponent3D`:

| Param | Value |
|---|---|
| RaysPerDirection | 5 (→ 11 rays) |
| MaxRayDegrees | 60 |
| DetectableTags | `Ball` (1 tag) |
| RayLength | 20 |
| SphereCastRadius | 0.5 |
| ObservationStacks | 1 |
| StartVerticalOffset / EndVerticalOffset | 0.6 / 0 |

Ray-observation count = `11 * 3 * 1` = **33 floats**. Totaal feature
dimensie ≈ **3 vector + 33 ray = 36**.

#### Action space

- Continuous: **0**
- Discrete: **2 branches**, BranchSizes `[3, 2]`
  - Branch 0 (rotation): 0 = -45°, 1 = 0°, 2 = +45° (snap rotation)
  - Branch 1 (catch): 0 = niet, 1 = catch attempt

#### Rewards

| Event / conditie | Reward |
|---|---|
| Rotation change (`rotateAction != lastRotation`) | `-0.001` (anti-jitter) |
| Catch attempt frame (`catchAttempt == true`) | `-0.02` (anti-spam) |
| `OnTriggerEnter` met Ball én `attemptedCatch` | `+1` (catch success) |
| `OnTriggerEnter` met Ball én niet attempted | `-1` (miss) |
| `OnCollisionEnter` met Ball (hard hit, geen trigger) | `-1` |

#### Episode termination

`EndEpisode()` bij elke bal-interactie (trigger of collision). MaxStep is 0
dus de episode eindigt nooit puur op tijd — er moet altijd een bal langs
komen.

#### Dependencies

- `Renderer catchZoneRenderer` (alleen visueel)
- `Collider` op agent (waarvan minstens één trigger voor de
  `OnTriggerEnter` catch zone)
- `BallSpawner_Catching` in de scene die balprefabs spawned met
  `Ball_Catching` script
- Tag `Ball` op de bal
- `RayPerceptionSensorComponent3D` voor perceptie

---

## 3. Support scripts

| Script | Doel | Gebruikt door |
|---|---|---|
| `Ball.cs` | Lifetime + collision-callback die `DodgeballAgent.OnHitByBall()` triggert. | DodgeballAgent (via prefab → BallSpawner) |
| `BallSpawner.cs` | Spawned ballen vanaf vaste hoeken rond arena center richting agent (met aim noise + speed variation). Heeft `ResetSpawner()` voor episode reset. | DodgeballAgent |
| `ArenaDuplicator.cs` | Awake-only: kloont arena 3× met X/Z offsets (15m grid) → 4 parallelle trainingsomgevingen. Static guard tegen recursie. | Generic (alle training scenes kunnen hem gebruiken) |
| `Ball_catching.cs` (`Ball_Catching` class) | Lifetime + notify spawner bij destroy zodat er pas een nieuwe bal komt als de vorige weg is. | CatchAgent (via prefab → BallSpawner_Catching) |
| `BallSpawner_catching.cs` (`BallSpawner_Catching` class) | Spawned één bal tegelijk in een symmetrische boog vóór het target en mikt op target. Wacht tot vorige bal `NotifyBallDestroyed()` aanroept. | CatchAgent |
| `Throwing/Target.cs` | Doelobject: random reset op tegenoverliggende helft, optioneel sinusoïdaal heen-en-weer bewegen, bijhoudt `CurrentVelocity`, dispatcht `agent.NotifyTargetHit`. | ThrowingAgent |
| `Throwing/ThrowableBall.cs` | Bal die "Held" → "Thrown" state heeft. In Held volgt hij de holder via transform; bij Release wordt hij non-kinematic met gegeven velocity. Negeert agent collider om instant-miss te vermijden. Detecteert hit op Target of miss op andere collider. | ThrowingAgent |

## 4. Scenes

| Scene | Inhoud (kort) |
|---|---|
| `SampleScene.unity` | Default Unity sample, geen ML-Agents content. Niet gebruikt voor training. |
| `TrainingScene.unity` | DodgeballAgent training. Bevat agent + RayPerceptionSensor + BallSpawner + arena (waarschijnlijk ×4 via ArenaDuplicator). MaxStep 1000. Model in inference slot: `DodgeballAgent.onnx`. |
| `ThrowingTrainingScene.unity` | ThrowingAgent training. Agent + Target + ballHolder + ballPrefab references. MaxStep 500. Model in inference slot: `ThrowingAgent_v2.onnx`. Behavior name `Throwing`. |
| `catching training.unity` | CatchAgent training. Agent + RayPerceptionSensor + BallSpawner_Catching. MaxStep 0. Model in inference slot: `CatchAgent.onnx`. Bestandsnaam met spatie kan op sommige CI-systemen issues geven. |

## 5. Models

| Model | Agent | Behavior name match | Opmerkingen |
|---|---|---|---|
| `Assets/Models/DodgeballAgent.onnx` | DodgeballAgent | ✅ | Geassigned in TrainingScene. |
| `Assets/Models/ThrowingAgent_v2.onnx` | ThrowingAgent | ⚠️ obs-shape mismatch | Scene heeft `VectorObservationSize: 10` terwijl code 12 obs schrijft — model is waarschijnlijk getraind op 10. |
| `Assets/Models/CatchAgent.onnx` | CatchAgent | ✅ | Geassigned in catching training. |

## 6. Comparison tables

### 6.1 Observation summary

| Agent | Vector obs (code) | Vector stack | Ray sensor | Ray feature count | Totaal features |
|---|---:|---:|---|---:|---:|
| DodgeballAgent | 2 | 1 | 5 rays / 90° / 2 tags / stack 3 | 132 | ~134 |
| ThrowingAgent | 12 (scene: 10 ⚠️) | 1 | — | 0 | 12 (of 10) |
| CatchAgent | 1 (placeholder) | 3 | 5 rays / 60° / 1 tag / stack 1 | 33 | ~36 |

### 6.2 Action summary

| Agent | Continuous | Discrete branches | Discrete sizes | Totale action shape |
|---|---:|---:|---|---|
| DodgeballAgent | 2 | 1 (dummy) | [1] | 2 cont |
| ThrowingAgent | 5 | 1 | [2] | 5 cont + 1 disc(2) |
| CatchAgent | 0 | 2 | [3, 2] | 2 disc(3,2) |

### 6.3 Reward shape

| Agent | Sparse terminal | Dense shaping | EndEpisode triggers |
|---|---|---|---|
| DodgeballAgent | -1 op hit | +0.01/step survival | Hit by ball |
| ThrowingAgent | +0.7…+1.8 hit / -0.1 miss + closest-approach bonus | -0.0002/step time + aim bonus terwijl hasBall | Hit, miss, of MaxStep zonder gooi |
| CatchAgent | +1 catch / -1 miss/hit | -0.001 rotation change, -0.02 catch spam | Elke bal-interactie |

## 7. Opvallende issues en inconsistenties

1. **ThrowingAgent obs-mismatch.** Code = 12 observations, scene
   BehaviorParameters = `VectorObservationSize: 10`. Het bestaande
   `ThrowingAgent_v2.onnx` model is dus ofwel op 10 obs getraind (en zal
   crashen of garbage produceren met de huidige code) ofwel op 12 en is de
   scene niet bijgewerkt. **Action item:** bepalen welk van de twee
   correct is, scene of code aanpassen en eventueel hertrainen.

2. **Bestandsnaam met spatie:** `Assets/Scripts/CatchAgent .cs` heeft een
   trailing space vóór `.cs`. Werkt in Unity maar is bug-magneet bij
   command-line tools, git op Linux runners, en zoek/replace acties.
   Aanrader: hernoem naar `CatchAgent.cs`.

3. **Naming inconsistency:** Catching variant gebruikt
   `Ball_catching.cs` / `BallSpawner_catching.cs` (lowercase suffix) terwijl
   de classes `Ball_Catching` / `BallSpawner_Catching` heten (CamelCase
   suffix). De Throwing variant zit netjes in een subfolder
   `Throwing/` — overweeg vergelijkbare folders `Dodgeball/` en `Catching/`
   voor consistentie.

4. **Dummy discrete action op DodgeballAgent.** `BranchSizes: [1]` in de
   scene is feitelijk geen discrete actie, maar wel meegelift in de model
   shape. Voor een gedeeld multi-brain Agent script is dat irritant — beter
   expliciet op 0 zetten.

5. **`MaxStep = 0` op CatchAgent.** Geen tijdslimiet op de episode. Als de
   ballenspawner faalt of een bal nooit aankomt, blijft de episode hangen.
   Voor multi-brain training waar één env-tick alle brains tikt is dat
   risicovol.

6. **ArenaDuplicator gebruikt static guard.** `hasDuplicated` is een
   static bool die nooit gereset wordt. Bij domain reload (Play → Stop →
   Play) is dat ok in modern Unity, maar het is iets om te onthouden als
   een tweede arena-set ooit nodig is — bv. één set per brain.

7. **Geen MaxStep wordt geforceerd op DodgeballAgent voor "succesvol
   overleven":** de agent krijgt alleen reward voor overleven en straf
   voor geraakt worden; geen expliciete win-conditie / positive terminal
   reward. Dat is een ontwerpkeuze, maar relevant als je deze brain in een
   gedeeld trainings-curriculum gaat hangen.

8. **Scene `SampleScene.unity` is dood gewicht** voor de ML-Agents kant.
   Niet kritisch, wel iets om bij multi-brain-PoC weg te halen of expliciet
   te markeren als "VR demo scene".

## 8. Vooruitkijk: multi-brain PoC

De drie agents verschillen sterk in zowel obs- als action-shape (zie
tabellen 6.1 en 6.2). Een gedeeld `Agent` script met één behavior name is
daardoor niet realistisch zonder zware aanpassingen. Praktischer:

- Houd drie aparte `Agent` subclasses (zoals nu).
- Maak één multi-brain scene waarin alle drie de behaviors tegelijk
  geregistreerd staan onder verschillende behavior names, en elk hun eigen
  `.onnx` model laden in inference.
- Eventueel een gedeelde `BaseSportAgent` met alleen shared boilerplate
  (`myArena`, `Rigidbody` cache, helpers voor reward logging), niet voor
  obs/action.
