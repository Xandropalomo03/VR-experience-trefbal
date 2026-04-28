using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    [Header("Setup")]
    public GameObject ballPrefab;
    public Transform target;

    [Header("Spawn Timing")]
    public float minSpawnInterval = 0.5f;
    public float maxSpawnInterval = 1.5f;

    [Header("Speed")]
    public float minSpeed = 6f;
    public float maxSpeed = 10f;

    private float timer;
    private float nextSpawnTime;

    private void Start()
    {
        SetNextSpawnTime();
    }

    private void Update()
    {
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
        if (ballPrefab == null || target == null) return;

        GameObject ball = Instantiate(ballPrefab, transform.position, Quaternion.identity);

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;
        direction = direction.normalized;

        float speed = Random.Range(minSpeed, maxSpeed);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = direction * speed;
        }
    }
}