using UnityEngine;

public class BallScore : MonoBehaviour
{
    private void OnCollisionEnter(Collision collision)
    {
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