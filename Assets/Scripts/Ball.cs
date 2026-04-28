using UnityEngine;

public class Ball : MonoBehaviour
{
    [Header("Lifetime")]
    public float lifetime = 5f;

    [HideInInspector]
    public BallSpawner spawner;

    private float timer;

    private void OnEnable()
    {
        timer = 0f;
    }

    private void Update()
    {
        timer += Time.deltaTime;

        // vernietig bal na tijdje (failsafe)
        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        // laat spawner weten dat bal weg is
        if (spawner != null)
        {
            spawner.NotifyBallDestroyed();
        }
    }
}