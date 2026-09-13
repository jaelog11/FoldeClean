using System.Diagnostics;
using System.IO;

namespace FoldeClean;

/// <summary>빠른 폴더 탐색. 정션/심볼릭 링크는 따라가지 않는다.</summary>
public static class Scanner
{
    public static ScanResult Scan(string root, Action<int>? progress = null, int maxDepth = 50)
    {
        var sw = Stopwatch.StartNew();
        root = Path.GetFullPath(root);
        var res = new ScanResult { Root = root };
        var stack = new Stack<(string dir, int depth)>();
        stack.Push((root, 0));
        var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true, RecurseSubdirectories = false };

        while (stack.Count > 0)
        {
            var (cur, depth) = stack.Pop();
            IEnumerable<FileSystemInfo> entries;
            try { entries = new DirectoryInfo(cur).EnumerateFileSystemInfos("*", opts); }
            catch { res.Errors++; continue; }

            foreach (var e in entries)
            {
                int attrs;
                try { attrs = (int)e.Attributes; } catch { res.Errors++; continue; }
                if ((attrs & FileRec.AttrReparse) != 0) { res.SkippedLinks++; continue; }
                bool isDir = (attrs & (int)FileAttributes.Directory) != 0;
                if (isDir)
                {
                    res.Dirs.Add(e.FullName);
                    // 숨김 폴더(.git 등)는 내려가지 않음. 프로젝트 감지는 Safety 에서 이름으로 본다.
                    if ((attrs & FileRec.AttrHidden) == 0 && depth < maxDepth) stack.Push((e.FullName, depth + 1));
                    continue;
                }
                var fi = (FileInfo)e;
                var name = fi.Name;
                int dot = name.LastIndexOf('.');
                var ext = dot > 0 ? name[(dot + 1)..].ToLowerInvariant() : "";
                long size; double mtime, ctime;
                try { size = fi.Length; mtime = TimeUtil.ToUnix(fi.LastWriteTime); ctime = TimeUtil.ToUnix(fi.CreationTime); }
                catch { res.Errors++; continue; }
                res.Files.Add(new FileRec { Path = fi.FullName, Name = name, Ext = ext, Size = size, Mtime = mtime, Ctime = ctime, Attrs = attrs, Depth = depth });
                if (progress != null && res.Files.Count % 2000 == 0) progress(res.Files.Count);
            }
        }
        res.Elapsed = sw.Elapsed.TotalSeconds;
        return res;
    }
}
