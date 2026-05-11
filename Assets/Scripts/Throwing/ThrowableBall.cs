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
    private bool collisionHandled;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    public void AttachTo(Transform holderTransform, ThrowingAgent agent)
    {
        holder = holderTransform;
        owner = agent;
        state = State.Held;
        collisionHandled = false;

        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (holder != null)
            transform.position = holder.position;
    }

    public void Release(Vector3 velocity)
    {
        state = State.Thrown;
        rb.isKinematic = false;
        rb.linearVelocity = velocity;
    }

    private void FixedUpdate()
    {
        // In Held mode volgt de bal de hand
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

        // Target laag check via Target component op object of parent
        Target tgt = other.GetComponentInParent<Target>();
        if (tgt != null)
        {
            collisionHandled = true;
            tgt.OnBallHit(hitPoint, owner);
            return;
        }

        // Anders: grond of muur = miss
        collisionHandled = true;
        if (owner != null)
            owner.NotifyMiss();
    }
}
