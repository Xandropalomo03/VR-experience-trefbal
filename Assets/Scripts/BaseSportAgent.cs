using UnityEngine;
using Unity.MLAgents;

// Shared boilerplate voor de sport-agents in dit project (Dodgeball,
// Throwing, Catch). Bevat alleen component-caches; bevat bewust GEEN
// observation- of action-logic, want de drie agents verschillen daarin
// fundamenteel (zie docs/codebase-audit.md).
//
// Subclasses die Initialize() overriden MOETEN base.Initialize() aanroepen
// zodat de caches gevuld worden.
public abstract class BaseSportAgent : Agent
{
    protected Rigidbody rb;
    protected Collider agentCollider;
    protected Transform myArena;

    public override void Initialize()
    {
        TryGetComponent(out rb);
        TryGetComponent(out agentCollider);
        myArena = transform.parent;
    }
}
