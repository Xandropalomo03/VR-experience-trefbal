using UnityEngine;

public class BallScore : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
        // Geen ScoreManager in de scene (bv. trainings-scenes) -> niets doen.
        // Zo is dit component veilig op de ball-prefabs te zetten zonder dat het
        // buiten de game-scene crasht op een null Instance.
        if (ScoreManager.Instance == null) return;

        // bal raakt speler
        if (collision.gameObject.CompareTag("Player"))
        {
            ScoreManager.Instance.AddAIPoint();
        }

        // bal raakt AI
        if (collision.gameObject.CompareTag("Agent"))
        {
            ScoreManager.Instance.AddPlayerPoint();
        }
    }
}