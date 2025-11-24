using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DefaultExecutionOrder(-1)]
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public float initialGameSpeed = 5f;
    public float gameSpeedIncrease = 0.1f;
    public float gameSpeed { get; private set; }

    [SerializeField] private TextMeshProUGUI inGameScoreText;
    [SerializeField] private TextMeshProUGUI gameOverText;
    [SerializeField] private TextMeshProUGUI gameOverScoreText;
    [SerializeField] private Image backgroundPanel;
    [SerializeField] ScoreUIManager scoreUi;

    private PlayerController player;
    private Spawner spawner;

    private float score;
    public float Score => score;

    private void Awake()
    {
        if (Instance != null) {
            DestroyImmediate(gameObject);
        } else {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) {
            Instance = null;
        }
    }

    private void Start()
    {
        player = FindObjectOfType<PlayerController>();
        spawner = FindObjectOfType<Spawner>();

        // Hide score UI (if present) until gameplay starts
        scoreUi = FindObjectOfType<ScoreUIManager>();
        if (scoreUi != null) scoreUi.SetVisible(false);

        backgroundPanel.gameObject.SetActive(true);
        inGameScoreText.gameObject.SetActive(false);

        var nameUi = FindObjectOfType<PlayerNameUI>();
        if (nameUi != null)
        {
            // Show panel and require name input at app launch (even if PlayerPrefs has a value)
            nameUi.Show(prefillWithSaved: true, isChange: false);
        }
        else
        {
            var existingName = PlayerPrefs.GetString("username", "");
            if (!string.IsNullOrEmpty(existingName)) NewGame();
        }
        // Do not start the game loop immediately. Show the PlayerNameUI so user must confirm their name.
        enabled = false;
    }

    public void NewGame()
    {
        Obstacle[] obstacles = FindObjectsOfType<Obstacle>();

        foreach (var obstacle in obstacles) {
            Destroy(obstacle.gameObject);
        }

        score = 0f;
        gameSpeed = initialGameSpeed;
        enabled = true;

        player.gameObject.SetActive(true);
        spawner.gameObject.SetActive(true);
        inGameScoreText.gameObject.SetActive(true);
        backgroundPanel.gameObject.SetActive(false);
        gameOverText.gameObject.SetActive(false);

        UpdateHiscore();

        // Ensure player record exists on the server (uses PlayerPrefs "username" or default "Player")
        try {
            var username = PlayerPrefs.GetString("username", "Player");
            if (ScoreApiClient.Instance != null) {
                ScoreApiClient.Instance.EnsurePlayerExists(username, (rec) => {
                    Debug.Log($"EnsurePlayerExists callback: {(rec != null ? "ok" : "failed")}");
                });
            }
            else Debug.Log("ScoreApiClient instance not found in scene.");
        } catch (System.Exception ex) {
            Debug.LogWarning("Error ensuring player exists: " + ex.Message);
        }

        // Show leaderboard / score UI when gameplay starts
        if (scoreUi != null) scoreUi.SetVisible(true);
    }

    /// <summary>
    /// Put the game into a paused state for player name entry: hide game-over UI and score UI
    /// and make sure gameplay objects are inactive.
    /// </summary>
    public void PauseForNameEntry()
    {
        // stop game loop
        enabled = false;

        // hide gameplay objects
        if (player != null) player.gameObject.SetActive(false);
        if (spawner != null) spawner.gameObject.SetActive(false);

        // hide game over UI
        if (gameOverText != null) gameOverText.gameObject.SetActive(false);

        // hide leaderboard
        if (scoreUi != null) scoreUi.SetVisible(false);
    }

    public void GameOver()
    {
        gameSpeed = 0f;
        enabled = false;

        player.gameObject.SetActive(false);
        spawner.gameObject.SetActive(false);
        gameOverText.gameObject.SetActive(true);
        inGameScoreText.gameObject.SetActive(false);
        backgroundPanel.gameObject.SetActive(true);

        UpdateHiscore();
        gameOverScoreText.text = Mathf.FloorToInt(score).ToString("D5");

        // Hide leaderboard / score UI when game over
        if (scoreUi != null) scoreUi.SetVisible(false);

        // Send final score to server (create or update)
        try {
            var username = PlayerPrefs.GetString("username", "Player");
            int finalScore = Mathf.FloorToInt(score);
            if (ScoreApiClient.Instance != null) {
                ScoreApiClient.Instance.UpdateScoreForPlayer(username, finalScore, (rec) => {
                    Debug.Log($"UpdateScoreForPlayer callback: {(rec != null ? "ok" : "failed")}");
                    // refresh leaderboard UI if present
                    if (scoreUi != null) scoreUi.Refresh();
                });
            }
            else Debug.Log("ScoreApiClient instance not found in scene.");
        } catch (System.Exception ex) {
            Debug.LogWarning("Error updating score: " + ex.Message);
        }
    }

    // Called by Retry button (wired in inspector) to restart the game using saved username
    public void OnRetryPressed()
    {
        NewGame();
        enabled = true;
    }

    // Called by Change Username button to show the name-input UI
    public void OnChangeUsernamePressed()
    {
        var nameUi = FindObjectOfType<PlayerNameUI>();
        if (nameUi != null)
        {
            // Pause gameplay
            enabled = false;
            // Hide score UI while changing
            if (scoreUi != null) scoreUi.SetVisible(false);
            nameUi.Show(prefillWithSaved: true, isChange: true);
        }
    }

    public void OnExit()
    {
        Application.Quit();
    }

    private void Update()
    {
        gameSpeed += gameSpeedIncrease * Time.deltaTime;
        score += gameSpeed * Time.deltaTime;
        inGameScoreText.text = Mathf.FloorToInt(score).ToString("D5");
    }

    private void UpdateHiscore()
    {
        float hiscore = PlayerPrefs.GetFloat("hiscore", 0);

        if (score > hiscore)
        {
            hiscore = score;
            PlayerPrefs.SetFloat("hiscore", hiscore);
        }
    }
}
