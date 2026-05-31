using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

public class DodgeballAgent : BaseSportAgent
{
    [SerializeField] private float moveSpeed = 4f;
    [SerializeField] private BallSpawner ballSpawner;

    [Tooltip("UIT = training (treffer eindigt de episode, agent reset/teleporteert " +
             "naar een random spawn). AAN = game (vr-omgeving): de agent " +
             "teleporteert NIET bij een reset/brain-switch en eindigt de episode " +
             "niet bij een treffer; de rally speelt door en scoring loopt los via " +
             "BallScore. Zet AAN in de game-scene.")]
    [SerializeField] private bool gameMode = false;

    // rb en myArena worden door BaseSportAgent.Initialize() gevuld.

    public override void OnEpisodeBegin()
    {
        DebugLogger.Log("DODGE", $"OnEpisodeBegin pos={transform.localPosition} spawnerNull={ballSpawner == null} game={gameMode}");

        // In de game NIET herpositioneren: de BrainSwitcher re-enabled de dodge-
        // agent bij elke terugkeer naar idle (na catch/throw), wat OnEpisodeBegin
        // triggert. Teleporteren zou de agent dan elke rally laten verspringen.
        // We behouden de huidige (door de BrainSwitcher overgedragen) positie.
        if (!gameMode)
        {
            transform.localPosition = new Vector3(
                Random.Range(-4f, 4f),
                1f,
                Random.Range(-4f, -1f)
            );
        }
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
        // Snelheid in BODY-frame i.p.v. wereld. De RayPerception draait al mee
        // met de body; door de snelheid ook body-relatief te maken is de hele
        // observatie rotatie-invariant -> het model werkt ongeacht hoe de body
        // (en dus de arena) gedraaid staat. In de trainings-scene staat de body
        // op identity, dus dit is identiek aan het oude wereld-gedrag.
        Vector3 localVel = transform.InverseTransformDirection(rb.linearVelocity);
        sensor.AddObservation(localVel.x / moveSpeed);
        sensor.AddObservation(localVel.z / moveSpeed);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        if (StepCount % 100 == 0)
            Debug.Log($"step={StepCount} action=({actions.ContinuousActions[0]:F2}, {actions.ContinuousActions[1]:F2}) vel={rb.linearVelocity.magnitude:F2}");

        float moveX = actions.ContinuousActions[0];
        float moveZ = actions.ContinuousActions[1];

        // Beweging in BODY-frame (consistent met de body-relatieve observaties).
        // Identity-body in training -> identiek aan het oude wereld-gedrag.
        Vector3 movement = transform.TransformDirection(new Vector3(moveX, 0f, moveZ)) * moveSpeed;
        rb.linearVelocity = new Vector3(movement.x, rb.linearVelocity.y, movement.z);

        // Kleine survival reward per stap
        AddReward(0.01f); // was 0.005f
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var continuousActions = actionsOut.ContinuousActions;
        continuousActions[0] = Input.GetAxis("Horizontal");
        continuousActions[1] = Input.GetAxis("Vertical");
    }

    public void OnHitByBall()
    {
        DebugLogger.Log("DODGE", $"OnHitByBall step={StepCount} game={gameMode}");
        AddReward(-1f);
        // In de game NIET resetten/teleporteren: de speler krijgt z'n punt via
        // BallScore en de rally speelt door. Alleen tijdens training eindigt de
        // episode hier (agent reset).
        if (!gameMode)
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