using System.IO;
using System.Text;
using Microsoft.Win32;

namespace FoldeClean;

/// <summary>
/// 연결 파일 감지.
///   block : 절대 옮기지 않음 (시스템 폴더, 프로그램 폴더, 레지스트리 등록, 잠김)
///   warn  : 확인 필요 (바로가기 대상, 프로젝트 내부, 클라우드 자리표시자, 실행파일, 숨김)
///   safe  : 자유롭게 정리 가능
/// </summary>
public sealed class SafetyAnalyzer
{
    static readonly HashSet<string> ExecExt = new() { "exe", "dll", "sys", "msi", "com", "bat", "cmd", "ps1", "vbs", "scr", "ocx", "drv", "cpl" };
    static readonly HashSet<string> ProjectMarkers = new(StringComparer.OrdinalIgnoreCase) {
        ".git", ".svn", ".hg", "package.json", "pyproject.toml", "requirements.txt", "Cargo.toml", "go.mod", "pom.xml",
        "build.gradle", "CMakeLists.txt", "Makefile", "Gemfile", "composer.json", ".vs", ".idea", "node_modules", "venv", ".venv" };
    static readonly string[] CloudHints = { "onedrive", "dropbox", "google drive", "googledrive", "icloud", "box sync" };

    readonly string _root;
    readonly List<string> _sysRoots;
    readonly Dictionary<string, string> _shortcuts;      // 대상경로(lower) → lnk 경로
    readonly HashSet<string> _regPaths;
    readonly bool _checkLocks;
    readonly int _lockLimit;
    readonly Dictionary<string, string?> _projectCache = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, bool> _programDirCache = new(StringComparer.OrdinalIgnoreCase);

    public int ShortcutCount => _shortcuts.Count;
    public int RegistryCount => _regPaths.Count;

    public SafetyAnalyzer(string root, bool checkLocks = true, int lockCheckLimit = 5000, int scanDepth = 50)
    {
        _root = Path.GetFullPath(root);
        _sysRoots = SystemRoots();
        _shortcuts = CollectShortcutTargets(new[] { _root }, scanDepth > 0);
        _regPaths = CollectRegistryPaths();
        _checkLocks = checkLocks;
        _lockLimit = lockCheckLimit;
    }

    // ------------------------------------------------------------ 시스템 경로
    static string Env(string n) => Environment.GetEnvironmentVariable(n) ?? "";

    static List<string> SystemRoots()
    {
        var roots = new List<string> { Env("SystemRoot"), Env("ProgramFiles"), Env("ProgramFiles(x86)"), Env("ProgramData"),
            Env("LOCALAPPDATA"), Env("APPDATA"), Env("ProgramW6432") };
        var drive = Path.GetPathRoot(Env("SystemRoot").Length > 0 ? Env("SystemRoot") : @"C:\") ?? @"C:\";
        roots.AddRange(new[] { "$Recycle.Bin", "System Volume Information", "Recovery", "PerfLogs", "Windows" }.Select(n => Path.Combine(drive, n)));
        return roots.Where(r => r.Length > 0).Select(r => Norm(Path.GetFullPath(r))).ToList();
    }

    static string Norm(string p) => p.TrimEnd('\\').ToLowerInvariant();
    static bool Under(string path, string baseNorm) { var p = Norm(path); return p == baseNorm || p.StartsWith(baseNorm + "\\"); }

    // ------------------------------------------------------------ .lnk 파싱
    public static string? ParseLnkTarget(string path)
    {
        try
        {
            byte[] data;
            using (var f = File.OpenRead(path)) { data = new byte[Math.Min(f.Length, 64 * 1024)]; f.ReadExactly(data); }
            if (data.Length < 0x4C || data[0] != 'L' || data[1] != 0 || data[2] != 0 || data[3] != 0) return null;
            uint flags = BitConverter.ToUInt32(data, 0x14);
            int off = 0x4C;
            if ((flags & 0x1) != 0) { int idSize = BitConverter.ToUInt16(data, off); off += 2 + idSize; }
            if ((flags & 0x2) != 0)
            {
                uint liFlags = BitConverter.ToUInt32(data, off + 8);
                uint localOff = BitConverter.ToUInt32(data, off + 16);
                if ((liFlags & 0x1) != 0 && localOff != 0)
                {
                    int start = off + (int)localOff;
                    int end = Array.IndexOf(data, (byte)0, start);
                    if (end < 0) return null;
                    return Encoding.Default.GetString(data, start, end - start);
                }
            }
            return null;
        }
        catch { return null; }
    }

    static IEnumerable<string> ShortcutDirs()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var c = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Path.Combine(home, "OneDrive", "Desktop"),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Path.Combine(Env("APPDATA"), "Microsoft", "Internet Explorer", "Quick Launch"),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) };
        return c.Where(d => d.Length > 0 && Directory.Exists(d));
    }

    /// <summary>
    /// 바로가기가 가리키는 파일을 모은다. 바탕화면·시작 메뉴는 늘 하위까지 보고,
    /// 정리 대상 폴더(root)는 검사 범위와 같은 깊이만 본다.
    /// 예전에는 root 도 무조건 하위 전체를 훑어, "이 폴더의 파일만" 검사인데도 오래 걸렸다.
    /// </summary>
    static Dictionary<string, string> CollectShortcutTargets(IEnumerable<string> extra, bool extraRecursive)
    {
        var map = new Dictionary<string, string>();
        var deep = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var flat = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = FileAttributes.ReparsePoint };

        void Collect(string dir, EnumerationOptions opts)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.lnk", opts); } catch { return; }
            foreach (var lp in files)
            {
                var t = ParseLnkTarget(lp);
                if (t != null) { try { map[Norm(Path.GetFullPath(t))] = lp; } catch { } }
            }
        }

        foreach (var d in ShortcutDirs()) Collect(d, deep);
        foreach (var d in extra) Collect(d, extraRecursive ? deep : flat);
        return map;
    }

    // ------------------------------------------------------------ 레지스트리
    static HashSet<string> CollectRegistryPaths()
    {
        var paths = new HashSet<string>();
        void Add(object? val)
        {
            if (val is not string s || !s.Contains(':')) return;
            var v = s.Trim().Trim('"');
            string p;
            if (v.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || File.Exists(v) || Directory.Exists(v)) p = v;
            else { int i = v.IndexOf(".exe", StringComparison.OrdinalIgnoreCase); if (i < 0) return; p = v[..(i + 4)].Trim('"'); }
            try { p = Norm(Path.GetFullPath(p)); } catch { return; }
            paths.Add(p);
            var dir = Path.GetDirectoryName(p); if (dir != null) paths.Add(Norm(dir));
        }
        var keys = new (RegistryKey hive, string sub)[] {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run") };
        foreach (var (hive, sub) in keys)
        {
            RegistryKey? k = null;
            try { k = hive.OpenSubKey(sub); } catch { }
            if (k == null) continue;
            using (k)
            {
                foreach (var vn in k.GetValueNames()) { try { Add(k.GetValue(vn)); } catch { } }
                foreach (var name in k.GetSubKeyNames())
                {
                    try
                    {
                        using var sk = k.OpenSubKey(name);
                        if (sk == null) continue;
                        foreach (var vn in new[] { "", "Path", "InstallLocation", "DisplayIcon", "UninstallString" })
                            try { Add(sk.GetValue(vn)); } catch { }
                    }
                    catch { }
                }
            }
        }
        return paths;
    }

    // ------------------------------------------------------------ 잠김
    /// <summary>읽기 전용 + 공유 금지로 열어 본다. 다른 프로세스가 열어 두었으면 실패한다. (쓰기 열기는 백신 검사를 유발해 느리다)</summary>
    static bool IsLocked(string path)
    {
        try { using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 1, FileOptions.None); return false; }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
        catch { return false; }
    }

    const int AttrOffline = 0x1000;
    public int LockChecked { get; private set; }
    public bool LockCheckTimedOut { get; private set; }

    // ------------------------------------------------------------ 프로젝트 / 프로그램 폴더
    string? ProjectRootOf(string dir)
    {
        var chain = new List<string>();
        string? res = null;
        var d = dir;
        while (true)
        {
            if (_projectCache.TryGetValue(d, out res)) break;
            chain.Add(d);
            bool marker = false;
            try
            {
                foreach (var e in Directory.EnumerateFileSystemEntries(d))
                {
                    var n = Path.GetFileName(e);
                    if (ProjectMarkers.Contains(n) || n.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) { marker = true; break; }
                }
            }
            catch { }
            if (marker) { res = d; break; }
            var parent = Path.GetDirectoryName(d);
            if (parent == null || string.Equals(d, _root, StringComparison.OrdinalIgnoreCase)) { res = null; break; }
            d = parent;
        }
        foreach (var c in chain) _projectCache[c] = res;
        return res;
    }

    bool IsProgramDir(string dir)
    {
        if (_programDirCache.TryGetValue(dir, out var r)) return r;
        bool exe = false, dll = false;
        try
        {
            foreach (var e in Directory.EnumerateFiles(dir))
            {
                var x = Path.GetExtension(e).ToLowerInvariant();
                if (x == ".exe") exe = true; else if (x == ".dll" || x == ".manifest") dll = true;
                if (exe && dll) break;
            }
        }
        catch { }
        return _programDirCache[dir] = exe && dll;
    }

    // ------------------------------------------------------------ 분석
    public Dictionary<string, object?> Analyze(List<FileRec> files, Action<int>? progress = null, Action<int, int>? lockProgress = null, int lockTimeBudgetSec = 20)
    {
        int safe = 0, warn = 0, block = 0;
        for (int i = 0; i < files.Count; i++)
        {
            var f = files[i];
            var reasons = new List<string>();
            var level = "safe";
            var p = Norm(f.Path);
            var d = Path.GetDirectoryName(f.Path) ?? "";

            string group = d;   // 기본 묶음: 상위 폴더
            if (_sysRoots.Any(r => Under(f.Path, r))) { level = "block"; reasons.Add("reason.sysdir"); }
            if (_regPaths.Contains(p) || _regPaths.Contains(Norm(d))) { level = "block"; reasons.Add("reason.registry"); }
            if (f.System) { level = "block"; reasons.Add("reason.sysattr"); }
            if (IsProgramDir(d)) { level = "block"; reasons.Add("reason.programdir"); group = d; }

            if (level != "block")
            {
                if (_shortcuts.TryGetValue(p, out var lnk)) { level = "warn"; reasons.Add("reason.shortcut|" + Path.GetFileName(lnk)); }
                if (ExecExt.Contains(f.Ext)) { level = "warn"; reasons.Add("reason.exec"); }
                if (f.Ext == "lnk") { level = "warn"; reasons.Add("reason.lnk"); }
                var pr = ProjectRootOf(d);
                if (pr != null) { level = "warn"; reasons.Add("reason.project|" + Path.GetFileName(pr)); group = pr; }
                if (f.CloudPlaceholder || CloudHints.Any(h => p.Contains(h))) { level = "warn"; reasons.Add("reason.cloud"); }
                if (f.Hidden) { level = "warn"; reasons.Add("reason.hidden"); }
            }
            f.Risk = level; f.Reasons = reasons; f.Group = group;
            if (progress != null && i % 200 == 0) progress(i);
        }

        // 잠김 검사: 병렬 + 시간 제한. 클라우드 자리표시자/오프라인 파일은 열면 내려받기가 시작되므로 건너뛴다.
        if (_checkLocks)
        {
            var targets = files.Where(f => f.Risk != "block" && f.Ext != "lnk" && !f.CloudPlaceholder && (f.Attrs & AttrOffline) == 0)
                               .Take(_lockLimit).ToList();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int done = 0; bool timedOut = false;
            var po = new ParallelOptions { MaxDegreeOfParallelism = 8 };
            Parallel.ForEach(targets, po, (f, state) =>
            {
                if (sw.Elapsed.TotalSeconds > lockTimeBudgetSec) { timedOut = true; state.Stop(); return; }
                if (IsLocked(f.Path)) lock (f) { f.Risk = "block"; f.Reasons.Add("reason.locked"); }
                int n = Interlocked.Increment(ref done);
                if (lockProgress != null && n % 200 == 0) lockProgress(n, targets.Count);
            });
            LockChecked = done; LockCheckTimedOut = timedOut;
        }

        foreach (var f in files) { if (f.Risk == "safe") safe++; else if (f.Risk == "warn") warn++; else block++; }
        return new()
        {
            ["stats"] = new Dictionary<string, int> { ["safe"] = safe, ["warn"] = warn, ["block"] = block },
            ["shortcut_count"] = ShortcutCount, ["registry_count"] = RegistryCount,
            ["lock_checked"] = LockChecked, ["lock_timed_out"] = LockCheckTimedOut,
        };
    }
}
