using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody))]
public class VRBallThrow : MonoBehaviour
{
    public float throwMultiplier = 2f;
    public float maxSpeed = 12f;

    private Rigidbody rb;
    private UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab;

    private Vector3 lastPos;
    private Vector3 velocity;

    private bool held;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    void Update()
    {
        if (!held) return;

        velocity = (transform.position - lastPos) / Time.deltaTime;
        lastPos = transform.position;
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        held = true;
        lastPos = transform.position;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        held = false;

        Vector3 throwVel = velocity * throwMultiplier;

        throwVel = Vector3.ClampMagnitude(throwVel, maxSpeed);

        rb.linearVelocity = throwVel;
    }
}