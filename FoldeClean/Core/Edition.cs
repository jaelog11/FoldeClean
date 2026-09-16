using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoldeClean;

/// <summary>
/// 판 구분. Standard(무료) / Trial(체험) / Pro(구매).
/// 상태는 %LOCALAPPDATA%\FoldeClean\edition.json 에 저장한다.
/// 지금은 정직 모델(복사 방지 없음). 실제 구매 확인은 Microsoft Store 인앱 구매를 붙일 때 연결한다.
/// </summary>
public static class Edition
{
    public const int TrialDays = 14;
    static readonly string StatePath = Path.Combine(Paths.AppData, "edition.json");
    static readonly object _lock = new();
    static State? _cache;

    sealed class State
    {
        [JsonPropertyName("trial_started")] public string? TrialStarted { get; set; }   // ISO 8601 UTC
        [JsonPropertyName("license")] public string? License { get; set; }              // 추후 Store/키
    }

    static State Load()
    {
        lock (_lock)
        {
            if (_cache != null) return _cache;
            try { _cache = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath)) ?? new State(); }
            catch { _cache = new State(); }
            return _cache;
        }
    }

    static void Save(State s)
    {
        lock (_lock)
        {
            _cache = s;
            try
            {
                Directory.CreateDirectory(Paths.AppData);
                File.WriteAllText(StatePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { Log.Error("edition save", ex); }
        }
    }

    static DateTime? TrialStartUtc
    {
        get
        {
            var v = Load().TrialStarted;
            return DateTime.TryParse(v, null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        }
    }

    public static bool HasLicense => !string.IsNullOrWhiteSpace(Load().License);

    /// <summary>체험 남은 일수. 시작 전이면 null, 만료면 0.</summary>
    public static int? TrialDaysLeft
    {
        get
        {
            if (TrialStartUtc is not DateTime start) return null;
            var used = (DateTime.UtcNow - start).TotalDays;
            return (int)Math.Max(0, Math.Ceiling(TrialDays - used));
        }
    }

    public static string Current =>
        HasLicense ? "pro" : (TrialDaysLeft is > 0 ? "trial" : "standard");

    /// <summary>Pro 기능을 쓸 수 있는가 (구매 또는 체험 중).</summary>
    public static bool ProActive => Current is "pro" or "trial";

    public static bool TrialUsed => TrialStartUtc != null;

    public static bool StartTrial()
    {
        if (TrialUsed || HasLicense) return false;
        var s = Load();
        s.TrialStarted = DateTime.UtcNow.ToString("o");
        Save(s);
        Log.Info("trial started");
        return true;
    }

    /// <summary>개발·검증용. 실제 구매 연결 전까지 수동으로 Pro 상태를 넣을 때 쓴다.</summary>
    public static void SetLicense(string? key)
    {
        var s = Load();
        s.License = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
        Save(s);
    }

    public static Dictionary<string, object?> Info() => new()
    {
        ["edition"] = Current,
        ["pro_active"] = ProActive,
        ["trial_days_left"] = TrialDaysLeft,
        ["trial_used"] = TrialUsed,
        ["trial_days"] = TrialDays,
    };

    /// <summary>Pro 전용 기능 목록. 화면에서 잠금 표시에 쓴다.</summary>
    public static readonly string[] ProFeatures = { "rules", "profiles", "schedule", "destination" };
}
