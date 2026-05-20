using UnityEngine;

public class BallSpawner_Catching : MonoBehaviour
{
    [Header("Setup")]
    public GameObject ballPrefab;
    public Transform target;

    [Header("Random X Spawn")]
    public float minX = -8f;
    public float maxX = 8f;

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
        // wacht tot vorige bal weg is
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
        nextSpawnTime = Random.Range(
            minSpawnInterval,
            maxSpawnInterval
        );
    }

    private void SpawnBall()
    {
        if (ballPrefab == null || target == null)
            return;

        // pak huidige positie van spawner
        Vector3 spawnPosition = transform.position;

        // random X offset
        spawnPosition.x += Random.Range(minX, maxX);

        // zelfde hoogte behouden
        spawnPosition.y = transform.position.y;

        // zelfde Z behouden
        spawnPosition.z = transform.position.z;

        // spawn bal
        currentBall = Instantiate(
            ballPrefab,
            spawnPosition,
            Quaternion.identity
        );

        // link terug naar spawner
        Ball_Catching ballScript =
            currentBall.GetComponent<Ball_Catching>();

        if (ballScript != null)
        {
            ballScript.spawner = this;
        }

        // richting naar target
        Vector3 direction =
            target.position - spawnPosition;

        direction.y = 0f;
        direction.Normalize();

        float speed =
            Random.Range(minSpeed, maxSpeed);

        Rigidbody rb =
            currentBall.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.linearVelocity = direction * speed;
        }
    }

    public void NotifyBallDestroyed()
    {
        currentBall = null;
    }

    // debug lijnen in scene view
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;

        Vector3 left =
            transform.position + Vector3.right * minX;

        Vector3 right =
            transform.position + Vector3.right * maxX;

        Gizmos.DrawSphere(left, 0.3f);
        Gizmos.DrawSphere(right, 0.3f);

        Gizmos.DrawLine(left, right);
    }
}