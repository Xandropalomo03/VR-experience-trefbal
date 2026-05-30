using UnityEngine;
using Unity.MLAgents;

// ============================================================================
// MatchCoordinator
// ----------------------------------------------------------------------------
// Bepaalt automatisch wat de agent doet. Speelbare game-loop met vier
// toestanden:
//
//   IDLE   -> AgentIdleMovement bestuurt de dodge-body (rustig rondlopen).
//   DODGE  -> de getrainde dodge-brain bestuurt de dodge-body.
//   CATCH  -> de getrainde catch-brain (aparte body) probeert de bal te vangen.
//   THROW  -> de getrainde throw-brain (aparte body) gooit een bal terug.
//
// SWITCH-LOGICA (de gewenste loop)
//   - Default = idle.
//   - Komt er een bal AANGEROLD richting de agent (binnen activationDistance en
//     met snelheidscomponent naar de agent toe) -> kies DODGE of CATCH op basis
//     van catchChance (default leunt naar catch zodat de rally doorgaat).
//   - CATCH lukt (agent vangt de bal) -> THROW: gooi terug richting de plek waar
//     de bal vandaan kwam.
//   - CATCH mislukt (gemist / hard geraakt) -> terug naar IDLE.
//   - Na de worp (THROW klaar) -> terug naar IDLE.
//   - DODGE: bal weg / gestopt / langs -> na een korte vertraging terug naar IDLE.
//
// IDLE vs DODGE (DecisionRequester-toggle, ongewijzigd t.o.v. de vorige versie)
//   Idle en dodge delen DEZELFDE body (de dodge-body). We zetten de dodge-brain
//   NIET aan/uit via Agent.enabled (dat triggert OnEpisodeBegin -> teleport +
//   ballen vernietigen). In plaats daarvan pauzeren/hervatten we via de
//   DecisionRequester op de dodge-body:
//     - DecisionRequester.enabled = false  -> idle-locomotie stuurt, GEEN reset.
//     - DecisionRequester.enabled = true   -> dodge-brain hervat, GEEN reset.
//
// CATCH en THROW (aparte bodies, via de BrainSwitcher)
//   Catch en throw zijn aparte GameObject-bodies op dezelfde plek. Daarvoor
//   gebruiken we de BrainSwitcher (Agent.enabled + zichtbaarheid + transform-
//   overdracht). Voor die twee brains is OnEpisodeBegin juist GEWENST:
//     - CatchAgent.OnEpisodeBegin reset enkel rotatie/kleur (geen teleport,
//       vernietigt geen ballen) -> onschuldig.
//     - ThrowingAgent.OnEpisodeBegin spawnt ZELF een verse bal in de ball-holder.
//       Dat is meteen de bal-handoff: na een catch hoeven we niets fysiek over te
//       dragen; we switchen naar de throw-body en die heeft vanzelf een bal.
//   De caught bal wordt door CatchAgent vernietigd; de throw-body gooit zijn
//   eigen verse bal.
//
// CATCH/THROW-RESULTAAT
//   We luisteren op CatchAgent.CatchFinished(bool) en ThrowingAgent.ThrowFinished.
//   Die events vuren MIDDEN in een collision/EndEpisode-callback, dus we
//   verwerken ze NIET direct (zou een brain-switch midden in een agent-callback
//   forceren). We zetten een 'pending'-vlag en handelen die af in FixedUpdate.
//
// THROW-DOEL (testscene, nog geen speler)
//   We verplaatsen het Target-object (brainSwitcher.throwSupport) naar de plek
//   waar de bal vandaan kwam, zodat de throw-brain daarheen terugmikt.
//
// REFERENTIES WIREN (Inspector)
//   - agentTransform : de dodge-body (DodgeballAgent + AgentIdleMovement). Verplicht.
//   - idleMovement   : het AgentIdleMovement-component op die body. Verplicht.
//   - brainSwitcher  : verplicht voor catch + throw. Leeg = alleen idle/dodge
//                      (val terug op het oude gedrag).
//   De catch-/throw-agents en het throw-target worden via de BrainSwitcher
//   gevonden (catchAgent / throwAgent / throwSupport); niets extra te wiren.
// ============================================================================
public class MatchCoordinator : MonoBehaviour
{
    private enum State { Idle, Dodge, Catch, Throw }

    [Header("Referenties")]
    [Tooltip("De dodge-body (zelfde GameObject als DodgeballAgent/AgentIdleMovement).")]
    [SerializeField] private Transform agentTransform;
    [Tooltip("Idle-loopgedrag op de dodge-body.")]
    [SerializeField] private AgentIdleMovement idleMovement;
    [Tooltip("Verplicht voor catch + throw. Leeg = alleen idle/dodge.")]
    [SerializeField] private BrainSwitcher brainSwitcher;

    [Header("Bal-detectie")]
    [Tooltip("Tag van de ballen waarop gereageerd wordt.")]
    [SerializeField] private string ballTag = "Ball";
    [Tooltip("Binnen deze afstand (meter, horizontaal) telt een bal mee.")]
    [SerializeField] private float activationDistance = 8f;
    [Tooltip("Bal moet sneller dan dit (m/s) zijn, anders geldt 'ie als gestopt.")]
    [SerializeField] private float minBallSpeed = 0.5f;
    [Tooltip("Snelheidscomponent NAAR de agent toe moet groter zijn dan dit (m/s).")]
    [SerializeField] private float minApproachSpeed = 1f;
    [Tooltip("Alleen ballen in dezelfde arena (parent) als de agent meetellen.")]
    [SerializeField] private bool sameArenaOnly = true;

    [Header("Gedrag")]
    [Tooltip("Kans (0..1) dat de agent CATCHT i.p.v. DODGET bij een naderende bal. " +
             "Hoog = rally gaat door. 1 = altijd catchen, 0 = altijd dodgen.")]
    [Range(0f, 1f)]
    [SerializeField] private float catchChance = 0.7f;
    [Tooltip("Zo lang na de laatste naderende bal blijft de agent nog in dodge, " +
             "daarna terug naar idle. Voorkomt flikkeren.")]
    [SerializeField] private float returnToIdleDelay = 1.5f;
    [Tooltip("Veiligheid: als een catch-poging na zo veel sec niet is afgerond " +
             "(bal vloog langs zonder te raken), terug naar idle.")]
    [SerializeField] private float catchTimeout = 3f;
    [Tooltip("Veiligheid: als een worp na zo veel sec niet is afgerond, terug naar idle.")]
    [SerializeField] private float throwTimeout = 8f;

    private State state = State.Idle;
    private float lastIncomingTime = -999f;
    private float stateEnterTime = -999f;
    private Vector3 lastBallOrigin;

    // Resultaat-vlaggen, gezet door de agent-events en afgehandeld in FixedUpdate
    // (zodat we niet midden in een collision-callback van brain wisselen).
    private int pendingCatch;      // 0 = niets, 1 = gevangen, -1 = gemist
    private bool pendingThrowDone;

    private DecisionRequester decisionRequester;
    private Agent dodgeAgentComponent; // alleen voor de fallback
    private Transform arena;

    private CatchAgent catchAgent;
    private ThrowingAgent throwAgent;

    private void Awake()
    {
        if (agentTransform != null)
        {
            agentTransform.TryGetComponent(out decisionRequester);
            agentTransform.TryGetComponent(out dodgeAgentComponent);
            arena = agentTransform.parent;
        }

        if (decisionRequester == null)
        {
            Debug.LogWarning("MatchCoordinator: geen DecisionRequester op de dodge-agent " +
                "gevonden. Val terug op Agent.enabled-togglen — let op: dat triggert " +
                "OnEpisodeBegin (agent teleporteert + ballen worden vernietigd) bij " +
                "elke switch naar dodge.");
        }

        // Catch-/throw-agents via de BrainSwitcher ophalen (geen extra wiring).
        if (brainSwitcher != null)
        {
            if (brainSwitcher.catchAgent != null)
                brainSwitcher.catchAgent.TryGetComponent(out catchAgent);
            if (brainSwitcher.throwAgent != null)
                brainSwitcher.throwAgent.TryGetComponent(out throwAgent);

            if (catchAgent != null) catchAgent.CatchFinished += OnCatchFinished;
            if (throwAgent != null) throwAgent.ThrowFinished += OnThrowFinished;
        }
        else
        {
            Debug.LogWarning("MatchCoordinator: geen BrainSwitcher gewired. Alleen " +
                "idle/dodge actief — catch en throw worden overgeslagen.");
        }
    }

    private void OnDestroy()
    {
        if (catchAgent != null) catchAgent.CatchFinished -= OnCatchFinished;
        if (throwAgent != null) throwAgent.ThrowFinished -= OnThrowFinished;
    }

    private void Start()
    {
        // Begin in idle, maar forceer GEEN brain-switch: de BrainSwitcher kiest
        // bij scene-start zelf de dodge-body (na zijn sensor-warm-up). Een
        // vroege switch hier zou die warm-up verstoren.
        EnterIdle(switchBrain: false);
    }

    private void FixedUpdate()
    {
        if (agentTransform == null || idleMovement == null) return;

        GameObject incoming = GetIncomingBall();
        if (incoming != null) lastIncomingTime = Time.time;

        switch (state)
        {
            case State.Idle:
                if (incoming != null)
                {
                    // Onthoud waar de bal vandaan komt (voor het terugmikken).
                    lastBallOrigin = incoming.transform.position;

                    bool canCatch = brainSwitcher != null && catchAgent != null;
                    if (canCatch && Random.value < catchChance)
                        EnterCatch();
                    else
                        EnterDodge();
                }
                break;

            case State.Dodge:
                if (Time.time - lastIncomingTime >= returnToIdleDelay)
                    EnterIdle();
                break;

            case State.Catch:
                if (pendingCatch == 1) { pendingCatch = 0; EnterThrow(); }
                else if (pendingCatch == -1) { pendingCatch = 0; EnterIdle(); }
                else if (Time.time - stateEnterTime >= catchTimeout)
                {
                    DebugLogger.Log("MATCH", "catch timeout -> IDLE");
                    EnterIdle();
                }
                break;

            case State.Throw:
                if (pendingThrowDone) { pendingThrowDone = false; EnterIdle(); }
                else if (Time.time - stateEnterTime >= throwTimeout)
                {
                    DebugLogger.Log("MATCH", "throw timeout -> IDLE");
                    EnterIdle();
                }
                break;
        }
    }

    // Geeft de eerste bal terug die binnen bereik richting de agent komt, anders null.
    private GameObject GetIncomingBall()
    {
        GameObject[] balls = GameObject.FindGameObjectsWithTag(ballTag);
        Vector3 agentPos = agentTransform.position;

        foreach (GameObject ball in balls)
        {
            if (ball == null) continue;
            if (sameArenaOnly && arena != null && ball.transform.parent != arena) continue;

            // Horizontaal redeneren (de hoogte van de bal doet er niet toe).
            Vector3 toAgent = agentPos - ball.transform.position;
            toAgent.y = 0f;
            float dist = toAgent.magnitude;
            if (dist > activationDistance || dist < 0.0001f) continue;

            if (!ball.TryGetComponent(out Rigidbody ballRb)) continue;
            Vector3 vel = ballRb.linearVelocity;
            vel.y = 0f;
            if (vel.magnitude < minBallSpeed) continue; // gestopt / rolt nauwelijks / vastgehouden

            // Snelheidscomponent richting de agent (projectie op de richting).
            float approach = Vector3.Dot(vel, toAgent.normalized);
            if (approach >= minApproachSpeed) return ball;
        }

        return null;
    }

    private void EnterIdle(bool switchBrain = true)
    {
        state = State.Idle;
        stateEnterTime = Time.time;
        pendingCatch = 0;
        pendingThrowDone = false;
        DebugLogger.Log("MATCH", "-> IDLE");

        // Zorg dat de dodge-body weer de actieve body is (na catch/throw).
        if (switchBrain && brainSwitcher != null) brainSwitcher.SwitchToBrain("dodge");

        // Dodge-brain pauzeren, idle-locomotie aan.
        SetDodgeBrainActive(false);
        idleMovement.enabled = true;
    }

    private void EnterDodge()
    {
        state = State.Dodge;
        stateEnterTime = Time.time;
        DebugLogger.Log("MATCH", "idle -> DODGE (bal nadert)");

        // Idle-locomotie uit; dodge-body actief; brain hervatten.
        idleMovement.enabled = false;
        if (brainSwitcher != null) brainSwitcher.SwitchToBrain("dodge");
        SetDodgeBrainActive(true);
    }

    private void EnterCatch()
    {
        state = State.Catch;
        stateEnterTime = Time.time;
        pendingCatch = 0;
        DebugLogger.Log("MATCH", "idle -> CATCH (bal nadert)");

        // Idle-locomotie uit en dodge-brain pauzeren (de BrainSwitcher zet de
        // dodge-body straks toch uit). Dan naar de catch-body.
        idleMovement.enabled = false;
        SetDodgeBrainActive(false);
        brainSwitcher.SwitchToBrain("catch");
    }

    private void EnterThrow()
    {
        state = State.Throw;
        stateEnterTime = Time.time;
        pendingThrowDone = false;
        DebugLogger.Log("MATCH", "catch gelukt -> THROW");

        idleMovement.enabled = false;

        // Switch naar de throw-body. Diens OnEpisodeBegin spawnt zelf een verse
        // bal in de holder (de bal-handoff) en reset het target.
        brainSwitcher.SwitchToBrain("throw");

        // Mik terug naar waar de bal vandaan kwam. NA de switch, want de
        // throw-OnEpisodeBegin heeft het target net naar een random plek gezet.
        if (brainSwitcher.throwSupport != null)
        {
            Vector3 aim = lastBallOrigin;
            brainSwitcher.throwSupport.transform.position = aim;
            DebugLogger.Log("MATCH", $"throw-target gezet op {aim} (oorsprong bal)");
        }
    }

    // ---- agent-event handlers (zetten alleen vlaggen; afhandeling in FixedUpdate) ----

    private void OnCatchFinished(bool success)
    {
        if (state != State.Catch) return;
        pendingCatch = success ? 1 : -1;
        DebugLogger.Log("MATCH", $"CatchFinished success={success}");
    }

    private void OnThrowFinished()
    {
        if (state != State.Throw) return;
        pendingThrowDone = true;
        DebugLogger.Log("MATCH", "ThrowFinished");
    }

    // Pauzeer/hervat de DODGE-brain ZONDER een episode-reset (zie kop-comment).
    private void SetDodgeBrainActive(bool active)
    {
        if (decisionRequester != null)
        {
            decisionRequester.enabled = active;
            return;
        }

        // Fallback (geen DecisionRequester): toggle de Agent zelf. LET OP: dit
        // triggert OnEpisodeBegin -> teleport + ballen weg bij naar-dodge.
        if (dodgeAgentComponent != null)
            dodgeAgentComponent.enabled = active;
    }
}
