using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CatchAgent : Agent
{
    [Header("Catch Zone")]
    public Renderer catchZoneRenderer;

    private bool attemptedCatch;
    private int lastRotation = -1;

    // ---------------- EPISODE ----------------
    public override void OnEpisodeBegin()
    {
        attemptedCatch = false;
        lastRotation = -1;

        transform.rotation = Quaternion.Euler(0f, 0f, 0f);

        if (catchZoneRenderer != null)
            catchZoneRenderer.material.color = Color.white;
    }

    // ---------------- OBSERVATIONS ----------------
    // RayPerception doet alles
    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(0f); // placeholder (only ray-based perception)
    }

    // ---------------- ACTIONS ----------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        int rotateAction = actions.DiscreteActions[0];
        bool catchAttempt = actions.DiscreteActions[1] == 1;

        attemptedCatch = catchAttempt;

        // ---------------- ROTATION (3 SNAP) ----------------
        if (rotateAction == 0)
            transform.rotation = Quaternion.Euler(0f, -45f, 0f);
        else if (rotateAction == 1)
            transform.rotation = Quaternion.Euler(0f, 0f, 0f);
        else if (rotateAction == 2)
            transform.rotation = Quaternion.Euler(0f, 45f, 0f);

        // small rotation penalty (prevents jitter)
        if (rotateAction != lastRotation && lastRotation != -1)
            AddReward(-0.001f);

        lastRotation = rotateAction;

        // ---------------- CATCH SPAM PENALTY ----------------
        // elke keer SPACE wordt gebruikt = kleine straf
        if (catchAttempt)
        {
            AddReward(-0.02f); // <-- spam penalty (belangrijk)
        }

        // ---------------- VISUAL ----------------
        if (catchZoneRenderer != null)
        {
            catchZoneRenderer.material.color =
                catchAttempt ? Color.yellow : Color.white;
        }
    }

    // ---------------- SUCCESS / FAIL ----------------
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        if (attemptedCatch)
        {
            AddReward(1f);
            Debug.Log("SUCCESSFUL CATCH");
        }
        else
        {
            AddReward(-1f);
            Debug.Log("MISS / NO CATCH");
        }

        Destroy(other.gameObject);
        EndEpisode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Ball")) return;

        AddReward(-1f);
        Debug.Log("HARD HIT");

        Destroy(collision.gameObject);
        EndEpisode();
    }

    // ---------------- HEURISTIC ----------------
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        if (Input.GetKey(KeyCode.A))
            actionsOut.DiscreteActions.Array[0] = 0;
        else if (Input.GetKey(KeyCode.D))
            actionsOut.DiscreteActions.Array[0] = 2;
        else
            actionsOut.DiscreteActions.Array[0] = 1;

        actionsOut.DiscreteActions.Array[1] =
            Input.GetKey(KeyCode.Space) ? 1 : 0;
    }
}