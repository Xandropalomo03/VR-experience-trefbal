using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// Agent die leert om een bal naar een doel te gooien.
//
// Canonical vector observation size: 10.
// Dit is wat de huidige BehaviorParameters in ThrowingTrainingScene.unity
// vragen, en wat ThrowingAgent_v2.onnx (input shape [batch, 10]) verwacht.
// Eerdere versies hebben kortstondig 12 obs geschreven (twee extra sin/cos
// van de hoek-naar-target); die zijn weer verwijderd zodat code, scene en
// model consistent zijn. Aim-info zit nog steeds impliciet via toTarget.x/z
// en de agent yaw, en de aim-bonus reward gebruikt ComputeAngleToTarget()
// los van de observation vector.
public class ThrowingAgent : BaseSportAgent
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

    // rb en agentCollider worden door BaseSportAgent.Initialize() gevuld.
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
        base.Initialize();

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

    // Vector observation size = 10. Volgorde MOET stabiel blijven; deze
    // shape is gekoppeld aan ThrowingAgent_v2.onnx en aan
    // ThrowingTrainingScene.unity (BehaviorParameters.VectorObservationSize=10).
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 targetPos = target != null ? target.transform.position : transform.position;
        Vector3 targetVel = target != null ? target.CurrentVelocity : Vector3.zero;
        Vector3 toTarget = targetPos - transform.position;

        // [0..1] Relatieve positie target (genormaliseerd)
        sensor.AddObservation(toTarget.x / 10f);
        sensor.AddObservation(toTarget.z / 10f);

        // [2..3] Target snelheid
        sensor.AddObservation(targetVel.x / 5f);
        sensor.AddObservation(targetVel.z / 5f);

        // [4..5] Agent rotation als sin/cos
        float yawRad = transform.eulerAngles.y * Mathf.Deg2Rad;
        sensor.AddObservation(Mathf.Sin(yawRad));
        sensor.AddObservation(Mathf.Cos(yawRad));

        // [6] Heeft de agent een bal?
        sensor.AddObservation(hasBall ? 1f : 0f);

        // [7] Tijd in episode
        sensor.AddObservation(MaxStep > 0 ? (float)StepCount / MaxStep : 0f);

        // [8] Afstand tot doel
        sensor.AddObservation(toTarget.magnitude / 10f);

        // [9] Target grootte
        sensor.AddObservation(curTargetSize / 2f);

        // (sin/cos angleToTarget zaten hier ooit als obs 10/11 maar zijn
        // verwijderd: niet getraind in v2, model verwacht shape [batch, 10].
        // Voor reward shaping wordt ComputeAngleToTarget() nog steeds gebruikt
        // in OnActionReceived.)
    }

    private float ComputeAngleToTarget()
    {
        if (target == null) return 0f;
        Vector3 toTarget = target.transform.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 1e-6f) return 0f;
        toTarget.Normalize();
        return Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
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

        // Tijd-penalty (milder dan voorheen)
        AddReward(-0.0002f);

        // Aim bonus: zolang hij de bal heeft, beloon naar het doel draaien.
        // Max +0.002 per step bij perfect mikken, 0 bij |hoek| >= 30°.
        if (hasBall)
        {
            float angleToTarget = ComputeAngleToTarget();
            float absAngle = Mathf.Abs(angleToTarget);
            if (absAngle < 30f)
                AddReward(0.002f * (1f - absAngle / 30f));
        }

        // Trigger gooi
        if (trigger == 1 && hasBall && !hasThrown)
        {
            ThrowBall(aimYaw, throwAngle, throwPower);
        }

        // Max steps zonder ooit te gooien
        if (StepCount >= MaxStep - 1 && !hasThrown)
        {
            AddReward(-0.1f);
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
            AddReward(1.5f);
        else
            AddReward(0.7f);

        // Proximity bonus
        float proxBonus = 0.3f * (1f - Mathf.Min(distToCenter / 1f, 1f));
        AddReward(proxBonus);

        EndEpisode();
    }

    // Aangeroepen door ThrowableBall bij missen (grond/muur)
    public void NotifyMiss()
    {
        Debug.Log("Miss, closestApproach=" + closestApproach);

        AddReward(-0.1f);

        // Bonus op basis van closest approach (nu max 0.5 ipv 0.3)
        float dist = closestApproach;
        float bonus = 0.5f * (1f - Mathf.Min(dist / 5f, 1f));
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
