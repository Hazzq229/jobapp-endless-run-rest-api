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

    [Header("Debug")]
    [Tooltip("Enable verbose debug logging for API CRUD calls.")]
    public bool enableDebugLogs = true;

    [Header("Time Settings")]
    [Tooltip("When true, CreatedAt will use West Indonesian Time (UTC+7) instead of UTC.")]
    public bool useWestIndonesianTime = false;

    [Tooltip("Windows Time Zone ID used when useWestIndonesianTime=true (default: SE Asia Standard Time)")]
    public string windowsTimeZoneId = "SE Asia Standard Time"; // Bangkok, Hanoi, Jakarta (UTC+7)

    [Tooltip("IANA Time Zone ID fallback for non-Windows platforms (default: Asia/Jakarta)")]
    public string ianaTimeZoneId = "Asia/Jakarta"; // UTC+7

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

        if (enableDebugLogs)
        {
            Debug.Log($"[ScoreApiClient] Sending {req.method} {req.url}");
        }

        yield return req.SendWebRequest();

        #if UNITY_2020_1_OR_NEWER
        if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
        #else
        if (req.isNetworkError || req.isHttpError)
        #endif
        {
            Debug.LogWarning($"ScoreApiClient request error: {req.responseCode} {req.error} -> {req.url}");
        }
        else if (enableDebugLogs)
        {
            Debug.Log($"[ScoreApiClient] Response {req.responseCode} {req.method} {req.url}");
        }
        onDone?.Invoke(req);
    }

    // Returns current time formatted as ISO-8601; if useWestIndonesianTime is true, returns WIB (UTC+7) with offset
    string NowIsoString()
    {
        if (!useWestIndonesianTime)
        {
            return DateTime.UtcNow.ToString("o");
        }

        // Prefer system timezone if available; otherwise fall back to +07 offset
        try
        {
            if (!string.IsNullOrEmpty(windowsTimeZoneId))
            {
                var tzWin = TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);
                var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tzWin);
                return local.ToString("o");
            }
        }
        catch { /* ignore and try IANA */ }

        try
        {
            if (!string.IsNullOrEmpty(ianaTimeZoneId))
            {
                var tzIana = TimeZoneInfo.FindSystemTimeZoneById(ianaTimeZoneId);
                var local = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tzIana);
                return local.ToString("o");
            }
        }
        catch { /* ignore and fall back */ }

        // Fallback: manually apply +07:00 offset
        var wib = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(7));
        return wib.ToString("o");
    }

    // GET leaderboard page
    public void GetLeaderboard(int page, int pageSize, Action<PlayerScore[], int?> onComplete)
    {
        StartCoroutine(GetLeaderboardCoroutine(page, pageSize, onComplete));
    }

    IEnumerator GetLeaderboardCoroutine(int page, int pageSize, Action<PlayerScore[], int?> onComplete)
    {
        string url = $"{baseUrl}{ScoresPath}?page={page}&pageSize={pageSize}";
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] GET leaderboard page={page} size={pageSize} url={url}");
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
                if (enableDebugLogs)
                    Debug.Log($"[ScoreApiClient] GET leaderboard success items={(arr==null?0:arr.Length)} total={(total.HasValue?total.Value.ToString():"?")}");
                onComplete?.Invoke(arr, total);
            }
            else
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ScoreApiClient] GET leaderboard failed code={req.responseCode} error={req.error}");
                onComplete?.Invoke(null, null);
            }
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

        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] FindPlayerRecord (case-sensitive) '{playerName}', maxPages={maxPages}");

        int page = 1;
        int pageSize = 100; // server caps >100 to 10; use 100 as max allowed

        while (page <= maxPages)
        {
            if (enableDebugLogs)
                Debug.Log($"[ScoreApiClient] Scanning page {page} (size {pageSize}) for '{playerName}'");
            PlayerScore[] results = null;
            int? total = null;
            bool done = false;
            yield return GetLeaderboardCoroutine(page, pageSize, (items, tot) => { results = items; total = tot; done = true; });
            // wait for coroutine to set done
            while (!done) yield return null;

            if (results == null || results.Length == 0) break;

            foreach (var r in results)
            {
                // Case-sensitive comparison, treat different capitalization as different users
                if (string.Equals(r.PlayerName, playerName, StringComparison.Ordinal))
                {
                    if (enableDebugLogs)
                        Debug.Log($"[ScoreApiClient] Found '{playerName}' id={r.Id} score={r.Score}");
                    onComplete?.Invoke(r);
                    yield break;
                }
            }

            // stop if fewer than pageSize results returned (last page)
            if (results.Length < pageSize) break;
            page++;
        }
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] Player '{playerName}' not found after scanning {page-1} pages");
        onComplete?.Invoke(null);
    }

    // Ensure player exists: if not found, POST to create with Score=0
    public void EnsurePlayerExists(string playerName, Action<PlayerScore> onComplete)
    {
        StartCoroutine(EnsurePlayerExistsCoroutine(playerName, onComplete));
    }

    IEnumerator EnsurePlayerExistsCoroutine(string playerName, Action<PlayerScore> onComplete)
    {
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] EnsurePlayerExists '{playerName}'");
        PlayerScore found = null;
        bool finished = false;
        FindPlayerRecord(playerName, (r) => { found = r; finished = true; });
        while (!finished) yield return null;

        if (found != null)
        {
            if (enableDebugLogs)
                Debug.Log($"[ScoreApiClient] Player exists id={found.Id} score={found.Score}");
            onComplete?.Invoke(found);
            yield break;
        }

        // create
    var create = new PlayerScore { PlayerName = playerName, Score = 0, CreatedAt = NowIsoString() };
        string json = JsonUtility.ToJson(create);
        string url = $"{baseUrl}{ScoresPath}";
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] Creating new player '{playerName}' via POST {url}");
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
                if (enableDebugLogs)
                    Debug.Log($"[ScoreApiClient] Create player success id={(created!=null?created.Id:-1)} name='{created?.PlayerName}'");
                onComplete?.Invoke(created);
            }
            else
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ScoreApiClient] Create player failed code={req.responseCode} error={req.error}");
                onComplete?.Invoke(null);
            }
        }
    }

    // Update score: if existing record found, PUT to /api/scores/{id}; otherwise POST new
    public void UpdateScoreForPlayer(string playerName, int score, Action<PlayerScore> onComplete)
    {
        StartCoroutine(UpdateScoreForPlayerCoroutine(playerName, score, onComplete));
    }

    IEnumerator UpdateScoreForPlayerCoroutine(string playerName, int score, Action<PlayerScore> onComplete)
    {
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] UpdateScoreForPlayer '{playerName}' -> {score}");
        PlayerScore existing = null;
        bool done = false;
        FindPlayerRecord(playerName, (r) => { existing = r; done = true; });
        while (!done) yield return null;

        if (existing == null)
        {
            // create new record
            if (enableDebugLogs)
                Debug.Log($"[ScoreApiClient] No existing record for '{playerName}', creating new with score={score}");
            var create = new PlayerScore { PlayerName = playerName, Score = score, CreatedAt = NowIsoString() };
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
                    if (enableDebugLogs)
                        Debug.Log($"[ScoreApiClient] Create score success id={(created!=null?created.Id:-1)} name='{created?.PlayerName}' score={created?.Score}");
                    onComplete?.Invoke(created);
                }
                else
                {
                    if (enableDebugLogs)
                        Debug.LogWarning($"[ScoreApiClient] Create score failed code={req.responseCode} error={req.error}");
                    onComplete?.Invoke(null);
                }
            }
            yield break;
        }

    // update existing
    existing.Score = score;
    // update CreatedAt to the time of this score update (as requested)
    existing.CreatedAt = NowIsoString();
        string putJson = JsonUtility.ToJson(existing);
        string putUrl = $"{baseUrl}{ScoresPath}/{existing.Id}";
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] Updating id={existing.Id} score={existing.Score} via PUT {putUrl}");
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
                if (enableDebugLogs)
                    Debug.Log($"[ScoreApiClient] Update success for id={existing.Id}, fetching the updated record");
                string getUrl = $"{baseUrl}{ScoresPath}/{existing.Id}";
                using (var getReq = UnityWebRequest.Get(getUrl))
                {
                    yield return SendRequest(getReq, null);
                    if (getReq.responseCode >= 200 && getReq.responseCode < 300)
                    {
                        var createdTxt = getReq.downloadHandler.text;
                        var updated = ParseSingleJson(createdTxt);
                        if (enableDebugLogs)
                            Debug.Log($"[ScoreApiClient] GET-by-id success id={(updated!=null?updated.Id:existing.Id)} score={(updated!=null?updated.Score:existing.Score)}");
                        onComplete?.Invoke(updated ?? existing);
                        yield break;
                    }
                }
                if (enableDebugLogs)
                    Debug.LogWarning($"[ScoreApiClient] GET-by-id after update failed, returning local existing record id={existing.Id}");
                onComplete?.Invoke(existing);
            }
            else
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ScoreApiClient] Update failed code={req.responseCode} error={req.error}");
                onComplete?.Invoke(null);
            }
        }
    }

    public void DeleteRecordByPlayer(string playerName, Action<bool> onComplete)
    {
        StartCoroutine(DeleteRecordByPlayerCoroutine(playerName, onComplete));
    }

    IEnumerator DeleteRecordByPlayerCoroutine(string playerName, Action<bool> onComplete)
    {
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] DeleteRecordByPlayer '{playerName}'");
        PlayerScore existing = null;
        bool done = false;
        FindPlayerRecord(playerName, (r) => { existing = r; done = true; });
        while (!done) yield return null;

        if (existing == null)
        {
            if (enableDebugLogs)
                Debug.Log($"[ScoreApiClient] No record found for '{playerName}' to delete");
            onComplete?.Invoke(false);
            yield break;
        }

        string url = $"{baseUrl}{ScoresPath}/{existing.Id}";
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] Deleting id={existing.Id} via DELETE {url}");
        using (var req = UnityWebRequest.Delete(url))
        {
            yield return SendRequest(req, null);
            bool ok = req.responseCode >= 200 && req.responseCode < 300;
            if (enableDebugLogs)
            {
                if (ok) Debug.Log($"[ScoreApiClient] Delete success id={existing.Id}");
                else Debug.LogWarning($"[ScoreApiClient] Delete failed id={existing.Id} code={req.responseCode} error={req.error}");
            }
            onComplete?.Invoke(ok);
        }
    }

    public void GetRank(string playerName, Action<RankResult> onComplete)
    {
        StartCoroutine(GetRankCoroutine(playerName, onComplete));
    }

    IEnumerator GetRankCoroutine(string playerName, Action<RankResult> onComplete)
    {
        if (string.IsNullOrWhiteSpace(playerName)) { onComplete?.Invoke(null); yield break; }
        string url = $"{baseUrl}{ScoresPath}/rank/{UnityWebRequest.EscapeURL(playerName)}";
        if (enableDebugLogs)
            Debug.Log($"[ScoreApiClient] GET rank for '{playerName}' url={url}");
        using (var req = UnityWebRequest.Get(url))
        {
            yield return SendRequest(req, null);
            if (req.responseCode >= 200 && req.responseCode < 300)
            {
                try
                {
                    var txt = req.downloadHandler.text;
                    var rank = JsonUtility.FromJson<RankResult>(txt);
                    if (enableDebugLogs && rank != null)
                        Debug.Log($"[ScoreApiClient] Rank result player={rank.player} score={rank.score} rank={rank.rank}");
                    onComplete?.Invoke(rank);
                }
                catch { onComplete?.Invoke(null); }
            }
            else
            {
                if (enableDebugLogs)
                    Debug.LogWarning($"[ScoreApiClient] GET rank failed code={req.responseCode} error={req.error}");
                onComplete?.Invoke(null);
            }
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
