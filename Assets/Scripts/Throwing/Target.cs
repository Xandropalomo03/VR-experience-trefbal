using UnityEngine;

// Het doel waarop de agent moet gooien.
[RequireComponent(typeof(Collider))]
public class Target : MonoBehaviour
{
    private float moveSpeed;
    private float moveRange = 2f;
    private Vector3 basePosition;
    private float moveT;
    private int moveDir = 1;
    private Vector3 prevPosition;
    private Vector3 currentVelocity;

    public Vector3 CurrentVelocity => currentVelocity;

    // Reset target op tegenoverliggende helft op gegeven afstand en grootte.
    public void ResetTarget(float distance, float size, float speed)
    {
        moveSpeed = speed;
        moveT = 0f;
        moveDir = Random.value < 0.5f ? -1 : 1;

        // Positie op tegenoverliggende helft (z > 1)
        float z = Mathf.Max(1f, distance);
        float xRange = Mathf.Max(0.5f, 3f);
        float x = Random.Range(-xRange, xRange);

        transform.localPosition = new Vector3(x, size * 0.5f, z);
        basePosition = transform.localPosition;

        transform.localScale = new Vector3(size, size, size);
        prevPosition = transform.position;
        currentVelocity = Vector3.zero;
    }

    private void FixedUpdate()
    {
        if (moveSpeed > 0f)
        {
            // Heen en weer langs X-as
            moveT += Time.fixedDeltaTime * moveSpeed * moveDir;
            float x = basePosition.x + Mathf.Sin(moveT) * moveRange;
            Vector3 lp = transform.localPosition;
            lp.x = Mathf.Clamp(x, basePosition.x - moveRange, basePosition.x + moveRange);
            transform.localPosition = lp;
        }

        // Snelheid bijhouden voor observation
        Vector3 cur = transform.position;
        currentVelocity = (cur - prevPosition) / Mathf.Max(Time.fixedDeltaTime, 1e-5f);
        prevPosition = cur;
    }

    // Door ThrowableBall aangeroepen bij raken.
    public void OnBallHit(Vector3 hitPoint, ThrowingAgent agent)
    {
        if (agent != null)
            agent.NotifyTargetHit(hitPoint);
    }
}
