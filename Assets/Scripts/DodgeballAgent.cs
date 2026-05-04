using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class DodgeballAgent : Agent
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private BallSpawner ballSpawner;

    private Rigidbody rb;
    private Transform myArena;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        myArena = transform.parent;
    }

    public override void OnEpisodeBegin()
    {
        transform.localPosition = new Vector3(
            Random.Range(-4f, 4f),
            1f,
            Random.Range(-4f, 4f)
        );
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
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

        GameObject closestBall = FindClosestBall();
        if (closestBall != null)
        {
            Vector3 ballRelative = closestBall.transform.position - transform.position;
            Vector3 ballVel = closestBall.GetComponent<Rigidbody>().linearVelocity;

            sensor.AddObservation(ballRelative.x / 10f);
            sensor.AddObservation(ballRelative.z / 10f);
            sensor.AddObservation(ballVel.x / 10f);
            sensor.AddObservation(ballVel.z / 10f);
            sensor.AddObservation(1f);
        }
        else
        {
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
            sensor.AddObservation(0f);
        }
    }

private GameObject FindClosestBall()
{
    GameObject[] balls = GameObject.FindGameObjectsWithTag("Ball");
    if (balls.Length == 0) return null;

    GameObject closest = null;
    float minDist = Mathf.Infinity;
    foreach (GameObject ball in balls)
    {
        if (myArena != null && ball.transform.parent != myArena) continue;

        float dist = Vector3.Distance(transform.position, ball.transform.position);
        if (dist < minDist)
        {
            minDist = dist;
            closest = ball;
        }
    }
    return closest;
}

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (StepCount % 100 == 0)
            Debug.Log($"step={StepCount} action=({actions.ContinuousActions[0]:F2}, {actions.ContinuousActions[1]:F2}) vel={rb.linearVelocity.magnitude:F2}");

        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        Vector3 movement = new Vector3(moveX, 0f, moveZ) * moveSpeed;
        rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);

        // Kleine survival reward per stap
        AddReward(0.005f); // was 0.001f
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
            if (myArena != null && ball.transform.parent != myArena) continue;
            Destroy(ball);
        }
    }
}