using UnityEngine;

// Bal die door de ThrowingAgent gegooid wordt.
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class ThrowableBall : MonoBehaviour
{
    private enum State { Held, Thrown }
    private State state = State.Held;

    private Transform holder;
    private ThrowingAgent owner;
    private Rigidbody rb;
    private Collider ballCollider;
    private Collider agentCollider;
    private bool collisionHandled;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ballCollider = GetComponent<Collider>();
    }

    public void AttachTo(Transform holderTransform, ThrowingAgent agent, Collider agentCol = null)
    {
        holder = holderTransform;
        owner = agent;
        agentCollider = agentCol;
        state = State.Held;
        collisionHandled = false;

        if (rb == null) rb = GetComponent<Rigidbody>();

        // Eerst velocities nullen, DAARNA pas kinematic zetten — anders krijg je warnings.
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        if (holder != null)
            transform.position = holder.position;
    }

    public void Release(Vector3 velocity)
    {
        state = State.Thrown;

        // Spawn de bal 0.3m vooruit in worprichting, anders zit hij nog in/tegen de agent.
        Vector3 horizDir = new Vector3(velocity.x, 0f, velocity.z);
        if (horizDir.sqrMagnitude > 0.0001f && holder != null)
        {
            horizDir.Normalize();
            transform.position = holder.position + horizDir * 0.3f;
        }

        // Negeer botsing met agent collider — voorkomt instant miss bij release.
        if (ballCollider != null && agentCollider != null)
            Physics.IgnoreCollision(ballCollider, agentCollider, true);

        rb.isKinematic = false;
        rb.linearVelocity = velocity;
        rb.angularVelocity = Vector3.zero;
    }

    private void FixedUpdate()
    {
        // Held: bal volgt de hand puur via transform, geen velocity manipulatie.
        if (state == State.Held && holder != null)
        {
            transform.position = holder.position;
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (state != State.Thrown || collisionHandled) return;

        GameObject other = collision.gameObject;
        Vector3 hitPoint = collision.contacts.Length > 0 ? collision.contacts[0].point : transform.position;

        // Eerst checken of we het target raken
        Target tgt = other.GetComponentInParent<Target>();
        if (tgt != null)
        {
            collisionHandled = true;
            tgt.OnBallHit(hitPoint, owner);
            return;
        }

        // Anders: grond/muur = miss
        collisionHandled = true;
        if (owner != null)
            owner.NotifyMiss();
    }
}
