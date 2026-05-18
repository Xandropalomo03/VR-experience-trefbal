using UnityEngine;
using Unity.MLAgents;

// Manuele brain switcher voor de multi-brain POC scene.
// Zit op een Manager GameObject in MultiBrainScene en zet één van de drie
// agent-GameObjects actief op basis van een keypress. Bewaart de positie +
// rotatie van de huidige actieve agent en past die toe op de nieuwe, zodat
// het visueel niet aanvoelt als een teleport.
//
// Bewust géén referenties naar specifieke Agent-subclasses — alleen
// GameObject.SetActive — zodat dit script later makkelijk uitbreidbaar is
// naar automatic switching zonder dat we hier brain-logic moeten kennen.
public class BrainSwitcher : MonoBehaviour
{
    [Header("Agents")]
    public GameObject dodgeAgent;
    public GameObject catchAgent;
    public GameObject throwAgent;

    [Header("Support objects (optional)")]
    // Per-brain support GameObjects. Mogen leeg blijven; null wordt
    // overgeslagen door de SetActive-helper.
    public GameObject dodgeSupport;   // bv. BallSpawner
    public GameObject catchSupport;   // bv. BallSpawner_Catching
    public GameObject throwSupport;   // bv. Target

    [Header("Keys")]
    public KeyCode dodgeKey = KeyCode.Alpha1;
    public KeyCode catchKey = KeyCode.Alpha2;
    public KeyCode throwKey = KeyCode.Alpha3;

    // Cache van wie nu actief is, zodat we positie/rotatie kunnen overdragen.
    private GameObject currentActive;

    private void Start()
    {
        // Bepaal welke agent we als default willen: dodge, tenzij die
        // expliciet uit staat in de Hierarchy. Sync de andere twee daaromheen.
        GameObject initial = dodgeAgent;
        if (initial == null || !initial.activeSelf)
        {
            // Fallback: pak de eerste die al actief staat in de Hierarchy.
            if (dodgeAgent != null && dodgeAgent.activeSelf) initial = dodgeAgent;
            else if (catchAgent != null && catchAgent.activeSelf) initial = catchAgent;
            else if (throwAgent != null && throwAgent.activeSelf) initial = throwAgent;
            else initial = dodgeAgent; // niemand actief — forceer dodge
        }

        SetOnlyActive(dodgeAgent, catchAgent, throwAgent, initial);
        GameObject initialSupport = SupportFor(initial);
        SetOnlyActive(dodgeSupport, catchSupport, throwSupport, initialSupport);
        currentActive = initial;

        if (currentActive != null)
            Debug.Log("BrainSwitcher start, active brain: " + currentActive.name);
        if (initialSupport != null)
            Debug.Log("Support active: " + initialSupport.name);

        // Geen ForceNewEpisode hier: ML-Agents roept OnEpisodeBegin()
        // sowieso aan bij Initialize() voor de initieel-actieve agent.
        // Het zelf aanroepen vóór de sensors klaar zijn geeft NRE in
        // Agent.UpdateSensors. Bij SwitchTo() hebben we het wél nodig
        // omdat een agent dan runtime van inactive naar active gaat.
    }

    private void Update()
    {
        if (Input.GetKeyDown(dodgeKey)) SwitchTo(dodgeAgent);
        else if (Input.GetKeyDown(catchKey)) SwitchTo(catchAgent);
        else if (Input.GetKeyDown(throwKey)) SwitchTo(throwAgent);
    }

    // Schakelt naar de gegeven agent en draagt positie/rotatie van de oude
    // actieve agent over zodat het visueel niet "springt".
    public void SwitchTo(GameObject newActive)
    {
        if (newActive == null) return;
        if (newActive == currentActive) return; // niks te doen

        // Bewaar transform van de huidige actieve agent.
        Vector3 savedPos = Vector3.zero;
        Quaternion savedRot = Quaternion.identity;
        bool hasSaved = false;
        if (currentActive != null)
        {
            savedPos = currentActive.transform.position;
            savedRot = currentActive.transform.rotation;
            hasSaved = true;
        }

        SetOnlyActive(dodgeAgent, catchAgent, throwAgent, newActive);

        // Bijbehorend support object meeschakelen.
        GameObject newSupport = SupportFor(newActive);
        SetOnlyActive(dodgeSupport, catchSupport, throwSupport, newSupport);

        // Pas saved transform toe op de nieuwe actieve agent.
        if (hasSaved)
        {
            newActive.transform.position = savedPos;
            newActive.transform.rotation = savedRot;
        }

        currentActive = newActive;
        Debug.Log("Switched to: " + newActive.name);
        if (newSupport != null)
            Debug.Log("Support active: " + newSupport.name);

        // Forceer een nieuwe episode op de net-geactiveerde agent. Zonder dit
        // roept ML-Agents geen OnEpisodeBegin() aan na enkel SetActive(true),
        // waardoor o.a. ThrowingAgent zonder bal blijft staan.
        ForceNewEpisode(newActive);
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

    // Zet één van de drie slots actief en de andere uit. Null-refs worden
    // overgeslagen. Generiek bruikbaar voor zowel agents als supports.
    private void SetOnlyActive(GameObject a, GameObject b, GameObject c, GameObject target)
    {
        if (a != null) a.SetActive(a == target);
        if (b != null) b.SetActive(b == target);
        if (c != null) c.SetActive(c == target);
    }

    // Null-safe wrapper rond Agent.EndEpisode(). Op de volgende frame triggert
    // ML-Agents dan automatisch OnEpisodeBegin() op die agent.
    private void ForceNewEpisode(GameObject go)
    {
        if (go == null) return;
        if (go.TryGetComponent(out Agent agent))
        {
            agent.EndEpisode();
            Debug.Log("Forced new episode on: " + go.name);
        }
    }

    // Map agent -> bijbehorend support object. Geen typecheck, puur op
    // referentie-identiteit zoals in de Inspector ingevuld.
    private GameObject SupportFor(GameObject agent)
    {
        if (agent == null) return null;
        if (agent == dodgeAgent) return dodgeSupport;
        if (agent == catchAgent) return catchSupport;
        if (agent == throwAgent) return throwSupport;
        return null;
    }
}
