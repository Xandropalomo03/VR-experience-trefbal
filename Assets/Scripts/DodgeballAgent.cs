using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class DodgeballAgent : Agent
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private BallSpawner ballSpawner;

    private Rigidbody rb;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = new Vector3(
            Random.Range(-4f, 4f),
            1f,
            Random.Range(-4f, 4f)
        );
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        if (ballSpawner != null)
        {
            ballSpawner.ResetSpawner();
        }

        DestroyAllBalls();
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        sensor.AddObservation(transform.localPosition.x / 5f);
        sensor.AddObservation(transform.localPosition.z / 5f);
        sensor.AddObservation(rb.linearVelocity.x / moveSpeed);
        sensor.AddObservation(rb.linearVelocity.z / moveSpeed);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        Vector3 movement = new Vector3(moveX, 0f, moveZ) * moveSpeed;
        rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);

        // Kleine survival reward per stap
        AddReward(0.001f);
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxis("Horizontal");
        continuousActions[1] = Input.GetAxis("Vertical");
    }

    public void OnHitByBall()
    {
        AddReward(-1f);
        EndEpisode();
    }

    private void DestroyAllBalls()
    {
        GameObject[] balls = GameObject.FindGameObjectsWithTag("Ball");
        foreach (GameObject ball in balls)
        {
            Destroy(ball);
        }
    }
}