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
    /// <summary>계획을 세운 뒤 사용자가 직접 지워서 옮길 것이 없던 경우. 오류가 아니다.</summary>
    [JsonPropertyName("missing")] public bool Missing { get; set; }
}

public sealed class Journal
{
    [JsonPropertyName("created")] public string Created { get; set; } = "";
    [JsonPropertyName("root")] public string? Root { get; set; }
    [JsonPropertyName("entries")] public List<JournalEntry> Entries { get; set; } = new();
    [JsonPropertyName("failed")] public List<JournalEntry> Failed { get; set; } = new();
    [JsonPropertyName("missing")] public List<JournalEntry> MissingFiles { get; set; } = new();
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
    bool _running, _finished; int _done, _total, _failed, _missing; string _current = ""; string? _journal;

    public Dictionary<string, object?> Snapshot()
    {
        lock (_lock) return new() { ["running"] = _running, ["done"] = _done, ["total"] = _total, ["failed"] = _failed, ["missing"] = _missing, ["current"] = _current, ["journal"] = _journal, ["finished"] = _finished };
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

    /// <summary>
    /// 우리가 파일을 빼내서 비게 된 폴더만 지운다. 그 폴더가 비면 위로 한 칸씩 올라가며 계속 확인하되,
    /// 정리 대상 폴더(root) 자체와 root 밖은 절대 건드리지 않는다.
    /// 예전에는 root 전체를 훑어 빈 폴더를 모두 지웠는데, 검사하지도 않은 깊은 곳의
    /// 사용자 폴더까지 사라져서 범위를 이렇게 좁혔다.
    /// </summary>
    static List<string> RemoveEmptiedDirs(IEnumerable<string> touchedDirs, string root)
    {
        var removed = new List<string>();
        string Norm(string p) { try { return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar); } catch { return p; } }
        var rootN = Norm(root);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var start in touchedDirs.Where(d => !string.IsNullOrEmpty(d))
                                         .Distinct(StringComparer.OrdinalIgnoreCase)
                                         .OrderByDescending(d => d.Length))
        {
            var cur = start;
            while (!string.IsNullOrEmpty(cur))
            {
                var n = Norm(cur);
                if (n.Equals(rootN, StringComparison.OrdinalIgnoreCase)) break;                       // 루트는 유지
                if (!n.StartsWith(rootN + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) break;  // 루트 밖은 건드리지 않음
                if (!seen.Add(n)) break;
                try
                {
                    if (!Directory.Exists(cur)) { cur = Path.GetDirectoryName(cur); continue; }
                    if (Directory.EnumerateFileSystemEntries(cur).Any()) break;                       // 아직 무언가 남아 있으면 중단
                    Directory.Delete(cur);
                    removed.Add(cur);
                }
                catch { break; }
                cur = Path.GetDirectoryName(cur);
            }
        }
        return removed;
    }

    /// <summary>탐색기에 이 폴더가 바뀌었다고 알려 화면을 새로 그리게 한다 (바탕화면이 갱신되지 않는 문제).</summary>
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern void SHChangeNotify(int eventId, uint flags, string? item1, string? item2);
    const int SHCNE_UPDATEDIR = 0x00001000;
    const uint SHCNF_PATHW = 0x0005;

    static void NotifyShell(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { SHChangeNotify(SHCNE_UPDATEDIR, SHCNF_PATHW, path, null); }
        catch (Exception ex) { Log.Error("shell notify", ex); }
    }

    public string Run(List<MoveRec> moves, bool removeEmptyDirs, string? root)
    {
        Directory.CreateDirectory(Paths.Journals);
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var jpath = Path.Combine(Paths.Journals, stamp + ".json");
        var lpath = Path.Combine(Paths.Journals, stamp + ".jsonl");
        var j = new Journal { Created = stamp, Root = root };
        CancelRequested = false;
        lock (_lock) { _running = true; _finished = false; _done = 0; _total = moves.Count; _failed = 0; _missing = 0; _current = ""; _journal = jpath; }

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
                // 계획을 세운 뒤 사용자가 탐색기에서 지웠을 수 있다. 오류가 아니라 "이미 없음"으로 센다.
                if (!File.Exists(m.Src))
                {
                    e = new JournalEntry { Src = m.Src, Dst = m.Dst, Missing = true };
                    j.MissingFiles.Add(e);
                    lock (_lock) _missing++;
                }
                else
                {
                    try { Move(m.Src, m.Dst); e = new JournalEntry { Src = m.Src, Dst = m.Dst }; j.Entries.Add(e); }
                    catch (FileNotFoundException) { e = new JournalEntry { Src = m.Src, Dst = m.Dst, Missing = true }; j.MissingFiles.Add(e); lock (_lock) _missing++; }
                    catch (Exception ex) { e = new JournalEntry { Src = m.Src, Dst = m.Dst, Error = ex.Message }; j.Failed.Add(e); lock (_lock) _failed++; }
                }
                log.WriteLine(JsonSerializer.Serialize(e, LineOpts));   // AutoFlush: 즉시 디스크로
                lock (_lock) { _done = i + 1; _current = m.Name; }
            }
        }
        // 우리가 비운 출발 폴더만 정리한다
        if (removeEmptyDirs && root != null)
            j.RemovedDirs = RemoveEmptiedDirs(j.Entries.Select(e => Path.GetDirectoryName(e.Src) ?? ""), root);
        WriteJson(jpath, j);
        NotifyShell(root);
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
                if (e.Missing) j.MissingFiles.Add(e);
                else if (e.Error == null) j.Entries.Add(e);
                else j.Failed.Add(e);
            }
            WriteJson(Path.ChangeExtension(lpath, ".json"), j);
            return j;
        }
        catch { return null; }
    }

    public Dictionary<string, object?> Undo(string journalPath)
    {
        var j = JsonSerializer.Deserialize<Journal>(File.ReadAllText(journalPath)) ?? new Journal();
        int ok = 0, fail = 0, dirsBack = 0;
        lock (_lock) { _running = true; _finished = false; _done = 0; _total = j.Entries.Count; _failed = 0; _current = "undo"; }

        // 정리하면서 지웠던 빈 폴더를 먼저 되살린다 (없으면 파일도 제자리로 못 돌아간다)
        foreach (var d in j.RemovedDirs.OrderBy(x => x.Length))
        {
            try { if (!Directory.Exists(d)) { Directory.CreateDirectory(d); dirsBack++; } } catch (Exception ex) { Log.Error("undo mkdir", ex); }
        }

        for (int i = j.Entries.Count - 1; i >= 0; i--)
        {
            var e = j.Entries[i];
            try { if (File.Exists(e.Dst)) { Move(e.Dst, e.Src); ok++; } else fail++; }
            catch { fail++; }
            lock (_lock) { _done = j.Entries.Count - i; _failed = fail; }
        }
        // 되돌리면서 비게 된 목적지 폴더만 정리한다
        if (j.Root != null)
            RemoveEmptiedDirs(j.Entries.Select(e => Path.GetDirectoryName(e.Dst) ?? ""), j.Root);
        j.Undone = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        WriteJson(journalPath, j);
        NotifyShell(j.Root);
        lock (_lock) { _running = false; _finished = true; }
        return new() { ["restored"] = ok, ["failed"] = fail, ["dirs_restored"] = dirsBack };
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
                list.Add(new() { ["path"] = p, ["created"] = j.Created, ["root"] = j.Root, ["count"] = j.Entries.Count, ["failed"] = j.Failed.Count, ["missing"] = j.MissingFiles.Count, ["undone"] = j.Undone, ["recovered"] = j.Recovered });
            }
            catch { }
        }
        return list;
    }
}
