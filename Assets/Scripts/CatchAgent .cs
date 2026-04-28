using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CatchAgent : Agent
{
    [Header("Ball")]
    public Transform ball;

    private bool isCatching;

    [Header("Catch Zone (debug only)")]
    public Transform catchZone; // alleen voor visuals/debug

    // ---------------- EPISODE ----------------
    public override void OnEpisodeBegin()
    {
        isCatching = false;
    }

    // ---------------- OBSERVATIONS ----------------
    public override void CollectObservations(VectorSensor sensor)
    {
        // 1. huidige state
        sensor.AddObservation(isCatching ? 1f : 0f);

        if (ball != null)
        {
            Vector3 toBall = ball.position - transform.position;

            Rigidbody rb = ball.GetComponent<Rigidbody>();

            // 2. richting naar bal
            sensor.AddObservation(toBall.normalized);

            // 3. afstand (genormaliseerd)
            sensor.AddObservation(toBall.magnitude / 15f);

            // 4. snelheid van bal
            sensor.AddObservation(rb != null ? rb.linearVelocity / 10f : Vector3.zero);
        }
        else
        {
            // fallback
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

        AddReward(-0.001f); // tijd penalty
    }

    // ---------------- UNITY VALIDATION (IMPORTANT) ----------------
    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Ball")) return;

        bool validCatch = isCatching;

        if (validCatch)
        {
            // ✔ goede catch
            AddReward(1f);
        }
        else
        {
            // ❌ geraakt zonder catch
            AddReward(-1f);
        }

        Destroy(other.gameObject);
        EndEpisode();
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!collision.gameObject.CompareTag("Ball")) return;

        // extra fail case (hard hit)
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
            Gizmos.color = isCatching ? Color.green : Color.red;
            Gizmos.DrawWireSphere(catchZone.position, 0.3f);
        }
    }
}