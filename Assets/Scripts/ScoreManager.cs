using TMPro;
using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance;

    [Header("UI")]
    public TextMeshPro playerScoreText;
    public TextMeshPro aiScoreText;

    private int playerScore = 0;
    private int aiScore = 0;

    private void Awake()
    {
        Instance = this;
        UpdateUI();
    }

    public void AddPlayerPoint()
    {
        playerScore++;
        UpdateUI();
    }

    public void AddAIPoint()
    {
        aiScore++;
        UpdateUI();
    }

    private void UpdateUI()
    {
        playerScoreText.text = playerScore.ToString();
        aiScoreText.text = aiScore.ToString();
    }
}