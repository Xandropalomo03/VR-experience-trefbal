using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

// Agent die leert om een bal naar een doel te gooien.
//
// Canonical vector observation size: 12 (throw v3).
// De eerste 10 obs zijn ongewijzigd t.o.v. v2; obs [10..11] zijn de sin/cos
// van de hoek-naar-target die voor v2 tijdelijk verwijderd waren (om compat
// te houden met ThrowingAgent_v2.onnx, input shape [batch, 10]). Voor v3
// staan ze er weer in, zodat aim-info expliciet in de observation vector zit.
// Het nieuwe v3 model verwacht input shape [batch, 12]; zet daarbij
// BehaviorParameters.VectorObservationSize in ThrowingTrainingScene.unity op
// 12. De aim-bonus reward gebruikt ComputeAngleToTarget() nog steeds los van
// de observation vector.
public class ThrowingAgent : BaseSportAgent
{
    [Header("References")]
    [SerializeField] private Transform ballHolder;
    [SerializeField] private Target target;
    [SerializeField] private GameObject ballPrefab;

    [Header("Movement")]
    [SerializeField] private float maxMoveSpeed = 4f;

    [Tooltip("UIT = training (agent spawnt elke episode op een random plek in z'n " +
             "helft). AAN = game (vr-omgeving): de agent behoudt z'n huidige " +
             "positie bij een nieuwe worp-episode i.p.v. te teleporteren. Zet AAN " +
             "in de game-scene.")]
    [SerializeField] private bool gameMode = false;

    [Tooltip("Alleen in gameMode: hoe ver (m) de throw-agent maximaal van z'n " +
             "START-positie mag bewegen tijdens het positioneren. De start = waar " +
             "de catch 'm achterlaat (vastgelegd bij de 1e actie, NA de BrainSwitcher-" +
             "overdracht). RELATIEF, dus GEEN grote terugsnap meer -> de worp triggert " +
             "vanaf de catch-eindpositie, maar hij drift niet van het veld. Parent-" +
             "relatief (lokale x,z), werkt ook in de gedraaide/verschoven arena.")]
    [SerializeField] private float maxDriftFromStart = 1.5f;

    [Tooltip("Alleen in gameMode: harde vangnet-grens (LOKALE |x| en |z|) zodat de " +
             "throw-body sowieso nooit buiten het veld komt, ook als er nog drift " +
             "zit. Net binnen de arena-muren (die staan op ±5).")]
    [SerializeField] private float arenaHalfExtent = 4.5f;

    // Worp-startpositie (lokaal), vastgelegd bij de eerste actie van de episode
    // (na de overdracht door de BrainSwitcher). De drift-clamp werkt hieromheen.
    private Vector3 throwStartLocal;
    private bool throwStartCaptured;

    // Vuurt wanneer de worp-episode klaar is (raak, mis, of nooit gegooid).
    // MatchCoordinator gebruikt dit om na de worp terug naar idle te gaan.
    public event System.Action ThrowFinished;

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

    // Wanneer de BrainSwitcher deze agent uitschakelt (na de warm-up of na een
    // worp), een nog VASTGEHOUDEN bal opruimen. Zo blijft er geen "spook"-bal
    // in de hand hangen wanneer throw niet de actieve brain is (de warm-up bij
    // scene-start spawnt er anders eentje die blijft zweven). Een al GEGOOIDE
    // bal (hasBall == false) laten we met rust zodat die de speler kan bereiken.
    private void OnDisable()
    {
        if (hasBall && currentBall != null)
        {
            Destroy(currentBall.gameObject);
            currentBall = null;
            hasBall = false;
        }
    }

    public override void OnEpisodeBegin()
    {
        // Lees curriculum env params
        var envParams = Academy.Instance.EnvironmentParameters;
        curTargetDistance = envParams.GetWithDefault("target_distance", 4f);
        curTargetSize = envParams.GetWithDefault("target_size", 1f);
        curTargetSpeed = envParams.GetWithDefault("target_speed", 0f);
        curThrowNoise = envParams.GetWithDefault("throw_noise", 0f);

        // Spawn agent op eigen helft. In de game NIET teleporteren: de
        // BrainSwitcher zet de throw-body bij elke worp op de plek van de agent;
        // herpositioneren zou 'm laten verspringen. We behouden de positie en
        // zetten alleen de oriëntatie speler-gericht (lokaal, kleine variatie).
        if (!gameMode)
        {
            transform.localPosition = new Vector3(
                Random.Range(-3f, 3f),
                1f,
                Random.Range(-4f, -1f)
            );
        }
        transform.localRotation = Quaternion.Euler(0f, Random.Range(-30f, 30f), 0f);

        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // Reset target
        if (target != null)
            target.ResetTarget(curTargetDistance, curTargetSize, curTargetSpeed);

        // Reset state
        hasThrown = false;
        closestApproach = float.MaxValue;
        // Startpositie opnieuw vastleggen bij de eerstvolgende actie (dan is de
        // BrainSwitcher-overdracht al gebeurd en staat de body waar de catch eindigde).
        throwStartCaptured = false;

        // Geef agent een bal
        SpawnBall();

        DebugLogger.Log("THROW", $"OnEpisodeBegin pos={transform.localPosition} targetDist={curTargetDistance} size={curTargetSize} hasBall={hasBall}");
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
        DebugLogger.Log("THROW", "hasBall true (ball spawned, attached to holder)");
    }

    // Vector observation size = 12 (throw v3). Volgorde MOET stabiel blijven;
    // deze shape is gekoppeld aan het v3 model en aan ThrowingTrainingScene.unity
    // (BehaviorParameters.VectorObservationSize=12). Het oude v2 model gebruikte
    // shape [batch, 10] (zonder de sin/cos angleToTarget op index 10/11).
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector3 targetPos = target != null ? target.transform.position : transform.position;
        Vector3 targetVel = target != null ? target.CurrentVelocity : Vector3.zero;
        Vector3 toTarget = targetPos - transform.position;

        // Observaties uitdrukken in het ARENA-frame (parent) i.p.v. wereld.
        // In de trainings-scene staat de arena op identity, dus dit is exact
        // gelijk aan de oude wereld-obs (zelfde getallen) -> het v3-model blijft
        // werken. In de VR-scene is de arena -90° gedraaid; door parent-relatief
        // te observeren ziet het model dezelfde verdeling als tijdens training
        // (doel "voor zich", lokale yaw ~0) i.p.v. out-of-distribution
        // wereld-coördinaten. De observatie-VOLGORDE en -grootte (12) blijven
        // ongewijzigd.
        Transform frame = transform.parent;
        Vector3 toTargetF = frame != null ? frame.InverseTransformDirection(toTarget) : toTarget;
        Vector3 targetVelF = frame != null ? frame.InverseTransformDirection(targetVel) : targetVel;

        // [0..1] Relatieve positie target (genormaliseerd)
        sensor.AddObservation(toTargetF.x / 10f);
        sensor.AddObservation(toTargetF.z / 10f);

        // [2..3] Target snelheid
        sensor.AddObservation(targetVelF.x / 5f);
        sensor.AddObservation(targetVelF.z / 5f);

        // [4..5] Agent rotation als sin/cos (LOKAAL t.o.v. arena, zie boven)
        float yawRad = transform.localEulerAngles.y * Mathf.Deg2Rad;
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

        // [10..11] Sin/cos van de hoek-naar-target (signed angle tussen
        // agent forward en richting naar het doel). Teruggezet voor v3 zodat
        // aim-info expliciet in de observation vector zit i.p.v. alleen
        // impliciet via toTarget.x/z en de agent yaw.
        float angleToTarget = ComputeAngleToTarget();
        float angleRad = angleToTarget * Mathf.Deg2Rad;
        sensor.AddObservation(Mathf.Sin(angleRad));
        sensor.AddObservation(Mathf.Cos(angleRad));
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

        // Movement in arena-frame (consistent met de parent-relatieve obs).
        // Identity-arena in training -> identiek aan het oude wereld-gedrag.
        Vector3 moveLocal = new Vector3(moveX, 0f, moveZ) * maxMoveSpeed;
        Vector3 vel = transform.parent != null ? transform.parent.TransformDirection(moveLocal) : moveLocal;
        rb.linearVelocity = new Vector3(vel.x, rb.linearVelocity.y, vel.z);

        // Houd agent in de buurt van z'n startpositie. Parent-relatief
        // (localPosition), dus dit werkt ook in de gedraaide/verschoven VR-arena.
        if (gameMode)
        {
            // Leg de start vast op de EERSTE actie na de episode-start (dan staat
            // de body waar de catch 'm achterliet, na de BrainSwitcher-overdracht).
            // Daarna alleen de DRIFT relatief begrenzen: geen grote terugsnap, dus
            // de worp triggert vanaf die plek, maar hij loopt niet van het veld af.
            if (!throwStartCaptured)
            {
                throwStartLocal = transform.localPosition;
                throwStartCaptured = true;
            }
            Vector3 lp = transform.localPosition;
            lp.x = Mathf.Clamp(lp.x, throwStartLocal.x - maxDriftFromStart, throwStartLocal.x + maxDriftFromStart);
            lp.z = Mathf.Clamp(lp.z, throwStartLocal.z - maxDriftFromStart, throwStartLocal.z + maxDriftFromStart);
            // Vangnet: sowieso binnen de arena (lokaal ±arenaHalfExtent), ook als
            // de relatieve clamp ergens om een gedrifte start heen zou werken.
            lp.x = Mathf.Clamp(lp.x, -arenaHalfExtent, arenaHalfExtent);
            lp.z = Mathf.Clamp(lp.z, -arenaHalfExtent, arenaHalfExtent);
            transform.localPosition = lp;
        }
        else if (transform.localPosition.z > 0f)
        {
            // Training: ongewijzigd — alleen de eigen helft (z <= 0) afdwingen.
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

        // Trigger gooi. (De warm-up-worp wordt buiten dit script voorkomen door
        // de DecisionRequester van de throw-body uit te zetten zolang het geen
        // echte Throw-state is; zie MatchCoordinator.)
        if (trigger == 1 && hasBall && !hasThrown)
        {
            ThrowBall(aimYaw, throwAngle, throwPower);
        }

        // Max steps zonder ooit te gooien. Alleen training: in de game regelt
        // MatchCoordinator dit via throwTimeout en is EndEpisode/respawn uit
        // (anders zou een almaar groeiende StepCount dit vroegtijdig triggeren).
        if (!gameMode && StepCount >= MaxStep - 1 && !hasThrown)
        {
            AddReward(-0.1f);
            ThrowFinished?.Invoke();
            EndEpisode();
        }
    }

    // Rondt een worp-poging af. ALTIJD ThrowFinished vuren (MatchCoordinator gaat
    // dan naar idle). In TRAINING ook EndEpisode (reset + nieuwe bal, leer verder).
    // In de GAME (gameMode) GEEN EndEpisode -> geen respawn, geen hergooi-lus: de
    // throw gooit één keer en stopt; de gegooide bal vliegt door (scoort via
    // BallScore). De volgende worp-bal komt vanzelf bij de volgende Throw-state
    // (BrainSwitcher re-enable -> OnEpisodeBegin -> SpawnBall).
    private void FinishThrow()
    {
        ThrowFinished?.Invoke();
        if (!gameMode)
            EndEpisode();
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

        DebugLogger.Log("THROW", $"hasBall false (released) angle={elev:F1} power={power:F1} yaw={yaw:F1} velocity={velocity}");
    }

    // Aangeroepen door ThrowableBall bij raken doel
    public void NotifyTargetHit(Vector3 hitPoint)
    {
        DebugLogger.Log("THROW", $"NotifyTargetHit at {hitPoint}");

        if (target == null)
        {
            FinishThrow();
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

        FinishThrow();
    }

    // Aangeroepen door ThrowableBall bij missen (grond/muur)
    public void NotifyMiss()
    {
        DebugLogger.Log("THROW", $"NotifyMiss closestApproach={closestApproach:F2}");

        AddReward(-0.1f);

        // Bonus op basis van closest approach (nu max 0.5 ipv 0.3)
        float dist = closestApproach;
        float bonus = 0.5f * (1f - Mathf.Min(dist / 5f, 1f));
        AddReward(bonus);

        FinishThrow();
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
