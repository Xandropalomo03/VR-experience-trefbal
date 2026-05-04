using UnityEngine;

public class BallSpawner_Catching : MonoBehaviour
{
    [Header("Setup")]
    public GameObject ballPrefab;
    public Transform target;
    public Transform[] spawnPoints;

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
        if (ballPrefab == null || target == null || spawnPoints.Length == 0)
            return;

        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];

        currentBall = Instantiate(ballPrefab, spawnPoint.position, Quaternion.identity);

        // IMPORTANT: link ball → spawner
        Ball_Catching ballScript = currentBall.GetComponent<Ball_Catching>();
        if (ballScript != null)
        {
            ballScript.spawner = this;
        }

        Vector3 direction = target.position - spawnPoint.position;
        direction.y = 0f;
        direction = direction.normalized;

        float speed = Random.Range(minSpeed, maxSpeed);

        Rigidbody rb = currentBall.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = direction * speed;
        }
    }

    // called by Ball_Catching
    public void NotifyBallDestroyed()
    {
        currentBall = null;
    }
}