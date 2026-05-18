using UnityEngine;

public class BallSpawner_Catching : MonoBehaviour
{
    [Header("Setup")]
    public GameObject ballPrefab;
    public Transform target;

    [Header("Spawn Area")]
    public float spawnRadius = 8f;
    public float arcAngle = 120f; // totale boog in graden

    [Header("Spawn Timing")]
    public float minSpawnInterval = 0.5f;
    public float maxSpawnInterval = 1.5f;

    [Header("Speed")]
    public float minSpeed = 6f;
    public float maxSpeed = 10f;

    private float timer;
    private float nextSpawnTime;
    private GameObject currentBall;

    private void Start()
    {
        SetNextSpawnTime();
    }

    private void Update()
    {
        // Spawn pas nieuwe bal als vorige weg is
        if (currentBall != null) return;

        timer += Time.deltaTime;

        if (timer >= nextSpawnTime)
        {
            timer = 0f;
            SetNextSpawnTime();
            SpawnBall();
        }
    }

    private void SetNextSpawnTime()
    {
        nextSpawnTime = Random.Range(minSpawnInterval, maxSpawnInterval);
    }

    private void SpawnBall()
    {
        if (ballPrefab == null || target == null)
            return;

        float halfArc = arcAngle / 2f;
        float randomAngle = Random.Range(-halfArc, halfArc);

        // Spawn vóór agent in symmetrische boog
        Vector3 baseForward = target.forward;

        Vector3 direction =
            Quaternion.Euler(0f, randomAngle, 0f) * baseForward;

        Vector3 spawnPosition =
            target.position + direction.normalized * spawnRadius;

        spawnPosition.y = target.position.y;

        currentBall = Instantiate(
            ballPrefab,
            spawnPosition,
            Quaternion.identity
        );

        // Link bal terug naar spawner
        Ball_Catching ballScript = currentBall.GetComponent<Ball_Catching>();
        if (ballScript != null)
        {
            ballScript.spawner = this;
        }

        // Laat bal naar target vliegen
        Vector3 throwDirection = target.position - spawnPosition;
        throwDirection.y = 0f;
        throwDirection.Normalize();

        float speed = Random.Range(minSpeed, maxSpeed);

        Rigidbody rb = currentBall.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = throwDirection * speed;
        }
    }

    public void NotifyBallDestroyed()
    {
        currentBall = null;
    }

    // Debug visualisatie
    private void OnDrawGizmos()
    {
        if (target == null) return;

        // Midden van boog
        Gizmos.color = Color.green;
        Gizmos.DrawRay(target.position, target.forward * 3f);

        // Achterkant
        Gizmos.color = Color.red;
        Gizmos.DrawRay(target.position, -target.forward * 3f);

        // Grenzen boog
        Gizmos.color = Color.yellow;

        float halfArc = arcAngle / 2f;

        Vector3 leftDir =
            Quaternion.Euler(0f, -halfArc, 0f) * target.forward;

        Vector3 rightDir =
            Quaternion.Euler(0f, halfArc, 0f) * target.forward;

        Gizmos.DrawRay(target.position, leftDir * spawnRadius);
        Gizmos.DrawRay(target.position, rightDir * spawnRadius);
    }
}