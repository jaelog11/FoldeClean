using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FoldeClean;

/// <summary>규칙 조건 하나. 한 규칙 안의 조건은 모두 만족해야 한다(AND).</summary>
public sealed class RuleCondition
{
    /// <summary>name | ext | size | age | dir | path</summary>
    [JsonPropertyName("field")] public string Field { get; set; } = "name";
    /// <summary>contains | not_contains | equals | starts | ends | regex | gt | lt</summary>
    [JsonPropertyName("op")] public string Op { get; set; } = "contains";
    [JsonPropertyName("value")] public string Value { get; set; } = "";

    public bool Matches(FileRec f, string root)
    {
        try
        {
            return Field switch
            {
                "size" => Number(f.Size),
                "age" => Number((TimeUtil.NowUnix - f.Mtime) / 86400.0),
                "ext" => Text(f.Ext),
                "dir" => Text(Path.GetFileName(Path.GetDirectoryName(f.Path) ?? "")),
                "path" => Text(f.Path),
                _ => Text(f.Name),
            };
        }
        catch { return false; }   // 잘못된 정규식 등은 '맞지 않음'으로 처리
    }

    bool Number(double actual)
    {
        if (!double.TryParse(Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var v)) return false;
        return Op switch { "gt" => actual > v, "lt" => actual < v, "equals" => Math.Abs(actual - v) < 0.0001, _ => false };
    }

    bool Text(string actual)
    {
        actual ??= "";
        var v = Value ?? "";
        const StringComparison ic = StringComparison.OrdinalIgnoreCase;
        return Op switch
        {
            "contains" => actual.Contains(v, ic),
            "not_contains" => !actual.Contains(v, ic),
            "equals" => actual.Equals(v, ic),
            "starts" => actual.StartsWith(v, ic),
            "ends" => actual.EndsWith(v, ic),
            "regex" => Regex.IsMatch(actual, v, RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)),
            _ => false,
        };
    }
}

/// <summary>사용자 규칙 하나. 조건을 모두 만족하면 동작을 적용하고, 그 파일에 대한 규칙 평가는 끝난다.</summary>
public sealed class Rule
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    [JsonPropertyName("enabled")] public bool Enabled { get; set; } = true;
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("conditions")] public List<RuleCondition> Conditions { get; set; } = new();
    /// <summary>folder | skip | prefix | suffix</summary>
    [JsonPropertyName("action")] public string Action { get; set; } = "folder";
    [JsonPropertyName("value")] public string Value { get; set; } = "";

    public bool Matches(FileRec f, string root) =>
        Enabled && Conditions.Count > 0 && Conditions.All(c => c.Matches(f, root));
}

/// <summary>
/// 규칙 모음. %LOCALAPPDATA%\FoldeClean\rules.json 에 저장.
/// 계획을 세울 때 전략보다 먼저 평가되며, 처음 맞는 규칙 하나만 적용된다.
/// </summary>
public sealed class RuleSet
{
    [JsonPropertyName("rules")] public List<Rule> Rules { get; set; } = new();

    static string Path_ => System.IO.Path.Combine(Paths.AppData, "rules.json");

    public static RuleSet Load()
    {
        try { return JsonSerializer.Deserialize<RuleSet>(File.ReadAllText(Path_)) ?? new RuleSet(); }
        catch { return new RuleSet(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Paths.AppData);
        var tmp = Path_ + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true, Encoder = Json.Options.Encoder }));
        File.Move(tmp, Path_, overwrite: true);
    }

    public Rule? FirstMatch(FileRec f, string root)
    {
        foreach (var r in Rules)
            if (r.Matches(f, root)) return r;
        return null;
    }

    /// <summary>
    /// 값의 치환자를 실제 값으로 바꾼다: {year} {month} {day} {ext} {category} {dir}
    /// 앞뒤 공백은 지우지 않는다. 이름 앞뒤에 붙이는 동작에서는 공백이 의도된 값일 수 있다.
    /// </summary>
    public static string Expand(string template, FileRec f)
    {
        if (string.IsNullOrEmpty(template)) return "";
        var t = TimeUtil.FromUnix(f.Mtime);
        return template
            .Replace("{year}", t.ToString("yyyy"))
            .Replace("{month}", t.ToString("MM"))
            .Replace("{day}", t.ToString("dd"))
            .Replace("{ext}", f.Ext)
            .Replace("{category}", Categories.Of(f).label)
            .Replace("{dir}", System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(f.Path) ?? ""));
    }

    /// <summary>규칙 하나를 시험 파일 목록에 적용해 몇 개가 걸리는지 세어 본다(편집기 미리보기용).</summary>
    public static Dictionary<string, object?> Test(Rule rule, List<FileRec> files, string root, int sampleCount = 8)
    {
        var hits = files.Where(f => rule.Matches(f, root)).ToList();
        return new()
        {
            ["count"] = hits.Count,
            ["samples"] = hits.Take(sampleCount).Select(f => new Dictionary<string, object?>
            {
                ["name"] = f.Name,
                ["dest"] = rule.Action == "skip" ? null : Expand(rule.Value, f),
            }).ToList(),
        };
    }
}
