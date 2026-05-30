using UnityEngine;

// ============================================================================
// AgentIdleMovement
// ----------------------------------------------------------------------------
// Simpele "rondlopen"-beweging voor de agent als er NIETS te doen is (idle).
// GEEN NavMesh: de agent kiest een random punt binnen z'n eigen helft en loopt
// daar langzaam naartoe via de Rigidbody, wacht even, en kiest dan een nieuw
// punt. Zo oogt de tegenstander "levend" terwijl de dodge-brain uit staat.
//
// WAAR PLAATSEN
//   Op HETZELFDE GameObject als de DodgeballAgent + Rigidbody (de agent-body,
//   bv. AgentDodge). Dit script bestuurt diezelfde Rigidbody.
//
// REFERENTIES WIREN
//   Geen object-referenties nodig. Wel even checken in de Inspector:
//   - moveSpeed: hou 'm laag (rustig wandelen), bv. 1.5.
//   - De helft-grenzen (areaMin/areaMax) staan default gelijk aan de spawn-zone
//     van DodgeballAgent.OnEpisodeBegin (x -4..4, z -4..-1). Pas aan als jouw
//     arena anders is. Dit zijn LOKALE coordinaten t.o.v. het arena-parent
//     (transform.parent), net als DodgeballAgent localPosition gebruikt.
//
// AAN/UIT
//   MatchCoordinator zet dit component aan (enabled = true) tijdens idle en uit
//   (enabled = false) zodra de dodge-brain het overneemt. Dit script doet dus
//   NIETS uit zichzelf behalve wanneer het enabled is. Bij uitschakelen zet het
//   de horizontale snelheid op 0 zodat de agent niet "doordrijft" de dodge in.
// ============================================================================
[RequireComponent(typeof(Rigidbody))]
public class AgentIdleMovement : MonoBehaviour
{
    [Header("Snelheid")]
    [Tooltip("Loopsnelheid tijdens idle. Laag houden = rustig wandelen.")]
    [SerializeField] private float moveSpeed = 1.5f;

    [Header("Eigen helft (LOKALE coords t.o.v. arena-parent)")]
    [Tooltip("Min hoek van de helft (x, z). Default = dodge spawn-zone.")]
    [SerializeField] private Vector2 areaMin = new Vector2(-4f, -4f);
    [Tooltip("Max hoek van de helft (x, z). Default = dodge spawn-zone.")]
    [SerializeField] private Vector2 areaMax = new Vector2(4f, -1f);

    [Header("Gedrag")]
    [Tooltip("Hoe dichtbij het doelpunt telt als 'aangekomen' (meter).")]
    [SerializeField] private float arriveThreshold = 0.4f;
    [Tooltip("Min/max pauze (sec) bij een doelpunt voordat een nieuw punt wordt gekozen.")]
    [SerializeField] private float minPause = 0.3f;
    [SerializeField] private float maxPause = 1.5f;

    private Rigidbody rb;
    private Vector3 targetLocal;   // huidig doelpunt in lokale arena-coords
    private float pauseTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    // Telkens als idle wordt aangezet: meteen een vers doelpunt kiezen.
    private void OnEnable()
    {
        ChooseNewTarget();
        pauseTimer = 0f;
    }

    // Bij uitschakelen (brain neemt over): horizontale beweging stoppen.
    private void OnDisable()
    {
        StopHorizontal();
    }

    private void FixedUpdate()
    {
        if (rb == null) return;

        // Even pauzeren bij een bereikt punt.
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
            StopHorizontal();
            if (pauseTimer <= 0f) ChooseNewTarget();
            return;
        }

        Vector3 targetWorld = LocalToWorld(targetLocal);
        Vector3 toTarget = targetWorld - rb.position;
        toTarget.y = 0f; // alleen over de grond bewegen

        if (toTarget.magnitude <= arriveThreshold)
        {
            // Aangekomen: even wachten, daarna nieuw punt.
            StopHorizontal();
            pauseTimer = Random.Range(minPause, maxPause);
            return;
        }

        // Beweeg richting het doelpunt; y-snelheid (zwaartekracht) intact laten,
        // exact zoals DodgeballAgent.OnActionReceived dat doet.
        Vector3 dir = toTarget.normalized;
        Vector3 vel = dir * moveSpeed;
        rb.linearVelocity = new Vector3(vel.x, rb.linearVelocity.y, vel.z);
    }

    private void ChooseNewTarget()
    {
        targetLocal = new Vector3(
            Random.Range(areaMin.x, areaMax.x),
            transform.localPosition.y,
            Random.Range(areaMin.y, areaMax.y)
        );
    }

    private void StopHorizontal()
    {
        if (rb == null) return;
        rb.linearVelocity = new Vector3(0f, rb.linearVelocity.y, 0f);
    }

    // Lokaal doelpunt -> wereldpositie. Gebruikt arena-parent net als de rest
    // van de codebase; geen parent -> lokaal == wereld.
    private Vector3 LocalToWorld(Vector3 local)
    {
        Transform parent = transform.parent;
        return parent != null ? parent.TransformPoint(local) : local;
    }
}
