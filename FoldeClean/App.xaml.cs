using System.IO;
using System.Text.Json;
using System.Windows;

namespace FoldeClean;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // 처리되지 않은 오류는 로그에 남기고 사용자에게 알린다 (오류 보고서 저장 버튼이 이 로그를 담는다)
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log.Error("Unhandled", a.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, a) =>
        {
            Log.Error("Dispatcher", a.Exception);
            MessageBox.Show("예상하지 못한 오류가 났습니다. 왼쪽 아래 '오류 보고서 저장'으로 보고서를 만들어 주세요.\n\n" + a.Exception.Message, "FoldeClean", MessageBoxButton.OK, MessageBoxImage.Error);
            a.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, a) => { Log.Error("Task", a.Exception); a.SetObserved(); };
        Log.Info($"start v{Api.Version} os={Environment.OSVersion.VersionString} args={string.Join(' ', e.Args)}");

        // 자체 점검: FoldeClean.exe --selftest <폴더>  → 검사·계획·실행·되돌리기 후 결과를 selftest.json 에 기록하고 종료
        if (e.Args.Length >= 2 && e.Args[0] == "--selftest")
        {
            var code = SelfTest(e.Args[1]);
            Shutdown(code);
            return;
        }
        base.OnStartup(e);
    }

    static int SelfTest(string root)
    {
        var log = new Dictionary<string, object?>();
        try
        {
            root = Path.GetFullPath(root);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var scan = Scanner.Scan(root);
            var before = scan.Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            var tScan = sw.Elapsed.TotalSeconds; sw.Restart();
            var analyzer = new SafetyAnalyzer(root, checkLocks: true);
            var tPrepare = sw.Elapsed.TotalSeconds; sw.Restart();
            var info = analyzer.Analyze(scan.Files);
            var tAnalyze = sw.Elapsed.TotalSeconds;
            log["timing_sec"] = new Dictionary<string, double> { ["scan"] = Math.Round(tScan, 2), ["prepare"] = Math.Round(tPrepare, 2), ["analyze"] = Math.Round(tAnalyze, 2) };
            log["scan"] = scan.Summary(); log["risk"] = info;
            var plans = new Dictionary<string, object?>();
            foreach (var id in new[] { "type", "date", "type_date", "para", "johnny", "archive_old", "dedupe" })
                plans[id] = Planner.Build(scan.Files, root, Strategies.Make(id, new()), false, new(), 0).Summary;
            log["plans"] = plans;

            // 중복 예상(크기만)과 실제(내용 읽기) 비교 — 예상은 항상 실제보다 크거나 같아야 한다
            var hint = Dupes.SizeHint(scan.Files);
            var exact = Dupes.ExactCheck(scan.Files, 1024);
            log["dupe"] = new Dictionary<string, object?>
            {
                ["hint_candidates"] = hint["candidates"], ["hint_max_reclaim"] = hint["max_reclaim"],
                ["exact_duplicates"] = exact["duplicates"], ["exact_reclaim"] = exact["reclaim"],
                ["upper_bound_ok"] = Convert.ToInt64(hint["candidates"]) >= Convert.ToInt64(exact["duplicates"])
                                  && Convert.ToInt64(hint["max_reclaim"]) >= Convert.ToInt64(exact["reclaim"]),
            };

            // 규칙(Pro) 검증: 폴더 지정 + 치환자, 건너뛰기, 이름 앞에 붙이기, 조건 두 개
            var baseline = Planner.Build(scan.Files, root, Strategies.Make("type", new()), false, new(), 0);
            var testRules = new RuleSet
            {
                Rules = new()
                {
                    new Rule { Id = "a", Name = "보고서 모으기", Action = "folder", Value = "문서/보고서/{year}",
                        Conditions = new() { new RuleCondition { Field = "name", Op = "contains", Value = "보고서" } } },
                    new Rule { Id = "b", Name = "PDF 제외", Action = "skip",
                        Conditions = new() { new RuleCondition { Field = "ext", Op = "equals", Value = "pdf" } } },
                    new Rule { Id = "c", Name = "압축 표시", Action = "prefix", Value = "[Z] ",
                        Conditions = new() { new RuleCondition { Field = "ext", Op = "equals", Value = "zip" } } },
                    new Rule { Id = "d", Name = "큰 동영상", Action = "folder", Value = "큰동영상",
                        Conditions = new() { new RuleCondition { Field = "ext", Op = "equals", Value = "mp4" },
                                             new RuleCondition { Field = "size", Op = "gt", Value = "5000" } } },
                }
            };
            var ruled = Planner.Build(scan.Files, root, Strategies.Make("type", new()), false, new(), 0, null, testRules);
            var prefixed = ruled.Moves.Where(m => m.Rule == "압축 표시").Select(m => Path.GetFileName(m.Dst)).FirstOrDefault();
            log["rules"] = new Dictionary<string, object?>
            {
                ["baseline_moves"] = baseline.Moves.Count,
                ["ruled_moves"] = ruled.Moves.Count,
                ["rule_hits"] = ruled.Summary["rule_hits"],
                ["rule_skips"] = ruled.Summary["rule_skips"],
                ["pdf_before"] = baseline.Moves.Count(m => m.Ext == "pdf"),
                ["pdf_after"] = ruled.Moves.Count(m => m.Ext == "pdf"),
                ["token_ok"] = ruled.Moves.Any(m => m.Rule == "보고서 모으기" && System.Text.RegularExpressions.Regex.IsMatch(m.DstRel, @"보고서.\d{4}$")),
                ["prefix_sample"] = prefixed,
                ["prefix_keeps_space"] = prefixed?.StartsWith("[Z] ") ?? false,
                ["multi_cond_hits"] = ruled.Moves.Count(m => m.Rule == "큰 동영상"),
            };
            // 우리가 건드리지 않는 빈 폴더가 실행·되돌리기 후에도 살아남아야 한다
            var bystander = new[] { Path.Combine(root, "_그대로둘폴더"), Path.Combine(root, "_보관", "가", "나") };
            foreach (var b in bystander) Directory.CreateDirectory(b);

            var plan = Planner.Build(scan.Files, root, Strategies.Make("type_date", new()), false, new(), 0);
            var ex = new Executor();
            var jpath = ex.Run(plan.Moves, true, root);
            log["executed"] = ex.Snapshot();
            log["bystander_dirs_kept"] = bystander.Count(Directory.Exists);
            log["bystander_dirs_total"] = bystander.Length;
            // 한 줄 기록(.jsonl)만으로 복구되는지도 확인
            var recovered = Executor.RecoverFromLog(Path.ChangeExtension(jpath, ".jsonl"));
            log["jsonl_recover_entries"] = recovered?.Entries.Count;
            log["undo"] = ex.Undo(jpath);
            log["bystander_dirs_after_undo"] = bystander.Count(Directory.Exists);
            foreach (var b in bystander.OrderByDescending(x => x.Length)) { try { Directory.Delete(b); } catch { } }
            try { Directory.Delete(Path.Combine(root, "_보관")); } catch { }
            var after = Scanner.Scan(root).Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            log["restored_identical"] = before.SequenceEqual(after);
            log["ok"] = true;
        }
        catch (Exception exn) { log["ok"] = false; log["error"] = exn.ToString(); }
        File.WriteAllText(Path.Combine(root, "..", "selftest.json"), JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true, Encoder = Json.Options.Encoder }));
        return log["ok"] is true ? 0 : 1;
    }
}
