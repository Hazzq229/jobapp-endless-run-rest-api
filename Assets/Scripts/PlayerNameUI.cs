using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayerNameUI shows an input field and start button. The player must enter a name before the game starts.
/// It saves the name into PlayerPrefs key "username" and then calls GameManager.NewGame().
/// Optionally waits for ScoreApiClient.EnsurePlayerExists before starting (recommended but not required).
/// </summary>
public class PlayerNameUI : MonoBehaviour
{
    [Tooltip("Panel that contains the input UI. Will be hidden when name is confirmed.")]
    public GameObject panel;

    [Tooltip("TMP Input Field for entering player name")]
    public TMP_InputField nameInput;

    [Tooltip("Start button that confirms the name and starts the game")]
    public Button startButton;

    [Tooltip("If true, the UI will wait for the API ensure-player response before starting. If API fails, game still starts (with a warning).")]
    public bool waitForServer = true;

    [Tooltip("If true, when changing the username from a non-running state (e.g. opened from Game Over), a new game will be started on confirm instead of just resuming.")]
    public bool startNewGameOnChangeIfNotRunning = true;

    void Start()
    {
        if (panel == null || nameInput == null || startButton == null)
        {
            Debug.LogWarning("PlayerNameUI: panel, nameInput or startButton is not assigned.");
            return;
        }

        panel.SetActive(true);
        startButton.onClick.RemoveAllListeners();
        startButton.onClick.AddListener(OnStartClicked);
    }

    bool isChangeMode = false;

    /// <summary>
    /// Show the player-name panel.
    /// prefillWithSaved: if true, prefill the input field with saved PlayerPrefs username (if any)
    /// isChange: if true, the panel is opened to change username (on close it will resume game),
    ///          if false, the panel is opened for initial entry (on confirm it will start a new game).
    /// </summary>
    public void Show(bool prefillWithSaved, bool isChange)
    {
        isChangeMode = isChange;
        panel.SetActive(true);
        if (prefillWithSaved)
        {
            nameInput.text = PlayerPrefs.GetString("username", "");
        }

        // Pause game while entering name and hide game-over/score UI
        if (GameManager.Instance != null)
            GameManager.Instance.PauseForNameEntry();
    }

    void OnStartClicked()
    {
        var name = nameInput.text?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(name))
        {
            // simple feedback: you may replace with label or popup
            Debug.Log("Please enter a player name before starting.");
            return;
        }

        // Save name
        PlayerPrefs.SetString("username", name);
        PlayerPrefs.Save();

        // Disable UI while processing
        panel.SetActive(false);

        // Optionally wait for server to create/ensure player exists
        if (waitForServer && ScoreApiClient.Instance != null)
        {
            startButton.interactable = false;
            ScoreApiClient.Instance.EnsurePlayerExists(name, (rec) =>
            {
                if (rec == null)
                {
                    Debug.LogWarning("EnsurePlayerExists failed or returned null. Starting locally anyway.");
                }
                // re-enable start button for subsequent uses
                startButton.interactable = true;
                StartGameAfterNameConfirmed();
            });
        }
        else
        {
            StartGameAfterNameConfirmed();
        }
    }
    void StartGameAfterNameConfirmed()
    {
        // Start the game
        if (GameManager.Instance != null)
        {
            // ensure button is interactive for next time
            startButton.interactable = true;

            // If gameplay was paused for name entry, make sure to unpause time
            bool wasPaused = Time.timeScale == 0f;
            if (wasPaused)
                Time.timeScale = 1f;

            if (isChangeMode)
            {
                // If this panel was opened from a Game Over state (no active run), resuming won't start a new round.
                // Heuristic: if the manager was disabled or time was paused for name entry, start a new game instead.
                if (startNewGameOnChangeIfNotRunning && (!GameManager.Instance.enabled || wasPaused))
                {
                    GameManager.Instance.NewGame();
                    GameManager.Instance.enabled = true;
                }
                else
                {
                    // just resume current session
                    GameManager.Instance.enabled = true;
                }
            }
            else
            {
                GameManager.Instance.NewGame();
                GameManager.Instance.enabled = true;
            }
        }
        else
        {
            Debug.LogWarning("GameManager not found when starting game after name confirmation.");
        }
    }
}
