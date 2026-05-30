using UnityEngine;
using Unity.MLAgents;

// ============================================================================
// MatchCoordinator
// ----------------------------------------------------------------------------
// Bepaalt automatisch wat de agent doet. Eerste speelbare game-loop, bewust
// simpel: alleen twee toestanden.
//
//   IDLE   -> AgentIdleMovement bestuurt de body (rustig rondlopen).
//   DODGE  -> de getrainde dodge-brain bestuurt de body.
//
// (catch en throw doen we hier NOG NIET.)
//
// SWITCH-LOGICA
//   - Default = idle.
//   - Komt er een bal AANGEROLD richting de agent (binnen activationDistance en
//     met een snelheidscomponent naar de agent toe) -> DODGE.
//   - Bal weg / gestopt / langs -> na een korte vertraging terug naar IDLE.
//
// HOE "DODGE" WORDT AANGEZET  (belangrijk!)
//   We zetten de dodge-brain NIET aan/uit via Agent.enabled. Reden: bij
//   re-enable draait Agent.OnEnable -> OnEpisodeBegin, en DodgeballAgent
//   .OnEpisodeBegin teleporteert de agent naar een random plek EN vernietigt
//   alle ballen in de arena. Dan zou je net de bal slopen die je wilt ontwijken
//   -> kapotte loop.
//   In plaats daarvan pauzeren/hervatten we de brain via de DecisionRequester:
//     - DecisionRequester.enabled = false  -> brain vraagt geen acties meer,
//       OnActionReceived stopt, AgentIdleMovement mag de body sturen. GEEN reset.
//     - DecisionRequester.enabled = true   -> brain hervat vanaf de huidige
//       positie, bal blijft bestaan. GEEN reset.
//   De Agent-component zelf blijft de hele tijd enabled (sensors blijven leven).
//
//   We roepen daarnaast nog steeds BrainSwitcher.SwitchToBrain("dodge") aan
//   (als er een BrainSwitcher gewired is) voor de logging / support-objects en
//   zodat dit straks meegroeit met catch + throw. In een scene met enkel de
//   dodge-agent mag BrainSwitcher leeg blijven.
//
// WAAR PLAATSEN
//   Op een leeg "MatchCoordinator" (of bestaande Manager) GameObject in de
//   game-scene.
//
// REFERENTIES WIREN (Inspector)
//   - agentTransform : de agent-body (zelfde GameObject als DodgeballAgent /
//                      AgentIdleMovement). Verplicht.
//   - idleMovement   : het AgentIdleMovement-component op die body. Verplicht.
//   - brainSwitcher  : optioneel. Leeg laten als deze scene geen BrainSwitcher
//                      heeft (dan stuurt MatchCoordinator de dodge-brain direct
//                      via de DecisionRequester).
//   DecisionRequester wordt automatisch van agentTransform gehaald; niets te
//   wiren. (Valt terug op Agent.enabled-togglen als er geen DecisionRequester
//   is — met een waarschuwing, want dan krijg je wel de OnEpisodeBegin-reset.)
// ============================================================================
public class MatchCoordinator : MonoBehaviour
{
    private enum State { Idle, Dodge }

    [Header("Referenties")]
    [Tooltip("De agent-body (zelfde GameObject als DodgeballAgent/AgentIdleMovement).")]
    [SerializeField] private Transform agentTransform;
    [Tooltip("Idle-loopgedrag op de agent-body.")]
    [SerializeField] private AgentIdleMovement idleMovement;
    [Tooltip("Optioneel: laat leeg in een scene zonder BrainSwitcher.")]
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

    [Header("Timing")]
    [Tooltip("Zo lang na de laatste naderende bal blijft de agent nog in dodge, " +
             "daarna terug naar idle. Voorkomt flikkeren.")]
    [SerializeField] private float returnToIdleDelay = 1.5f;

    private State state = State.Idle;
    private float lastIncomingTime = -999f;

    private DecisionRequester decisionRequester;
    private Agent dodgeAgentComponent; // alleen voor de fallback
    private Transform arena;

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
            Debug.LogWarning("MatchCoordinator: geen DecisionRequester op de agent " +
                "gevonden. Val terug op Agent.enabled-togglen — let op: dat triggert " +
                "OnEpisodeBegin (agent teleporteert + ballen worden vernietigd) bij " +
                "elke switch naar dodge.");
        }
    }

    private void Start()
    {
        // Begin altijd in idle.
        EnterIdle();
    }

    // Detectie in FixedUpdate: we lezen Rigidbody-snelheden van de ballen.
    private void FixedUpdate()
    {
        if (agentTransform == null || idleMovement == null) return;

        bool incoming = IsBallIncoming();
        if (incoming) lastIncomingTime = Time.time;

        switch (state)
        {
            case State.Idle:
                if (incoming) EnterDodge();
                break;

            case State.Dodge:
                // Pas terug naar idle als er al even geen bal meer aankomt.
                if (Time.time - lastIncomingTime >= returnToIdleDelay)
                    EnterIdle();
                break;
        }
    }

    // True als er minstens één bal binnen bereik is die richting de agent komt.
    private bool IsBallIncoming()
    {
        GameObject[] balls = GameObject.FindGameObjectsWithTag(ballTag);
        Vector3 agentPos = agentTransform.position;

        foreach (GameObject ball in balls)
        {
            if (ball == null) continue;
            if (sameArenaOnly && arena != null && ball.transform.parent != arena) continue;

            // Horizontaal redeneren (de y/hoogte van de bal doet er niet toe).
            Vector3 toAgent = agentPos - ball.transform.position;
            toAgent.y = 0f;
            float dist = toAgent.magnitude;
            if (dist > activationDistance || dist < 0.0001f) continue;

            if (!ball.TryGetComponent(out Rigidbody ballRb)) continue;
            Vector3 vel = ballRb.linearVelocity;
            vel.y = 0f;
            if (vel.magnitude < minBallSpeed) continue; // gestopt / rolt nauwelijks

            // Snelheidscomponent richting de agent (projectie op de richting).
            float approach = Vector3.Dot(vel, toAgent.normalized);
            if (approach >= minApproachSpeed) return true;
        }

        return false;
    }

    private void EnterDodge()
    {
        state = State.Dodge;
        DebugLogger.Log("MATCH", "idle -> DODGE (bal nadert)");

        // Idle-locomotie uit, daarna de brain hervatten.
        idleMovement.enabled = false;

        // Conceptuele switch via BrainSwitcher (logging + support + toekomstige
        // catch/throw). Mag een no-op zijn als dodge al de actieve brain is.
        if (brainSwitcher != null) brainSwitcher.SwitchToBrain("dodge");

        SetBrainActive(true);
    }

    private void EnterIdle()
    {
        state = State.Idle;
        DebugLogger.Log("MATCH", "-> IDLE (geen bal in de buurt)");

        // Brain pauzeren, daarna idle-locomotie aan.
        SetBrainActive(false);
        idleMovement.enabled = true;
    }

    // Pauzeer/hervat de dodge-brain ZONDER een episode-reset (zie kop-comment).
    private void SetBrainActive(bool active)
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
