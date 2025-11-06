using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

[Serializable]
public class PlayerScore
{
    public int Id;
    public string PlayerName = string.Empty;
    public int Score;
    public string CreatedAt = string.Empty; // ISO string from server
}

[Serializable]
public class PlayerScoreListWrapper
{
    public PlayerScore[] items;
}

[Serializable]
public class RankResult
{
    public string player;
    public int score;
    public int rank;
}

/// <summary>
/// ScoreApiClient - adapted to the user's existing API at http://localhost:5289
/// Endpoints used:
/// GET  /api/scores?page=1&pageSize=10
/// POST /api/scores
/// PUT  /api/scores/{id}
/// DELETE /api/scores/{id}
/// GET  /api/scores/rank/{playerName}
///
/// Note: server limits pageSize to max 100; this client will iterate pages when searching for a player.
/// </summary>
public class ScoreApiClient : MonoBehaviour
{
    public static ScoreApiClient Instance { get; private set; }

    [Tooltip("Base URL of the game score API, e.g. http://localhost:5289")]
    public string baseUrl = "http://localhost:5289";

    public string ScoresPath => "/api/scores";
    public int requestTimeout = 10;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    IEnumerator SendRequest(UnityWebRequest req, Action<UnityWebRequest> onDone)
    {
        req.timeout = requestTimeout;
        if (req.method == UnityWebRequest.kHttpVerbPOST || req.method == UnityWebRequest.kHttpVerbPUT)
            req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

#if UNITY_2020_1_OR_NEWER
        if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
#else
        if (req.isNetworkError || req.isHttpError)
#endif
        {
            Debug.LogWarning($"ScoreApiClient request error: {req.responseCode} {req.error} -> {req.url}");
        }
        onDone?.Invoke(req);
    }

    // GET leaderboard page
    public void GetLeaderboard(int page, int pageSize, Action<PlayerScore[], int?> onComplete)
    {
        StartCoroutine(GetLeaderboardCoroutine(page, pageSize, onComplete));
    }

    IEnumerator GetLeaderboardCoroutine(int page, int pageSize, Action<PlayerScore[], int?> onComplete)
    {
        string url = $"{baseUrl}{ScoresPath}?page={page}&pageSize={pageSize}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return SendRequest(req, null);
            if (req.responseCode >= 200 && req.responseCode < 300)
            {
                var text = req.downloadHandler.text;
                var arr = ParseListJson(text);
                int? total = null;
                if (req.GetResponseHeaders()?.TryGetValue("X-Total-Count", out var t) == true)
                    if (int.TryParse(t, out var tt)) total = tt;
                onComplete?.Invoke(arr, total);
            }
            else onComplete?.Invoke(null, null);
        }
    }

    // Find a player record by name by iterating pages (server pageSize capped at 100)
    public void FindPlayerRecord(string playerName, Action<PlayerScore> onComplete, int maxPages = 10)
    {
        StartCoroutine(FindPlayerRecordCoroutine(playerName, onComplete, maxPages));
    }

    IEnumerator FindPlayerRecordCoroutine(string playerName, Action<PlayerScore> onComplete, int maxPages)
    {
        playerName = playerName?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(playerName)) { onComplete?.Invoke(null); yield break; }

        int page = 1;
        int pageSize = 100; // server caps >100 to 10; use 100 as max allowed

        while (page <= maxPages)
        {
            PlayerScore[] results = null;
            int? total = null;
            bool done = false;
            yield return GetLeaderboardCoroutine(page, pageSize, (items, tot) => { results = items; total = tot; done = true; });
            // wait for coroutine to set done
            while (!done) yield return null;

            if (results == null || results.Length == 0) break;

            foreach (var r in results)
            {
                if (string.Equals(r.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
                {
                    onComplete?.Invoke(r);
                    yield break;
                }
            }

            // stop if fewer than pageSize results returned (last page)
            if (results.Length < pageSize) break;
            page++;
        }

        onComplete?.Invoke(null);
    }

    // Ensure player exists: if not found, POST to create with Score=0
    public void EnsurePlayerExists(string playerName, Action<PlayerScore> onComplete)
    {
        StartCoroutine(EnsurePlayerExistsCoroutine(playerName, onComplete));
    }

    IEnumerator EnsurePlayerExistsCoroutine(string playerName, Action<PlayerScore> onComplete)
    {
        PlayerScore found = null;
        bool finished = false;
        FindPlayerRecord(playerName, (r) => { found = r; finished = true; });
        while (!finished) yield return null;

        if (found != null) { onComplete?.Invoke(found); yield break; }

        // create
        var create = new PlayerScore { PlayerName = playerName, Score = 0, CreatedAt = DateTime.UtcNow.ToString("o") };
        string json = JsonUtility.ToJson(create);
        string url = $"{baseUrl}{ScoresPath}";
        using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return SendRequest(req, null);
            if (req.responseCode >= 200 && req.responseCode < 300)
            {
                var txt = req.downloadHandler.text;
                var created = ParseSingleJson(txt);
                onComplete?.Invoke(created);
            }
            else onComplete?.Invoke(null);
        }
    }

    // Update score: if existing record found, PUT to /api/scores/{id}; otherwise POST new
    public void UpdateScoreForPlayer(string playerName, int score, Action<PlayerScore> onComplete)
    {
        StartCoroutine(UpdateScoreForPlayerCoroutine(playerName, score, onComplete));
    }

    IEnumerator UpdateScoreForPlayerCoroutine(string playerName, int score, Action<PlayerScore> onComplete)
    {
        PlayerScore existing = null;
        bool done = false;
        FindPlayerRecord(playerName, (r) => { existing = r; done = true; });
        while (!done) yield return null;

        if (existing == null)
        {
            // create new record
            var create = new PlayerScore { PlayerName = playerName, Score = score, CreatedAt = DateTime.UtcNow.ToString("o") };
            string json = JsonUtility.ToJson(create);
            string url = $"{baseUrl}{ScoresPath}";
            using (var req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return SendRequest(req, null);
                if (req.responseCode >= 200 && req.responseCode < 300)
                {
                    var txt = req.downloadHandler.text;
                    var created = ParseSingleJson(txt);
                    onComplete?.Invoke(created);
                }
                else onComplete?.Invoke(null);
            }
            yield break;
        }

        // update existing
        existing.Score = score;
        // preserve original CreatedAt if present
        if (string.IsNullOrEmpty(existing.CreatedAt)) existing.CreatedAt = DateTime.UtcNow.ToString("o");
        string putJson = JsonUtility.ToJson(existing);
        string putUrl = $"{baseUrl}{ScoresPath}/{existing.Id}";
        using (var req = new UnityWebRequest(putUrl, UnityWebRequest.kHttpVerbPUT))
        {
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(putJson));
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            yield return SendRequest(req, null);
            if (req.responseCode >= 200 && req.responseCode < 300)
            {
                // server returns 204 NoContent; to get fresh record we can re-fetch by id
                // try to GET by id
                string getUrl = $"{baseUrl}{ScoresPath}/{existing.Id}";
                using (var getReq = UnityWebRequest.Get(getUrl))
                {
                    yield return SendRequest(getReq, null);
                    if (getReq.responseCode >= 200 && getReq.responseCode < 300)
                    {
                        var createdTxt = getReq.downloadHandler.text;
                        var updated = ParseSingleJson(createdTxt);
                        onComplete?.Invoke(updated ?? existing);
                        yield break;
                    }
                }
                onComplete?.Invoke(existing);
            }
            else onComplete?.Invoke(null);
        }
    }

    public void DeleteRecordByPlayer(string playerName, Action<bool> onComplete)
    {
        StartCoroutine(DeleteRecordByPlayerCoroutine(playerName, onComplete));
    }

    IEnumerator DeleteRecordByPlayerCoroutine(string playerName, Action<bool> onComplete)
    {
        PlayerScore existing = null;
        bool done = false;
        FindPlayerRecord(playerName, (r) => { existing = r; done = true; });
        while (!done) yield return null;

        if (existing == null) { onComplete?.Invoke(false); yield break; }

        string url = $"{baseUrl}{ScoresPath}/{existing.Id}";
        using (var req = UnityWebRequest.Delete(url))
        {
            yield return SendRequest(req, null);
            onComplete?.Invoke(req.responseCode >= 200 && req.responseCode < 300);
        }
    }

    public void GetRank(string playerName, Action<RankResult?> onComplete)
    {
        StartCoroutine(GetRankCoroutine(playerName, onComplete));
    }

    IEnumerator GetRankCoroutine(string playerName, Action<RankResult?> onComplete)
    {
        if (string.IsNullOrWhiteSpace(playerName)) { onComplete?.Invoke(null); yield break; }
        string url = $"{baseUrl}{ScoresPath}/rank/{UnityWebRequest.EscapeURL(playerName)}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return SendRequest(req, null);
            if (req.responseCode >= 200 && req.responseCode < 300)
            {
                try
                {
                    var txt = req.downloadHandler.text;
                    var rank = JsonUtility.FromJson<RankResult>(txt);
                    onComplete?.Invoke(rank);
                }
                catch { onComplete?.Invoke(null); }
            }
            else onComplete?.Invoke(null);
        }
    }

    // Helpers to parse JSON arrays or single objects using JsonUtility
    PlayerScore[] ParseListJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return new PlayerScore[0];
        string trimmed = json.TrimStart();
        string toParse = json;
        if (trimmed.StartsWith("[")) toParse = "{\"items\":" + json + "}";
        // Normalize common camelCase keys (ASP.NET Core default) to PascalCase expected by Unity's JsonUtility
        toParse = NormalizeJsonKeys(toParse);
        try
        {
            var wrap = JsonUtility.FromJson<PlayerScoreListWrapper>(toParse);
            return wrap?.items ?? new PlayerScore[0];
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ParseListJson error: " + ex.Message + " json=" + toParse);
            return new PlayerScore[0];
        }
    }

    PlayerScore ParseSingleJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var fixedJson = NormalizeJsonKeys(json);
            return JsonUtility.FromJson<PlayerScore>(fixedJson);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("ParseSingleJson error: " + ex.Message + " json=" + json);
            return null;
        }
    }

    // Convert common camelCase JSON property names to PascalCase so Unity's JsonUtility can map to C# fields
    string NormalizeJsonKeys(string json)
    {
        if (string.IsNullOrEmpty(json)) return json;
        // quick replacements for known fields - keep id first to avoid matching 'Id' inside other keys
    json = json.Replace("\"id\"", "\"Id\"");
        json = json.Replace("\"playerName\"", "\"PlayerName\"");
        json = json.Replace("\"score\"", "\"Score\"");
        json = json.Replace("\"createdAt\"", "\"CreatedAt\"");
        // also handle possible camel-case with separators
        json = json.Replace("\"playername\"", "\"PlayerName\"");
        return json;
    }
}
