using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// Agent die leert om een bal naar een doel te gooien.
public class ThrowingAgent : Agent
{
    [Header("References")]
    [SerializeField] private Transform ballHolder;
    [SerializeField] private Target target;
    [SerializeField] private GameObject ballPrefab;

    [Header("Movement")]
    [SerializeField] private float maxMoveSpeed = 4f;

    [Header("Throw mapping")]
    [SerializeField] private float minAngle = 20f;
    [SerializeField] private float maxAngle = 60f;
    [SerializeField] private float minPower = 6f;
    [SerializeField] private float maxPower = 14f;
    [SerializeField] private float maxAimYaw = 45f;

    private Rigidbody rb;
    private Collider agentCollider;
    private ThrowableBall currentBall;
    private bool hasBall;

    // Curriculum waardes, opgehaald per episode
    private float curTargetDistance = 4f;
    private float curTargetSize = 1f;
    private float curTargetSpeed = 0f;
    private float curThrowNoise = 0f;

    // Tracking voor closest-approach reward
    private float closestApproach;
    private bool hasThrown;

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        agentCollider = GetComponent<Collider>();

        // Forceer non-kinematic zodat velocity-based movement werkt.
        if (rb != null && rb.isKinematic)
            rb.isKinematic = false;
    }

    public override void OnEpisodeBegin()
    {
        // Lees curriculum env params
        var envParams = Academy.Instance.EnvironmentParameters;
        curTargetDistance = envParams.GetWithDefault("target_distance", 4f);
        curTargetSize = envParams.GetWithDefault("target_size", 1f);
        curTargetSpeed = envParams.GetWithDefault("target_speed", 0f);
        curThrowNoise = envParams.GetWithDefault("throw_noise", 0f);

        // Spawn agent op eigen helft
        transform.localPosition = new Vector3(
            Random.Range(-3f, 3f),
            1f,
            Random.Range(-4f, -1f)
        );
        transform.localRotation = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Reset target
        if (target != null)
            target.ResetTarget(curTargetDistance, curTargetSize, curTargetSpeed);

        // Reset state
        hasThrown = false;
        closestApproach = float.MaxValue;

        // Geef agent een bal
        SpawnBall();

        Debug.Log("Episode begin, hasBall=" + hasBall);
    }

    private void SpawnBall()
    {
        // Cleanup oude bal
        if (currentBall != null)
        {
            Destroy(currentBall.gameObject);
            currentBall = null;
        }

        if (ballPrefab == null || ballHolder == null) return;

        GameObject ballGo = Instantiate(ballPrefab, ballHolder.position, Quaternion.identity, transform.parent);
        currentBall = ballGo.GetComponent<ThrowableBall>();
        if (currentBall == null)
            currentBall = ballGo.AddComponent<ThrowableBall>();

        currentBall.AttachTo(ballHolder, this, agentCollider);
        hasBall = true;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 targetPos = target != null ? target.transform.position : transform.position;
        Vector3 targetVel = target != null ? target.CurrentVelocity : Vector3.zero;
        Vector3 toTarget = targetPos - transform.position;

        // Relatieve positie target (genormaliseerd)
        sensor.AddObservation(toTarget.x / 10f);
        sensor.AddObservation(toTarget.z / 10f);

        // Target snelheid
        sensor.AddObservation(targetVel.x / 5f);
        sensor.AddObservation(targetVel.z / 5f);

        // Agent rotation als sin/cos
        float yawRad = transform.eulerAngles.y * Mathf.Deg2Rad;
        sensor.AddObservation(Mathf.Sin(yawRad));
        sensor.AddObservation(Mathf.Cos(yawRad));

        // Heeft de agent een bal?
        sensor.AddObservation(hasBall ? 1f : 0f);

        // Tijd in episode
        sensor.AddObservation(MaxStep > 0 ? (float)StepCount / MaxStep : 0f);

        // Afstand tot doel
        sensor.AddObservation(toTarget.magnitude / 10f);

        // Target grootte
        sensor.AddObservation(curTargetSize / 2f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        float moveX = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float aimYaw = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);
        float throwAngle = Mathf.Clamp(actions.ContinuousActions[3], -1f, 1f);
        float throwPower = Mathf.Clamp(actions.ContinuousActions[4], -1f, 1f);

        int trigger = actions.DiscreteActions[0];

        // Movement
        Vector3 vel = new Vector3(moveX, 0f, moveZ) * maxMoveSpeed;
        rb.linearVelocity = new Vector3(vel.x, rb.linearVelocity.y, vel.z);

        // Houd agent op eigen helft (z < 0)
        if (transform.localPosition.z > 0f)
        {
            Vector3 lp = transform.localPosition;
            lp.z = 0f;
            transform.localPosition = lp;
        }

        // Aim wordt niet meer via body rotation toegepast: dat veroorzaakte
        // double-apply samen met de yaw-component in de throw direction.
        // De body rotatie blijft op de waarde van OnEpisodeBegin.

        // Track closest approach voor miss reward
        if (hasThrown && currentBall != null && target != null)
        {
            float d = Vector3.Distance(currentBall.transform.position, target.transform.position);
            if (d < closestApproach) closestApproach = d;
        }

        // Tijd-penalty
        AddReward(-0.001f);

        // Trigger gooi
        if (trigger == 1 && hasBall && !hasThrown)
        {
            ThrowBall(aimYaw, throwAngle, throwPower);
        }

        // Max steps zonder ooit te gooien
        if (StepCount >= MaxStep - 1 && !hasThrown)
        {
            AddReward(-0.5f);
            EndEpisode();
        }
    }

    private void ThrowBall(float yawNorm, float angleNorm, float powerNorm)
    {
        if (currentBall == null) return;

        // Mapping:
        //   yawNorm   = -1 -> -maxAimYaw, 0 -> 0°,         +1 -> +maxAimYaw
        //   angleNorm = -1 -> minAngle,   0 -> middel,     +1 -> maxAngle
        //   powerNorm = -1 -> minPower,   0 -> middel,     +1 -> maxPower
        float yaw = yawNorm * maxAimYaw;
        float elev = Mathf.Lerp(minAngle, maxAngle, (angleNorm + 1f) * 0.5f);
        float power = Mathf.Lerp(minPower, maxPower, (powerNorm + 1f) * 0.5f);

        // Direction berekening in agent-local frame, dan transformeren naar world.
        // Zo werkt het correct ongeacht parent rotation.
        float elevRad = elev * Mathf.Deg2Rad;
        float yawRad = yaw * Mathf.Deg2Rad;
        Vector3 localDir = new Vector3(
            Mathf.Sin(yawRad) * Mathf.Cos(elevRad),
            Mathf.Sin(elevRad),
            Mathf.Cos(yawRad) * Mathf.Cos(elevRad)
        );
        Vector3 throwDir = transform.TransformDirection(localDir);

        // Optionele noise op richting
        if (curThrowNoise > 0f)
        {
            float nx = Random.Range(-curThrowNoise, curThrowNoise);
            float nz = Random.Range(-curThrowNoise, curThrowNoise);
            throwDir += new Vector3(nx, 0f, nz);
            throwDir.Normalize();
        }

        Vector3 velocity = throwDir * power;
        currentBall.Release(velocity);

        hasBall = false;
        hasThrown = true;
        closestApproach = Vector3.Distance(currentBall.transform.position, target.transform.position);

        Debug.Log("Throw computed: angle=" + elev + " power=" + power + " yaw=" + yaw + " velocity=" + velocity);
    }

    // Aangeroepen door ThrowableBall bij raken doel
    public void NotifyTargetHit(Vector3 hitPoint)
    {
        Debug.Log("Hit at " + hitPoint);

        if (target == null)
        {
            EndEpisode();
            return;
        }

        float distToCenter = Vector3.Distance(hitPoint, target.transform.position);

        if (distToCenter < 0.2f)
            AddReward(1.0f);
        else
            AddReward(0.5f);

        // Proximity bonus
        float proxBonus = 0.3f * (1f - Mathf.Min(distToCenter / 1f, 1f));
        AddReward(proxBonus);

        EndEpisode();
    }

    // Aangeroepen door ThrowableBall bij missen (grond/muur)
    public void NotifyMiss()
    {
        Debug.Log("Miss, closestApproach=" + closestApproach);

        AddReward(-0.3f);

        // Bonus op basis van closest approach
        float dist = closestApproach;
        float bonus = 0.3f * (1f - Mathf.Min(dist / 5f, 1f));
        AddReward(bonus);

        EndEpisode();
    }

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var ca = actionsOut.ContinuousActions;
        var da = actionsOut.DiscreteActions;

        // WASD voor beweging
        ca[0] = Input.GetAxis("Horizontal");
        ca[1] = Input.GetAxis("Vertical");

        // Q/E aim yaw
        float yaw = 0f;
        if (Input.GetKey(KeyCode.Q)) yaw -= 1f;
        if (Input.GetKey(KeyCode.E)) yaw += 1f;
        ca[2] = yaw;

        // R/F throw angle
        float angle = 0f;
        if (Input.GetKey(KeyCode.R)) angle += 1f;
        if (Input.GetKey(KeyCode.F)) angle -= 1f;
        ca[3] = angle;

        // T/G throw power
        float power = 0f;
        if (Input.GetKey(KeyCode.T)) power += 1f;
        if (Input.GetKey(KeyCode.G)) power -= 1f;
        ca[4] = power;

        // Spatie = gooien
        da[0] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }
}
