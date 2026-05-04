using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private Transform target;
    [SerializeField] private float spawnInterval = 1.5f;
    [SerializeField] private float ballSpeed = 8f;
    [SerializeField] private float speedVariation = 2f;
    [SerializeField] private float aimNoise = 1f;
    [SerializeField] private float[] spawnAngles = new float[] { 0f, 90f, 180f, 270f };
    [SerializeField] private float spawnRadius = 4.5f;

    private float timer;

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= spawnInterval)
        {
            timer = 0f;
            SpawnBall();
        }
    }

    private void SpawnBall()
    {
        if (ballPrefab == null || target == null) return;
        if (spawnAngles == null || spawnAngles.Length == 0) return;

        Vector3 arenaCenter = transform.parent != null ? transform.parent.position : transform.position;
        float angleDeg = spawnAngles[Random.Range(0, spawnAngles.Length)];
        float angleRad = angleDeg * Mathf.Deg2Rad;
        Vector3 spawnPos = new Vector3(
            arenaCenter.x + Mathf.Sin(angleRad) * spawnRadius,
            1f,
            arenaCenter.z + Mathf.Cos(angleRad) * spawnRadius
        );

        GameObject ball = Instantiate(ballPrefab, spawnPos, Quaternion.identity);
        if (transform.parent != null)
        {
            ball.transform.SetParent(transform.parent, true);
        }

        // Beetje ruis zorgt ervoor dat het niet gooit met aimbot
        Vector3 aimPoint = target.position + new Vector3(
            Random.Range(-aimNoise, aimNoise),
            0f,
            Random.Range(-aimNoise, aimNoise)
        );
        // Bepaald richting van de bal
        Vector3 direction = (aimPoint - spawnPos).normalized;

        float speed = ballSpeed + Random.Range(-speedVariation, speedVariation);
        ball.GetComponent<Rigidbody>().linearVelocity = direction * speed;
    }

    public void ResetSpawner()
    {
        timer = 0f;
    }
}