using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CatchAgent : Agent
{
    [Header("Ball")]
    public Transform ball;

    [Header("Catch Zone (debug + visuals)")]
    public Transform catchZone;
    public Renderer catchZoneRenderer;

    private bool isCatching;
    private bool lastCatching;

    // ---------------- EPISODE ----------------
    public override void OnEpisodeBegin()
    {
        isCatching = false;
        lastCatching = false;

        if (catchZoneRenderer != null)
            catchZoneRenderer.material.color = Color.white;
    }

    // ---------------- OBSERVATIONS ----------------
    public override void CollectObservations(VectorSensor sensor)
    {
        // state
        sensor.AddObservation(isCatching ? 1f : 0f);

        if (ball != null)
        {
            Vector3 toBall = ball.position - transform.position;
            Rigidbody rb = ball.GetComponent<Rigidbody>();

            // richting
            sensor.AddObservation(toBall.normalized);

            // afstand
            sensor.AddObservation(toBall.magnitude / 15f);

            // snelheid
            sensor.AddObservation(rb != null ? rb.linearVelocity / 10f : Vector3.zero);
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
            sensor.AddObservation(Vector3.zero);
        }
    }

    // ---------------- ACTIONS ----------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        int action = actions.DiscreteActions[0];

        isCatching = action == 1;

        // BASE TIME PENALTY
        AddReward(-0.001f);

        // ANTI-SPAM PENALTY (constant catch gedragingen)
        if (isCatching)
        {
            AddReward(-0.005f);
        }

        // TOGGLE SPAM PENALTY (flikkeren tussen aan/uit)
        if (isCatching != lastCatching)
        {
            AddReward(-0.01f);
        }

        lastCatching = isCatching;

        // VISUAL DEBUG
        if (catchZoneRenderer != null)
        {
            catchZoneRenderer.material.color =
                isCatching ? Color.yellow : Color.white;
        }
    }

    // ---------------- SUCCESS / FAIL ----------------
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        if (isCatching)
        {
            Debug.Log("SUCCESSFUL CATCH");
            AddReward(1f);
        }
        else
        {
            Debug.Log("HIT (no catch)");
            AddReward(-1f);
        }

        Destroy(other.gameObject);
        EndEpisode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Ball")) return;

        Debug.Log("HARD HIT");
        AddReward(-1f);

        Destroy(collision.gameObject);
        EndEpisode();
    }

    // ---------------- HEURISTIC ----------------
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        actionsOut.DiscreteActions.Array[0] =
            Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    // ---------------- DEBUG VISUAL ----------------
    private void OnDrawGizmos()
    {
        if (catchZone != null)
        {
            Gizmos.color = isCatching ? Color.yellow : Color.red;
            Gizmos.DrawWireSphere(catchZone.position, 0.3f);
        }
    }
}