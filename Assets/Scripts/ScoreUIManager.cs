using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Simple UI manager to display leaderboard using a vertical container and a row prefab.
/// Row prefab should contain two Text components (PlayerName, Score) and optionally a Button for delete.
/// </summary>
public class ScoreUIManager : MonoBehaviour
{
    public RectTransform container; // VerticalLayoutGroup parent
    public GameObject rowPrefab; // prefab with two Text components and a delete button (optional)
    public int page = 1;
    public int pageSize = 5;

    void Start()
    {
        Refresh();
    }

    /// <summary>
    /// Show or hide the leaderboard UI container. When hidden, it won't be visible during name input or game over.
    /// </summary>
    public void SetVisible(bool visible)
    {
        if (container != null)
            container.gameObject.SetActive(visible);
    }

    public void Refresh()
    {
        StartCoroutine(RefreshCoroutine());
    }

    IEnumerator RefreshCoroutine()
    {
        bool done = false;
        PlayerScore[] items = null;

        ScoreApiClient.Instance.GetLeaderboard(page, pageSize, (arr, total) => { items = arr; done = true; });
        while (!done) yield return null;

        // clear
        if (container != null)
        {
            foreach (Transform t in container) Destroy(t.gameObject);
        }

        if (items == null) yield break;

        foreach (var s in items)
        {
            var row = Instantiate(rowPrefab, container);

            // Try TextMeshPro first
            var tmpTexts = row.GetComponentsInChildren<TMP_Text>();
            if (tmpTexts != null && tmpTexts.Length >= 2)
            {
                tmpTexts[0].text = s.PlayerName;
                tmpTexts[1].text = s.Score.ToString();
            }
            else
            {
                // Fallback to legacy UI Text
                var texts = row.GetComponentsInChildren<Text>();
                if (texts != null && texts.Length >= 2)
                {
                    texts[0].text = s.PlayerName;
                    texts[1].text = s.Score.ToString();
                }
                else
                {
                    Debug.LogWarning("ScoreUIManager: rowPrefab does not contain TMP_Text or Text children to show name/score.");
                }
            }

            var delBtn = row.GetComponentInChildren<Button>();
            if (delBtn != null)
            {
                string player = s.PlayerName; // closure capture
                delBtn.onClick.AddListener(() =>
                {
                    ScoreApiClient.Instance.DeleteRecordByPlayer(player, (ok) => { if (ok) Refresh(); });
                });
            }
        }
    }
}
