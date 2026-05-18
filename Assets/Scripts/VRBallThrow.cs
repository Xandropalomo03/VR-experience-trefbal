using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(Rigidbody))]
public class VRBallThrow : MonoBehaviour
{
    public float throwMultiplier = 2f;
    public float maxSpeed = 12f;

    [Tooltip("Hoeveel frames we middelen voor een stabielere gooi")]
    public int velocityFrames = 8;

    private Rigidbody rb;
    private XRGrabInteractable grab;

    private Vector3[] velocityBuffer;
    private int bufferIndex;
    private Vector3 lastPos;
    private bool held;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        grab = GetComponent<XRGrabInteractable>();

        velocityBuffer = new Vector3[velocityFrames];

        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);
    }

    void Update()
    {
        if (!held) return;

        Vector3 frameVelocity = (transform.position - lastPos) / Time.deltaTime;
        lastPos = transform.position;

        // Sla op in circulaire buffer
        velocityBuffer[bufferIndex % velocityFrames] = frameVelocity;
        bufferIndex++;
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        held = true;
        lastPos = transform.position;
        bufferIndex = 0;

        // Buffer leegmaken
        for (int i = 0; i < velocityFrames; i++)
            velocityBuffer[i] = Vector3.zero;

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    void OnRelease(SelectExitEventArgs args)
    {
        held = false;

        // Gemiddelde van alle gebufferde snelheden
        Vector3 avgVelocity = Vector3.zero;
        foreach (Vector3 v in velocityBuffer)
            avgVelocity += v;
        avgVelocity /= velocityFrames;

        Vector3 throwVel = Vector3.ClampMagnitude(avgVelocity * throwMultiplier, maxSpeed);
        rb.linearVelocity = throwVel;
    }
}