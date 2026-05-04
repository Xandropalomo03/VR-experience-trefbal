using UnityEngine;

public class ArenaDuplicator : MonoBehaviour
{
    [SerializeField] private bool duplicate = true;

    private static readonly Vector3[] Offsets = new Vector3[]
    {
        new Vector3(15f, 0f, 0f),
        new Vector3(0f, 0f, 15f),
        new Vector3(15f, 0f, 15f),
    };

    // Voorkomt dat geklonede arenas zichzelf opnieuw dupliceren (Awake op clone vuurt direct).
    private static bool hasDuplicated = false;

    private void Awake()
    {
        if (!duplicate) return;
        if (hasDuplicated) return;
        hasDuplicated = true;

        foreach (Vector3 offset in Offsets)
        {
            Instantiate(gameObject, transform.position + offset, transform.rotation, transform.parent);
        }
    }
}
