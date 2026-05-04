using UnityEngine;

public class Ball_Catching : MonoBehaviour
{
    [Header("Lifetime")]
    public float lifetime = 5f;

    [HideInInspector]
    public BallSpawner_Catching spawner;

    private float timer;

    private void OnEnable()
    {
        timer = 0f;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        // failsafe destroy
        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        // notify spawner safely
        if (spawner != null)
        {
            spawner.NotifyBallDestroyed();
        }
    }
}