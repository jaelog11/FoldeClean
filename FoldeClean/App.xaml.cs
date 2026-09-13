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
            var plan = Planner.Build(scan.Files, root, Strategies.Make("type_date", new()), false, new(), 0);
            var ex = new Executor();
            var jpath = ex.Run(plan.Moves, true, root);
            log["executed"] = ex.Snapshot();
            // 한 줄 기록(.jsonl)만으로 복구되는지도 확인
            var recovered = Executor.RecoverFromLog(Path.ChangeExtension(jpath, ".jsonl"));
            log["jsonl_recover_entries"] = recovered?.Entries.Count;
            log["undo"] = ex.Undo(jpath);
            var after = Scanner.Scan(root).Files.Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
            log["restored_identical"] = before.SequenceEqual(after);
            log["ok"] = true;
        }
        catch (Exception exn) { log["ok"] = false; log["error"] = exn.ToString(); }
        File.WriteAllText(Path.Combine(root, "..", "selftest.json"), JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true, Encoder = Json.Options.Encoder }));
        return log["ok"] is true ? 0 : 1;
    }
}
