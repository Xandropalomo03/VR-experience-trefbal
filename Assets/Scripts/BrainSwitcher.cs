using UnityEngine;

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

        SetOnlyActive(initial);
        currentActive = initial;

        if (currentActive != null)
            Debug.Log("BrainSwitcher start, active brain: " + currentActive.name);
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

        SetOnlyActive(newActive);

        // Pas saved transform toe op de nieuwe actieve agent.
        if (hasSaved)
        {
            newActive.transform.position = savedPos;
            newActive.transform.rotation = savedRot;
        }

        currentActive = newActive;
        Debug.Log("Switched to: " + newActive.name);
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

    // Zet één agent actief en de andere twee uit. Null-refs worden overgeslagen.
    private void SetOnlyActive(GameObject target)
    {
        if (dodgeAgent != null) dodgeAgent.SetActive(dodgeAgent == target);
        if (catchAgent != null) catchAgent.SetActive(catchAgent == target);
        if (throwAgent != null) throwAgent.SetActive(throwAgent == target);
    }
}
