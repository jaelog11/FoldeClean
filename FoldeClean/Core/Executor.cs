using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoldeClean;

public sealed class JournalEntry
{
    [JsonPropertyName("src")] public string Src { get; set; } = "";
    [JsonPropertyName("dst")] public string Dst { get; set; } = "";
    [JsonPropertyName("error")] public string? Error { get; set; }
}

public sealed class Journal
{
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("root")] public string? Root { get; set; }
    [JsonPropertyName("entries")] public List<JournalEntry> Entries { get; set; } = new();
    [JsonPropertyName("failed")] public List<JournalEntry> Failed { get; set; } = new();
    [JsonPropertyName("removed_dirs")] public List<string> RemovedDirs { get; set; } = new();
    [JsonPropertyName("undone")] public string? Undone { get; set; }
    [JsonPropertyName("recovered")] public bool Recovered { get; set; }   // 강제 종료 후 .jsonl 로부터 복구됨
}

/// <summary>
/// 이동 실행과 되돌리기.
/// 이동 한 건이 끝날 때마다 journals/&lt;시각&gt;.jsonl 에 한 줄을 즉시 덧붙인다(정전·강제 종료 대비).
/// 작업이 끝나면 같은 이름의 .json 요약본을 만든다. .json 이 없고 .jsonl 만 있으면 중단된 작업이므로 복구해서 보여준다.
/// </summary>
public sealed class Executor
{
    readonly object _lock = new();
    public volatile bool CancelRequested;
    bool _running, _finished; int _done, _total, _failed; string _current = ""; string? _journal;

    public Dictionary<string, object?> Snapshot()
    {
        lock (_lock) return new() { ["running"] = _running, ["done"] = _done, ["total"] = _total, ["failed"] = _failed, ["current"] = _current, ["journal"] = _journal, ["finished"] = _finished };
    }
    public bool Running { get { lock (_lock) return _running; } }

    static readonly JsonSerializerOptions LineOpts = new() { Encoder = Json.Options.Encoder };

    static void Move(string src, string dst)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
        File.Move(src, dst, overwrite: false);
    }

    static void WriteJson(string path, Journal j)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(j, new JsonSerializerOptions { WriteIndented = true, Encoder = Json.Options.Encoder }));
        File.Move(tmp, path, overwrite: true);
    }

    static List<string> RemoveEmpty(string root)
    {
        var removed = new List<string>();
        IEnumerable<string> dirs;
        try { dirs = Directory.EnumerateDirectories(root, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }).OrderByDescending(d => d.Length).ToList(); }
        catch { return removed; }
        foreach (var d in dirs)
        {
            try { if (!Directory.EnumerateFileSystemEntries(d).Any()) { Directory.Delete(d); removed.Add(d); } } catch { }
        }
        return removed;
    }

    public string Run(List<MoveRec> moves, bool removeEmptyDirs, string? root)
    {
        Directory.CreateDirectory(Paths.Journals);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var jpath = Path.Combine(Paths.Journals, stamp + ".json");
        var lpath = Path.Combine(Paths.Journals, stamp + ".jsonl");
        var j = new Journal { Created = stamp, Root = root };
        CancelRequested = false;
        lock (_lock) { _running = true; _finished = false; _done = 0; _total = moves.Count; _failed = 0; _current = ""; _journal = jpath; }

        // 한 줄 기록: 첫 줄은 머리글, 이후 한 건마다 {"src","dst"} 또는 {"src","dst","error"}
        using (var log = new StreamWriter(new FileStream(lpath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
        {
            log.AutoFlush = true;
            log.WriteLine(JsonSerializer.Serialize(new { header = true, created = stamp, root, total = moves.Count }, LineOpts));
            for (int i = 0; i < moves.Count; i++)
            {
                if (CancelRequested) break;
                var m = moves[i];
                JournalEntry e;
                try { Move(m.Src, m.Dst); e = new JournalEntry { Src = m.Src, Dst = m.Dst }; j.Entries.Add(e); }
                catch (Exception ex) { e = new JournalEntry { Src = m.Src, Dst = m.Dst, Error = ex.Message }; j.Failed.Add(e); lock (_lock) _failed++; }
                log.WriteLine(JsonSerializer.Serialize(e, LineOpts));   // AutoFlush: 즉시 디스크로
                lock (_lock) { _done = i + 1; _current = m.Name; }
            }
        }
        if (removeEmptyDirs && root != null) j.RemovedDirs = RemoveEmpty(root);
        WriteJson(jpath, j);
        lock (_lock) { _running = false; _finished = true; }
        return jpath;
    }

    /// <summary>.json 이 없는 .jsonl(중단된 작업)을 읽어 Journal 로 복원하고 .json 을 만든다.</summary>
    public static Journal? RecoverFromLog(string lpath)
    {
        try
        {
            var lines = File.ReadAllLines(lpath);
            if (lines.Length == 0) return null;
            using var head = JsonDocument.Parse(lines[0]);
            var j = new Journal
            {
                Created = head.RootElement.TryGetProperty("created", out var c) ? c.GetString() ?? "" : Path.GetFileNameWithoutExtension(lpath),
                Root = head.RootElement.TryGetProperty("root", out var r) ? r.GetString() : null,
                Recovered = true,
            };
            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                JournalEntry? e;
                try { e = JsonSerializer.Deserialize<JournalEntry>(line); } catch { continue; }   // 마지막 줄이 잘렸을 수 있음
                if (e == null) continue;
                if (e.Error == null) j.Entries.Add(e); else j.Failed.Add(e);
            }
            WriteJson(Path.ChangeExtension(lpath, ".json"), j);
            return j;
        }
        catch { return null; }
    }

    public Dictionary<string, object?> Undo(string journalPath)
    {
        var j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)) ?? new Journal();
        int ok = 0, fail = 0;
        lock (_lock) { _running = true; _finished = false; _done = 0; _total = j.Entries.Count; _failed = 0; _current = "undo"; }
        for (int i = j.Entries.Count - 1; i >= 0; i--)
        {
            var e = j.Entries[i];
            try { if (File.Exists(e.Dst)) { Move(e.Dst, e.Src); ok++; } else fail++; }
            catch { fail++; }
            lock (_lock) { _done = j.Entries.Count - i; _failed = fail; }
        }
        if (j.Root != null) RemoveEmpty(j.Root);
        j.Undone = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        WriteJson(journalPath, j);
        lock (_lock) { _running = false; _finished = true; }
        return new() { ["restored"] = ok, ["failed"] = fail };
    }

    public static List<Dictionary<string, object?>> ListJournals()
    {
        var list = new List<Dictionary<string, object?>>();
        if (!Directory.Exists(Paths.Journals)) return list;
        // 중단된 작업(.json 없는 .jsonl) 먼저 복구
        foreach (var l in Directory.EnumerateFiles(Paths.Journals, "*.jsonl"))
            if (!File.Exists(Path.ChangeExtension(l, ".json"))) RecoverFromLog(l);
        foreach (var p in Directory.EnumerateFiles(Paths.Journals, "*.json").OrderByDescending(x => x))
        {
            try
            {
                var j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(p));
                if (j == null) continue;
                list.Add(new() { ["path"] = p, ["created"] = j.Created, ["root"] = j.Root, ["count"] = j.Entries.Count, ["failed"] = j.Failed.Count, ["undone"] = j.Undone, ["recovered"] = j.Recovered });
            }
            catch { }
        }
        return list;
    }
}
