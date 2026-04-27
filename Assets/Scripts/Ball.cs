using UnityEngine;

public class Ball : MonoBehaviour
{
    [SerializeField] private float lifetime = 4f;

    private float timer;

    private void OnEnable()
    {
        timer = 0f;
    }

    private void Update()
    {
        timer += Time.deltaTime;
        if (timer >= lifetime)
        {
            Destroy(gameObject);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        DodgeballAgent agent = collision.gameObject.GetComponent<DodgeballAgent>();
        if (agent != null)
        {
            agent.OnHitByBall();
            Destroy(gameObject);
        }
    }
}