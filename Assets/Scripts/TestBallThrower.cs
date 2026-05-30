using UnityEngine;

// ============================================================================
// TestBallThrower  (TIJDELIJK testhulpmiddel)
// ----------------------------------------------------------------------------
// Lanceert op een toetsdruk (default = spatie) een bal richting de agent, zodat
// je de idle->dodge loop in de Editor kunt testen ZONDER VR-bril. Bewust los
// van de echte BallSpawner. Weghalen zodra VR-gooien werkt.
//
// De bal komt elke druk uit een willekeurige horizontale richting op
// spawnDistance van de agent, en wordt richting de agent gemikt (met een beetje
// ruis, net als BallSpawner). De bal krijgt de "Ball"-tag via het prefab, dus
// MatchCoordinator pikt 'm vanzelf op.
//
// WAAR PLAATSEN
//   Op een leeg "TestBallThrower" GameObject in de game-scene (of gewoon naast
//   de MatchCoordinator).
//
// REFERENTIES WIREN (Inspector)
//   - ballPrefab : Assets/Prefabs/Ball.prefab  (heeft Rigidbody + tag "Ball").
//   - target     : de agent-body (waar de bal naartoe gemikt wordt).
//   Overige velden hebben werkbare defaults; pas snelheid/afstand naar smaak.
//
// GEBRUIK
//   Speel de scene af en druk op spatie. De agent hoort van idle naar dodge te
//   springen terwijl de bal nadert, en daarna terug naar idle.
// ============================================================================
public class TestBallThrower : MonoBehaviour
{
    [Header("Referenties")]
    [Tooltip("Ball.prefab (Rigidbody + tag 'Ball').")]
    [SerializeField] private GameObject ballPrefab;
    [Tooltip("De agent-body waar de bal naartoe gegooid wordt.")]
    [SerializeField] private Transform target;

    [Header("Toets")]
    [SerializeField] private KeyCode launchKey = KeyCode.Space;

    [Header("Richting")]
    [Tooltip("TIJDELIJK: gooi recht vanaf de voorkant van de agent (+z / agent-" +
             "forward) i.p.v. een willekeurige richting. Zo komt de bal de catch-" +
             "zone in en lukt de catch -> throw keten betrouwbaar. Uitzetten = " +
             "willekeurige richtingen (origineel gedrag).")]
    [SerializeField] private bool throwFromFront = true;

    [Header("Worp-instellingen")]
    [Tooltip("Afstand (meter) vanwaar de bal vertrekt, rond de agent.")]
    [SerializeField] private float spawnDistance = 8f;
    [Tooltip("Hoogte (meter) waarop de bal vertrekt.")]
    [SerializeField] private float spawnHeight = 1f;
    [Tooltip("Balsnelheid (m/s).")]
    [SerializeField] private float ballSpeed = 8f;
    [Tooltip("Ruis op het mikpunt zodat het geen aimbot is.")]
    [SerializeField] private float aimNoise = 0.5f;

    private void Update()
    {
        if (Input.GetKeyDown(launchKey))
            LaunchBall();
    }

    private void LaunchBall()
    {
        if (ballPrefab == null || target == null)
        {
            Debug.LogWarning("TestBallThrower: ballPrefab of target niet gewired.");
            return;
        }

        Vector3 agentPos = target.position;

        // Vertrekpunt rond de agent: vooraan of in een willekeurige richting.
        Vector3 spawnDir;
        if (throwFromFront)
        {
            // Recht voor de agent (agent-forward, ~+z). De catch-zone zit aan
            // diezelfde voorkant, dus de bal komt netjes de zone in.
            spawnDir = target.forward;
            spawnDir.y = 0f;
            if (spawnDir.sqrMagnitude < 0.0001f) spawnDir = Vector3.forward;
            spawnDir.Normalize();
        }
        else
        {
            // Willekeurige horizontale richting rond de agent.
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            spawnDir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        }

        Vector3 spawnPos = new Vector3(
            agentPos.x + spawnDir.x * spawnDistance,
            spawnHeight,
            agentPos.z + spawnDir.z * spawnDistance
        );

        GameObject ball = Instantiate(ballPrefab, spawnPos, Quaternion.identity);

        // In dezelfde arena hangen als de agent, zodat MatchCoordinator's
        // arena-filter en DodgeballAgent's cleanup hetzelfde werken.
        if (target.parent != null)
            ball.transform.SetParent(target.parent, true);

        // Mik op de agent, op vertrek-hoogte, met wat ruis (geen aimbot).
        Vector3 aimPoint = new Vector3(
            agentPos.x + Random.Range(-aimNoise, aimNoise),
            spawnHeight,
            agentPos.z + Random.Range(-aimNoise, aimNoise)
        );
        Vector3 direction = (aimPoint - spawnPos).normalized;

        if (ball.TryGetComponent(out Rigidbody rb))
            rb.linearVelocity = direction * ballSpeed;
        else
            Debug.LogWarning("TestBallThrower: ball-prefab heeft geen Rigidbody.");

        DebugLogger.Log("TEST", $"bal gelanceerd vanaf {spawnPos} richting agent ({ballSpeed} m/s)");
    }
}
