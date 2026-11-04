using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoldeClean;

/// <summary>분류 체계의 개념 하나. 상위 개념을 타고 올라가 계층을 이룬다.</summary>
public sealed class Concept
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("parent")] public string? Parent { get; set; }
    [JsonPropertyName("labels")] public Dictionary<string, string> Labels { get; set; } = new();
    /// <summary>이 개념이 가져가는 확장자. 최상위 개념에만 둔다.</summary>
    [JsonPropertyName("ext")] public List<string> Ext { get; set; } = new();
    /// <summary>세부 개념의 참고용 확장자. 분류를 가로채지는 않는다.</summary>
    [JsonPropertyName("ext_hint")] public List<string> ExtHint { get; set; } = new();
    /// <summary>파일 이름에 이 말이 들어가면 이 개념으로 본다.</summary>
    [JsonPropertyName("name_patterns")] public List<string> NamePatterns { get; set; } = new();
    [JsonPropertyName("synonyms")] public Dictionary<string, List<string>> Synonyms { get; set; } = new();
}

public sealed class OntologyFile
{
    [JsonPropertyName("version")] public int Version { get; set; } = 1;
    [JsonPropertyName("concepts")] public List<Concept> Concepts { get; set; } = new();
}

/// <summary>
/// 분류 체계. 예전에는 확장자와 분류가 코드에 박혀 있었는데, 이제 ontology.json 이 정한다.
/// 기본 파일은 exe 안에 들어 있고, %LOCALAPPDATA%\FoldeClean\ontology.json 이 있으면 그것을 쓴다.
///
/// 중요: 정리 폴더 이름에는 **최상위 개념만** 쓴다. 세부 개념(계약서, 스크린샷 …)은
/// 내 규칙과 검색에서만 쓴다. 그래야 예전에 정리해 둔 결과와 어긋나지 않는다.
/// </summary>
public static class Ontology
{
    static readonly object _lock = new();
    static OntologyFile? _data;
    static Dictionary<string, Concept> _byId = new(StringComparer.OrdinalIgnoreCase);
    static Dictionary<string, string> _extToTop = new(StringComparer.OrdinalIgnoreCase);
    static List<Concept> _withPatterns = new();

    public static string UserPath => Path.Combine(Paths.AppData, "ontology.json");

    public static OntologyFile Data
    {
        get { lock (_lock) { if (_data == null) Load(); return _data!; } }
    }

    static void Load()
    {
        OntologyFile? f = null;
        // 1) 사용자가 둔 파일이 있으면 그것을 쓴다
        try { if (File.Exists(UserPath)) f = JsonSerializer.Deserialize<OntologyFile>(File.ReadAllText(UserPath)); }
        catch (Exception ex) { Log.Error("ontology user file", ex); }
        // 2) 없으면 exe 안에 든 기본 파일
        if (f == null || f.Concepts.Count == 0)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var s = asm.GetManifestResourceStream("ontology.json");
                if (s != null) { using var r = new StreamReader(s); f = JsonSerializer.Deserialize<OntologyFile>(r.ReadToEnd()); }
            }
            catch (Exception ex) { Log.Error("ontology embedded", ex); }
        }
        _data = f ?? new OntologyFile();

        _byId = _data.Concepts.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _extToTop = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in _data.Concepts.Where(c => c.Parent == null))
            foreach (var e in c.Ext)
                _extToTop.TryAdd(e, c.Id);   // 먼저 나온 개념이 이긴다
        _withPatterns = _data.Concepts.Where(c => c.NamePatterns.Count > 0).ToList();
    }

    /// <summary>사용자가 파일을 고친 뒤 다시 읽는다.</summary>
    public static void Reload() { lock (_lock) { _data = null; } _ = Data; }

    /// <summary>확장자가 속한 최상위 개념. 모르면 other.</summary>
    public static string TopIdOf(string ext)
    {
        _ = Data;
        return _extToTop.TryGetValue(ext ?? "", out var id) ? id : "other";
    }

    /// <summary>현재 언어의 이름. 없으면 한국어, 그것도 없으면 id.</summary>
    public static string Label(string conceptId)
    {
        _ = Data;
        if (!_byId.TryGetValue(conceptId ?? "", out var c)) return conceptId ?? "";
        if (c.Labels.TryGetValue(I18n.Lang, out var l) && !string.IsNullOrEmpty(l)) return l;
        if (c.Labels.TryGetValue("ko", out var ko) && !string.IsNullOrEmpty(ko)) return ko;
        return c.Id;
    }

    /// <summary>이 개념과 그 위로 올라가는 모든 상위 개념 (자기 자신 포함).</summary>
    public static IEnumerable<string> Ancestors(string conceptId)
    {
        _ = Data;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cur = conceptId;
        while (!string.IsNullOrEmpty(cur) && seen.Add(cur))
        {
            yield return cur;
            cur = _byId.TryGetValue(cur, out var c) ? c.Parent : null;
        }
    }

    /// <summary>conceptId 가 ancestorId 이거나 그 아래인가.</summary>
    public static bool IsA(string conceptId, string ancestorId) =>
        Ancestors(conceptId).Contains(ancestorId, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 파일이 속한 개념들. 첫 번째는 언제나 최상위 개념(폴더 이름에 쓰는 것),
    /// 뒤에 이름으로 알아낸 세부 개념이 붙는다.
    /// </summary>
    public static List<string> ConceptsOf(FileRec f)
    {
        _ = Data;
        var top = TopIdOf(f.Ext);
        var list = new List<string> { top };
        var name = f.Name.ToLowerInvariant();
        foreach (var c in _withPatterns)
        {
            if (c.Id.Equals(top, StringComparison.OrdinalIgnoreCase)) continue;
            // 세부 개념은 자기 상위 개념과 같은 갈래일 때만 인정한다 (계약서는 문서 아래에서만)
            if (c.Parent != null && !IsA(top, c.Parent)) continue;
            if (c.NamePatterns.Any(p => !string.IsNullOrEmpty(p) && name.Contains(p.ToLowerInvariant())))
                list.Add(c.Id);
        }
        return list;
    }

    /// <summary>검색어를 개념의 동의어로 넓힌다. "계약" → 계약, 계약서, contract …</summary>
    public static List<string> ExpandQuery(string query)
    {
        _ = Data;
        var q = (query ?? "").Trim();
        var outSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (q.Length == 0) return new List<string>();
        outSet.Add(q);
        var ql = q.ToLowerInvariant();

        foreach (var c in _data!.Concepts)
        {
            bool hit = c.Labels.Values.Any(v => !string.IsNullOrEmpty(v) && (v.ToLowerInvariant().Contains(ql) || ql.Contains(v.ToLowerInvariant())))
                    || c.Synonyms.Values.SelectMany(v => v).Any(v => !string.IsNullOrEmpty(v) && (v.ToLowerInvariant().Contains(ql) || ql.Contains(v.ToLowerInvariant())))
                    || c.Id.Equals(q, StringComparison.OrdinalIgnoreCase);
            if (!hit) continue;
            foreach (var v in c.Labels.Values) if (!string.IsNullOrEmpty(v)) outSet.Add(v);
            foreach (var v in c.Synonyms.Values.SelectMany(v => v)) if (!string.IsNullOrEmpty(v)) outSet.Add(v);
            foreach (var p in c.NamePatterns) if (!string.IsNullOrEmpty(p)) outSet.Add(p);
        }
        return outSet.ToList();
    }

    /// <summary>화면에서 고를 수 있는 개념 목록 (규칙 편집기의 "개념" 조건용).</summary>
    public static List<Dictionary<string, object?>> ForPicker()
    {
        _ = Data;
        var order = new List<Concept>();
        foreach (var top in _data!.Concepts.Where(c => c.Parent == null))
        {
            order.Add(top);
            order.AddRange(_data.Concepts.Where(c => string.Equals(c.Parent, top.Id, StringComparison.OrdinalIgnoreCase)));
        }
        return order.Select(c => new Dictionary<string, object?>
        {
            ["id"] = c.Id, ["label"] = Label(c.Id), ["parent"] = c.Parent, ["top"] = c.Parent == null,
        }).ToList();
    }
}
