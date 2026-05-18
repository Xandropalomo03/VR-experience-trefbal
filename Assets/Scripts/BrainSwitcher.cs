using System.Collections;
using UnityEngine;
using Unity.MLAgents;

// Manuele brain switcher voor de multi-brain POC scene.
// Zit op een Manager GameObject in MultiBrainScene en kiest welk van de drie
// brains aan zet is, op basis van een keypress.
//
// LIFECYCLE STRATEGY
// Eerdere versie deed gameObject.SetActive() op de Agent-GameObjects, wat
// een NullReferenceException gaf in Agent.UpdateSensors. Root cause: bij
// SetActive(false/true) gaat de hele component-stack door OnDisable/OnEnable,
// inclusief de RayPerceptionSensorComponent. Die component cachet zijn
// RayPerceptionSensor in m_RaySensor maar refresht die niet zelf — alleen
// een aanroep van CreateSensors() doet dat. Agent.OnDisable doet wel een
// CleanupSensors (Dispose) zonder de lijst te wissen. Resultaat: een race
// waarin het Academy event-loop UpdateSensors aanroept op een sensor die
// is disposed of refereert naar een net-vernieuwde input die nog niet
// klaar is. Geen coroutine-delay loste dat structureel op.
//
// Nieuwe aanpak: laat de drie Agent-GameObjects PERMANENT active staan.
// Toggle alleen de Agent-MonoBehaviour zijn .enabled. Daardoor blijven de
// SensorComponents continu enabled — die hoeven nooit door hun lifecycle
// heen — en gaat alleen de Agent-component zelf netjes door
// OnDisable/OnEnable. Re-enable triggert LazyInitialize → OnEpisodeBegin
// automatisch, dus geen handmatige EndEpisode-hack meer nodig.
//
// Trade-off: de drie agent-bodies staan altijd in de scene. Als ze visueel
// overlappen, kun je per agent een aparte child-GameObject als
// "visual root" maken en die via een eigen SetActive togglen — dat is een
// scene-setup kwestie, geen scriptverandering.
//
// Bewust géén referenties naar specifieke Agent-subclasses, alleen de
// generieke Agent-class via TryGetComponent.
public class BrainSwitcher : MonoBehaviour
{
    [Header("Agents")]
    // Drie agent-GameObjects. Blijven permanent gameObject.SetActive(true)
    // staan; switchen gebeurt via Agent.enabled.
    public GameObject dodgeAgent;
    public GameObject catchAgent;
    public GameObject throwAgent;

    [Header("Support objects (optional)")]
    // Per-brain support GameObjects (target, ballspawner, etc). Mogen leeg
    // blijven; null wordt overgeslagen. Voor supports gebruiken we wél
    // gewone GameObject.SetActive — die zijn geen Agents.
    public GameObject dodgeSupport;
    public GameObject catchSupport;
    public GameObject throwSupport;

    [Header("Keys")]
    public KeyCode dodgeKey = KeyCode.Alpha1;
    public KeyCode catchKey = KeyCode.Alpha2;
    public KeyCode throwKey = KeyCode.Alpha3;

    // Cache van wie nu actief is, zodat we positie/rotatie kunnen overdragen.
    private GameObject currentActive;

    private void Start()
    {
        StartCoroutine(InitializeBrains());
    }

    // Warm-up bij scene start: alle drie agents enabled = true voor een paar
    // frames. Daardoor doorlopen ze hun eerste LazyInitialize (Brain ophalen,
    // sensors aanmaken) terwijl Academy nog niet veel callbacks doet. Daarna
    // disable we twee van de drie. Vanaf dat punt is elke switch alleen nog
    // een .enabled toggle op een Agent die al een keer geïnitialiseerd is.
    private IEnumerator InitializeBrains()
    {
        DebugLogger.Log("SWITCH", $"InitializeBrains start. log={DebugLogger.LogPath}");

        // Forceer GameObjects active (we togglen ze nooit meer).
        if (dodgeAgent != null) dodgeAgent.SetActive(true);
        if (catchAgent != null) catchAgent.SetActive(true);
        if (throwAgent != null) throwAgent.SetActive(true);

        SetAgentEnabled(dodgeAgent, true);
        SetAgentEnabled(catchAgent, true);
        SetAgentEnabled(throwAgent, true);

        DebugLogger.Log("SWITCH", "warm-up: all three agents enabled. State: " + FormatState());

        // Ademruimte voor Initialize + InitializeSensors + Academy first reset.
        yield return null;
        yield return null;
        yield return new WaitForFixedUpdate();

        GameObject initial = dodgeAgent != null ? dodgeAgent :
                             (catchAgent != null ? catchAgent : throwAgent);

        DebugLogger.Log("SWITCH", $"warm-up done, picking initial={Name(initial)}");
        ApplyActive(initial, transferTransform: false);
    }

    private void Update()
    {
        if (Input.GetKeyDown(dodgeKey)) { DebugLogger.Log("INPUT", $"key {dodgeKey} -> dodge"); SwitchTo(dodgeAgent); }
        else if (Input.GetKeyDown(catchKey)) { DebugLogger.Log("INPUT", $"key {catchKey} -> catch"); SwitchTo(catchAgent); }
        else if (Input.GetKeyDown(throwKey)) { DebugLogger.Log("INPUT", $"key {throwKey} -> throw"); SwitchTo(throwAgent); }
    }

    // Schakelt naar de gegeven agent en draagt positie/rotatie van de oude
    // actieve agent over zodat het visueel niet "springt".
    public void SwitchTo(GameObject newActive)
    {
        if (newActive == null) return;
        if (newActive == currentActive) return;

        ApplyActive(newActive, transferTransform: true);
    }

    // Voor latere automatic switching: accepteer een naam i.p.v. een ref.
    public void SwitchToBrain(string brainName)
    {
        if (string.IsNullOrEmpty(brainName)) return;

        switch (brainName.Trim().ToLowerInvariant())
        {
            case "dodge":
                SwitchTo(dodgeAgent);
                break;
            case "catch":
                SwitchTo(catchAgent);
                break;
            case "throw":
                SwitchTo(throwAgent);
                break;
            default:
                Debug.LogWarning("BrainSwitcher: unknown brain name '" + brainName + "'");
                break;
        }
    }

    // Centrale routing. Doet:
    //  1. transform-sync vanaf currentActive (optioneel)
    //  2. Agent.enabled toggle voor de drie agents
    //  3. SetActive toggle voor de drie support objects
    //  4. File + console logs (state before -> state after)
    private void ApplyActive(GameObject newActive, bool transferTransform)
    {
        string stateBefore = FormatState();
        DebugLogger.Log("APPLY", $"ApplyActive target={Name(newActive)} from={Name(currentActive)} transfer={transferTransform} | before: {stateBefore}");

        Vector3 savedPos = Vector3.zero;
        Quaternion savedRot = Quaternion.identity;
        bool hasSaved = false;
        if (transferTransform && currentActive != null && newActive != currentActive)
        {
            savedPos = currentActive.transform.position;
            savedRot = currentActive.transform.rotation;
            hasSaved = true;
            DebugLogger.Log("APPLY", $"transform saved from {Name(currentActive)} pos={savedPos} rot={savedRot.eulerAngles}");
        }

        SetAgentEnabled(dodgeAgent, dodgeAgent == newActive);
        SetAgentEnabled(catchAgent, catchAgent == newActive);
        SetAgentEnabled(throwAgent, throwAgent == newActive);

        GameObject newSupport = SupportFor(newActive);
        SetOnlyActive(dodgeSupport, catchSupport, throwSupport, newSupport);

        if (hasSaved && newActive != null)
        {
            newActive.transform.position = savedPos;
            newActive.transform.rotation = savedRot;
        }

        currentActive = newActive;

        DebugLogger.Log("APPLY", $"done. Switched to={Name(newActive)} support={Name(newSupport)} | after: {FormatState()}");
    }

    // Toggle alleen de Agent-component .enabled, niet het GameObject.
    // Re-enable triggert vanzelf Agent.OnEnable → LazyInitialize →
    // OnEpisodeBegin (zolang Academy.TotalStepCount > 0, wat het na de
    // warm-up is). Dus ThrowingAgent krijgt vanzelf een nieuwe bal.
    //
    // Zelfde call schakelt ook renderers + colliders mee zodat alleen de
    // actieve brain visueel en fysiek aanwezig is in de scene (de drie
    // agent-bodies overlappen anders op dezelfde positie). Colliders
    // mee-togglen voorkomt ook dat een stray ball de inactieve dodge-body
    // raakt en daar een ongewenste OnEpisodeBegin triggert.
    private void SetAgentEnabled(GameObject go, bool on)
    {
        if (go == null) return;
        if (go.TryGetComponent(out Agent agent))
        {
            if (agent.enabled != on)
                DebugLogger.Log("AGENT", $"{go.name}.Agent.enabled {agent.enabled}->{on}");
            agent.enabled = on;
        }
        SetAgentVisible(go, on);
    }

    // Zet alle renderers + colliders onder de agent op enabled = visible.
    // GetComponentsInChildren met includeInactive=true zodat we ook iets
    // pakken dat per ongeluk uit stond bij scene-design.
    private void SetAgentVisible(GameObject go, bool visible)
    {
        if (go == null) return;

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
        {
            if (r != null) r.enabled = visible;
        }

        var colliders = go.GetComponentsInChildren<Collider>(true);
        foreach (var c in colliders)
        {
            if (c != null) c.enabled = visible;
        }
    }

    // ----- state snapshot helpers (voor logs) -----
    private string FormatState()
    {
        return $"dodge[{AgentState(dodgeAgent)}] catch[{AgentState(catchAgent)}] throw[{AgentState(throwAgent)}] | sup dodge[{Active(dodgeSupport)}] catch[{Active(catchSupport)}] throw[{Active(throwSupport)}]";
    }

    private static string AgentState(GameObject go)
    {
        if (go == null) return "-";
        if (!go.activeInHierarchy) return "GO=off";
        if (go.TryGetComponent(out Agent a)) return a.enabled ? "ON" : "off";
        return "noAgent";
    }

    private static string Active(GameObject go)
    {
        if (go == null) return "-";
        return go.activeSelf ? "ON" : "off";
    }

    private static string Name(GameObject go) => go != null ? go.name : "(null)";

    // Zet één van de drie slots actief en de andere uit. Null-refs worden
    // overgeslagen. Voor support objects: dit is gewoon GameObject.SetActive.
    private void SetOnlyActive(GameObject a, GameObject b, GameObject c, GameObject target)
    {
        if (a != null) a.SetActive(a == target);
        if (b != null) b.SetActive(b == target);
        if (c != null) c.SetActive(c == target);
    }

    // Map agent -> bijbehorend support object op referentie-identiteit.
    private GameObject SupportFor(GameObject agent)
    {
        if (agent == null) return null;
        if (agent == dodgeAgent) return dodgeSupport;
        if (agent == catchAgent) return catchSupport;
        if (agent == throwAgent) return throwSupport;
        return null;
    }
}
