using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class CatchAgent : Agent
{
    [Header("Ball")]
    public Transform ball;

    [Header("Catch Zone")]
    public Renderer catchZoneRenderer;

    // ---------------- STATE ----------------
    private bool isCatching;
    private bool lastCatching;

    // rotation anti-spam
    private int lastRotation = -1;

    // catch cooldown (BELANGRIJK)
    private float catchCooldown = 0f;

    // ---------------- EPISODE ----------------
    public override void OnEpisodeBegin()
    {
        isCatching = false;
        lastCatching = false;

        lastRotation = -1;
        catchCooldown = 0f;

        // start fixed orientation (2-direction setup)
        transform.rotation = Quaternion.Euler(0f, 90f, 0f);

        if (catchZoneRenderer != null)
            catchZoneRenderer.material.color = Color.white;
    }

    // ---------------- OBSERVATIONS ----------------
    public override void CollectObservations(VectorSensor sensor)
    {
        // catch state
        sensor.AddObservation(isCatching ? 1f : 0f);

        if (ball != null)
        {
            Vector3 toBall = ball.position - transform.position;
            Rigidbody rb = ball.GetComponent<Rigidbody>();

            sensor.AddObservation(toBall.normalized); // direction (3)
            sensor.AddObservation(toBall.magnitude / 15f); // distance (1)
            sensor.AddObservation(rb != null ? rb.linearVelocity / 10f : Vector3.zero); // velocity (3)
        }
        else
        {
            sensor.AddObservation(Vector3.zero);
            sensor.AddObservation(0f);
            sensor.AddObservation(Vector3.zero);
        }

        // facing direction
        sensor.AddObservation(transform.forward); // (3)
    }

    // ---------------- ACTIONS ----------------
    public override void OnActionReceived(ActionBuffers actions)
    {
        int rotateAction = actions.DiscreteActions[0];
        bool catchAttempt = actions.DiscreteActions[1] == 1;

        // cooldown tick
        catchCooldown -= Time.deltaTime;

        // ---------------- ROTATION (2 FIXED DIRECTIONS) ----------------
        if (rotateAction == 0)
            transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        else
            transform.rotation = Quaternion.Euler(0f, 270f, 0f);

        // penalty voor flip-spam
        if (rotateAction != lastRotation)
            AddReward(-0.005f);

        lastRotation = rotateAction;

        // ---------------- BASE REWARD ----------------
        AddReward(-0.001f); // time penalty

        // ---------------- LEARNING SIGNAL ----------------
        if (ball != null)
        {
            Vector3 toBall = (ball.position - transform.position).normalized;
            float alignment = Vector3.Dot(transform.forward, toBall);
            float dist = Vector3.Distance(transform.position, ball.position);

            // stabiel leren kijken
            if (alignment < 0)
                AddReward(alignment * 0.03f);
            else
                AddReward(alignment * 0.01f);

            // goede positie bonus
            if (dist < 5f && alignment > 0.85f)
                AddReward(0.01f);

            // ---------------- CATCH LOGIC ----------------
            if (catchAttempt && catchCooldown <= 0f)
            {
                if (alignment > 0.85f)
                {
                    AddReward(0.05f);   // goede catch
                    catchCooldown = 0.5f;
                }
                else
                {
                    AddReward(-0.02f);  // slechte catch
                    catchCooldown = 0.2f;
                }
            }
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

        AddReward(1f);
        Debug.Log("SUCCESSFUL CATCH");

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
        actionsOut.DiscreteActions.Array[0] =
            Input.GetKey(KeyCode.A) ? 0 : 1;

        actionsOut.DiscreteActions.Array[1] =
            Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    // ---------------- DEBUG ----------------
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}