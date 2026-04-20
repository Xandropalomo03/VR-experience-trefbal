using UnityEngine;

public class BallSpawner : MonoBehaviour
{
    [SerializeField] private GameObject ballPrefab;
    [SerializeField] private Transform target;
    [SerializeField] private float spawnInterval = 1.5f;
    [SerializeField] private float ballSpeed = 8f;
    [SerializeField] private float speedVariation = 2f;
    [SerializeField] private float aimNoise = 1f;

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

        GameObject ball = Instantiate(ballPrefab, transform.position, Quaternion.identity);

        // Beetje ruis zorgt ervoor dat het niet gooit met aimbot
        Vector3 aimPoint = target.position + new Vector3(
            Random.Range(-aimNoise, aimNoise),
            0f,
            Random.Range(-aimNoise, aimNoise)
        );
        // Bepaald richting van de bal
        Vector3 direction = (aimPoint - transform.position).normalized;

        float speed = ballSpeed + Random.Range(-speedVariation, speedVariation);
        ball.GetComponent<Rigidbody>().linearVelocity = direction * speed;
    }

    public void ResetSpawner()
    {
        timer = 0f;
    }
}