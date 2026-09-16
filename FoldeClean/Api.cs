using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace FoldeClean;

/// <summary>JS 에서 이름으로 호출되는 API. 반환값은 JSON 으로 직렬화된다.</summary>
public sealed class Api
{
    ScanResult? _scan;
    Plan? _plan;
    readonly Executor _executor = new();
    volatile string _phase = "idle"; long _count, _total;
    public Func<string?>? PickFolder;

    static T Arg<T>(JsonElement args, int i, T def)
    {
        if (args.ValueKind != JsonValueKind.Array || i >= args.GetArrayLength()) return def;
        var a = args[i];
        if (a.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return def;
        try { return a.Deserialize<T>() ?? def; } catch { return def; }
    }

    public object? Call(string name, JsonElement args) => name switch
    {
        "app_info" => AppInfo(),
        "set_lang" => SetLang(Arg(args, 0, "ko")),
        "save_report" => SaveReport(Arg(args, 0, "")),
        "edition_info" => Edition.Info(),
        "start_trial" => StartTrial(),
        "set_license" => SetLicense(Arg(args, 0, "")),
        "rules_get" => RulesGet(),
        "rules_save" => RulesSave(Arg(args, 0, new List<Rule>())),
        "rules_test" => RulesTest(Arg(args, 0, new Rule())),
        "dedupe_check" => DedupeCheck(Arg(args, 0, 1024L)),
        "get_progress" => new Dictionary<string, object?> { ["phase"] = _phase, ["count"] = Interlocked.Read(ref _count), ["total"] = Interlocked.Read(ref _total), ["exec"] = _executor.Snapshot() },
        "pick_folder" => PickFolder?.Invoke(),
        "default_folders" => DefaultFolders(),
        "scan_folder" => ScanFolder(Arg(args, 0, ""), Arg(args, 1, true), Arg(args, 2, 0)),
        "strategies" => Strategies.Ids.Select(id => new { id }).ToList(),
        "build_plan" => BuildPlan(Arg(args, 0, "type"), Arg(args, 1, new Dictionary<string, JsonElement>()), Arg(args, 2, false), Arg(args, 4, new List<string>()), Arg(args, 5, 0L)),
        "plan_moves" => PlanMoves(Arg(args, 0, 0), Arg(args, 1, 500), Arg(args, 2, ""), Arg(args, 3, "")),
        "plan_flows" => PlanFlows(Arg(args, 0, "")),
        "exclude_moves" => ExcludeMoves(Arg(args, 0, new List<string>())),
        "execute" => Execute(Arg(args, 0, true)),
        "cancel" => Cancel(),
        "journals" => Executor.ListJournals(),
        "undo" => Undo(Arg(args, 0, "")),
        "open_path" => OpenPath(Arg(args, 0, "")),
        "reveal_path" => RevealPath(Arg(args, 0, "")),
        _ => new Dictionary<string, object?> { ["error"] = $"알 수 없는 요청: {name}" },
    };

    static Dictionary<string, object?> Err(string m) => new() { ["error"] = m };

    public static string Version
    {
        get
        {
            var v = typeof(Api).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
                ?? typeof(Api).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            int plus = v.IndexOf('+'); return plus > 0 ? v[..plus] : v;   // 빌드 해시 제거
        }
    }

    public const string Owner = "Jaelog By 아지랑이";

    static object AppInfo() => new Dictionary<string, object?>
    {
        ["name"] = "FoldeClean", ["version"] = Version, ["owner"] = Owner, ["engine"] = "C# / .NET " + Environment.Version.ToString(2),
        ["journals"] = Paths.Journals, ["lang"] = I18n.Lang,
    };

    static object SetLang(string lang) { I18n.Set(lang); return I18n.Lang; }

    /// <summary>중복을 실제로 확인한다. 크기가 겹치는 파일이 없으면 읽지 않고 바로 0을 돌려준다.</summary>
    object DedupeCheck(long minSize)
    {
        if (_scan == null) return Err("먼저 폴더를 검사하세요.");
        var hint = Dupes.SizeHint(_scan.Files, minSize);
        if ((int)(hint["candidates"] ?? 0) == 0)
            return new Dictionary<string, object?> { ["groups"] = 0, ["duplicates"] = 0, ["reclaim"] = 0L, ["instant"] = true };
        _phase = "hash"; _count = 0; _total = 0;
        try
        {
            var r = Dupes.ExactCheck(_scan.Files, minSize,
                (d, t) => { Interlocked.Exchange(ref _count, d); Interlocked.Exchange(ref _total, t); });
            r["instant"] = false;
            return r;
        }
        finally { _phase = "idle"; }
    }

    // ---------------------------------------------------------------- 판 구분과 규칙 (Pro)
    static object StartTrial()
    {
        if (!Edition.StartTrial()) return Err("체험을 이미 사용했거나 Pro 상태입니다.");
        return Edition.Info();
    }

    static object SetLicense(string key) { Edition.SetLicense(key); return Edition.Info(); }

    object RulesGet() => new Dictionary<string, object?>
    {
        ["rules"] = RuleSet.Load().Rules,
        ["pro_active"] = Edition.ProActive,
        ["scanned"] = _scan?.Files.Count ?? 0,
    };

    object RulesSave(List<Rule> rules)
    {
        if (!Edition.ProActive) return Err("규칙은 Pro 기능입니다.");
        var set = new RuleSet { Rules = rules ?? new List<Rule>() };
        foreach (var r in set.Rules)
            if (string.IsNullOrWhiteSpace(r.Id)) r.Id = Guid.NewGuid().ToString("N")[..8];
        try { set.Save(); } catch (Exception ex) { Log.Error("rules save", ex); return Err(ex.Message); }
        return new Dictionary<string, object?> { ["saved"] = set.Rules.Count };
    }

    /// <summary>편집 중인 규칙을 마지막 검사 결과에 적용해 몇 개가 걸리는지 보여준다.</summary>
    object RulesTest(Rule rule)
    {
        if (_scan == null) return Err("먼저 폴더를 검사하세요.");
        if (rule == null) return Err("규칙이 비어 있습니다.");
        var candidates = _scan.Files.Where(f => f.Risk != "block").ToList();
        return RuleSet.Test(rule, candidates, _scan.Root);
    }

    /// <summary>오류 보고서를 바탕화면에 저장한다. 파일 이름·경로 같은 개인 정보는 담지 않는다 (검사 폴더 경로만).</summary>
    object SaveReport(string userNote)
    {
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("FoldeClean error report").AppendLine("=======================");
            sb.AppendLine($"time      : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"version   : {Version}  ({Owner})");
            sb.AppendLine($"os        : {Environment.OSVersion.VersionString}  64bit={Environment.Is64BitOperatingSystem}  .NET {Environment.Version}");
            sb.AppendLine($"lang      : {I18n.Lang}");
            sb.AppendLine($"user note : {userNote}");
            sb.AppendLine();
            sb.AppendLine("[last scan]");
            if (_scan != null)
            {
                var stats = _scan.Files.GroupBy(f => f.Risk).ToDictionary(g => g.Key, g => g.Count());
                sb.AppendLine($"root={_scan.Root} files={_scan.Files.Count} dirs={_scan.Dirs.Count} skippedLinks={_scan.SkippedLinks} errors={_scan.Errors} elapsed={_scan.Elapsed:F2}s");
                sb.AppendLine($"risk: " + string.Join(", ", stats.Select(kv => $"{kv.Key}={kv.Value}")));
            }
            else sb.AppendLine("(none)");
            sb.AppendLine();
            sb.AppendLine("[last plan]");
            sb.AppendLine(_plan == null ? "(none)" : $"strategy={_plan.StrategyId} moves={_plan.Moves.Count} " + string.Join(", ", _plan.Summary.Select(kv => $"{kv.Key}={kv.Value}")));
            sb.AppendLine();
            sb.AppendLine("[executor]");
            sb.AppendLine(string.Join(", ", _executor.Snapshot().Select(kv => $"{kv.Key}={kv.Value}")));
            sb.AppendLine();
            sb.AppendLine("[journals]");
            foreach (var j in Executor.ListJournals().Take(5))
                sb.AppendLine(string.Join(", ", j.Select(kv => $"{kv.Key}={kv.Value}")));
            sb.AppendLine();
            sb.AppendLine("[log tail]");
            sb.AppendLine(Log.Tail());

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var path = Path.Combine(desktop, $"FoldeClean-report-{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(true));
            RevealPath(path);
            return new Dictionary<string, object?> { ["path"] = path };
        }
        catch (Exception ex) { Log.Error("save_report", ex); return Err(ex.Message); }
    }

    object DefaultFolders()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var c = new (string key, string label, string path)[] {
            ("downloads", "다운로드", Path.Combine(home, "Downloads")), ("desktop", "바탕화면", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
            ("documents", "문서", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), ("pictures", "사진", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)) };
        return c.Where(x => Directory.Exists(x.path)).Select(x => new { key = x.key, label = x.label, path = x.path }).ToList();
    }

    /// <summary>depth: 0 = 이 폴더의 파일만, 2 = 하위 2단계까지, 50 = 하위 폴더 전부.</summary>
    object ScanFolder(string root, bool checkLocks, int depth)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return Err("폴더를 찾을 수 없습니다.");
        depth = Math.Clamp(depth, 0, 50);
        _phase = "scan"; _count = 0; _total = 0;
        var res = Scanner.Scan(root, c => Interlocked.Exchange(ref _count, c), depth);
        _phase = "prepare"; _count = 0; _total = res.Files.Count;      // 바로가기·레지스트리 수집
        var analyzer = new SafetyAnalyzer(root, checkLocks);
        _phase = "analyze";
        var info = analyzer.Analyze(res.Files, i => Interlocked.Exchange(ref _count, i), (n, t) => { _phase = "locks"; Interlocked.Exchange(ref _count, n); Interlocked.Exchange(ref _total, t); });
        _scan = res; _plan = null; _phase = "idle";
        var flagged = res.Files.Where(f => f.Risk != "safe").OrderBy(f => f.Risk != "block").ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList();
        // 이 폴더의 파일만 검사했는데 하위 폴더가 있으면, 몇 개를 건너뛰었는지 알려준다
        int skippedDirs = depth == 0 ? res.Dirs.Count : 0;
        return new Dictionary<string, object?>
        {
            ["summary"] = res.Summary(), ["risk"] = info, ["categories"] = Categories.Breakdown(res.Files),
            ["flagged"] = flagged.Take(2000).Select(f => f.ToDict()).ToList(), ["flagged_total"] = flagged.Count,
            ["depth"] = depth, ["skipped_dirs"] = skippedDirs,
            ["dupe_hint"] = Dupes.SizeHint(res.Files),   // 파일을 읽지 않는 예상. "중복 없음"을 즉시 알려 준다
        };
    }

    object BuildPlan(string strategyId, Dictionary<string, JsonElement> opts, bool includeWarn, List<string> excludeExts, long minSize)
    {
        if (_scan == null) return Err("먼저 폴더를 검사하세요.");
        _phase = "plan"; _count = 0; _total = 0;
        try
        {
            var strat = Strategies.Make(strategyId, opts);
            var rules = Edition.ProActive ? RuleSet.Load() : null;   // 규칙은 Pro 에서만 적용
            if (rules != null && rules.Rules.Count == 0) rules = null;
            _plan = Planner.Build(_scan.Files, _scan.Root, strat, includeWarn, excludeExts.Select(e => e.ToLowerInvariant().TrimStart('.')).ToHashSet(), minSize,
                (phase, c, t) => { _phase = phase; Interlocked.Exchange(ref _count, c); Interlocked.Exchange(ref _total, t); }, rules);
            return _plan.ToDict(0);   // 이동 목록은 plan_moves 로 페이지 단위로 가져간다
        }
        catch (Exception ex) { return Err(ex.Message); }
        finally { _phase = "idle"; }
    }

    /// <summary>dest 가 주어지면 그 폴더(하위 포함)로 가는 파일만 추린다. 폴더 구조를 눌렀을 때 쓴다.</summary>
    object PlanMoves(int offset, int limit, string query, string dest)
    {
        if (_plan == null) return new Dictionary<string, object?> { ["items"] = new List<MoveRec>(), ["total"] = 0 };
        IEnumerable<MoveRec> moves = _plan.Moves;
        if (!string.IsNullOrEmpty(dest))
        {
            var d = dest.Trim().Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar);
            var prefix = d + Path.DirectorySeparatorChar;
            moves = moves.Where(m => m.DstRel.Equals(d, StringComparison.OrdinalIgnoreCase)
                                  || m.DstRel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrEmpty(query)) { var q = query.ToLowerInvariant(); moves = moves.Where(m => m.Name.ToLowerInvariant().Contains(q) || m.DstRel.ToLowerInvariant().Contains(q)); }
        var list = moves.ToList();
        return new Dictionary<string, object?> { ["items"] = list.Skip(offset).Take(limit).ToList(), ["total"] = list.Count };
    }

    object PlanFlows(string dest)
    {
        if (_plan == null) return Err("먼저 미리보기를 만드세요.");
        return Planner.Flows(_plan.Moves, dest);
    }

    object ExcludeMoves(List<string> paths)
    {
        if (_plan == null) return 0;
        var s = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        int before = _plan.Moves.Count;
        _plan.Moves = _plan.Moves.Where(m => !s.Contains(m.Src)).ToList();
        return before - _plan.Moves.Count;
    }

    object Execute(bool removeEmptyDirs)
    {
        if (_plan == null || _plan.Moves.Count == 0) return Err("실행할 계획이 없습니다.");
        if (_executor.Running) return Err("이미 실행 중입니다.");
        var moves = _plan.Moves.ToList(); var root = _plan.Root;
        Task.Run(() => _executor.Run(moves, removeEmptyDirs, root));
        return new Dictionary<string, object?> { ["started"] = true, ["total"] = moves.Count };
    }

    object Cancel() { _executor.CancelRequested = true; return true; }

    object Undo(string journalPath)
    {
        if (_executor.Running) return Err("실행 중에는 되돌릴 수 없습니다.");
        if (!File.Exists(journalPath)) return Err("기록 파일을 찾을 수 없습니다.");
        Task.Run(() => _executor.Undo(journalPath));
        return new Dictionary<string, object?> { ["started"] = true };
    }

    /// <summary>탐색기에서 해당 파일을 선택한 상태로 폴더를 연다.</summary>
    static object RevealPath(string path)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); return true; } catch { return false; }
    }

    static object OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return true; } catch { return false; }
    }
}
