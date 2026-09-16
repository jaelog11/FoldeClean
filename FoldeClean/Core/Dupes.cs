using System.IO;
using System.IO.Hashing;

namespace FoldeClean;

/// <summary>
/// 중복 탐지: 크기 → 앞 64KB 해시 → 전체 해시. 해시는 병렬.
/// 해시는 암호화용이 아닌 xxHash128 을 쓴다 (더 빠르고, 백신의 랜섬웨어 휴리스틱에 걸리지 않는다).
/// </summary>
public static class Dupes
{
    const int Head = 64 * 1024;

    static string? Hash(string path, int? limit)
    {
        try
        {
            using var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
            var h = new XxHash128();
            var buf = new byte[1 << 16];
            long remaining = limit ?? long.MaxValue;
            while (remaining > 0)
            {
                int n = f.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
                if (n <= 0) break;
                h.Append(buf.AsSpan(0, n));
                remaining -= n;
            }
            return Convert.ToHexString(h.GetCurrentHash());
        }
        catch { return null; }
    }

    static List<List<FileRec>> Group(List<FileRec> files, int? limit, Action<long>? bytesDone)
    {
        var digests = new string?[files.Count];
        Parallel.For(0, files.Count, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            digests[i] = Hash(files[i].Path, limit);
            bytesDone?.Invoke(limit is int l ? Math.Min(l, files[i].Size) : files[i].Size);
        });
        var buckets = new Dictionary<string, List<FileRec>>();
        for (int i = 0; i < files.Count; i++)
        {
            if (digests[i] is not string d) continue;
            if (!buckets.TryGetValue(d, out var l)) buckets[d] = l = new();
            l.Add(files[i]);
        }
        return buckets.Values.Where(g => g.Count > 1).ToList();
    }

    /// <summary>
    /// 파일을 읽지 않고 크기만으로 내는 예상. 크기가 같은 파일이 하나도 없으면 중복도 있을 수 없다.
    /// 검사 직후 바로 계산할 수 있어 "중복 없음"을 즉시 알려 줄 때 쓴다.
    /// </summary>
    public static Dictionary<string, object?> SizeHint(List<FileRec> files, long minSize = 1024)
    {
        var groups = files.Where(f => f.Risk != "block" && f.Size >= minSize)
                          .GroupBy(f => f.Size).Where(g => g.Count() > 1).ToList();
        return new()
        {
            ["candidates"] = groups.Sum(g => g.Count()),          // 크기가 겹치는 파일 수
            ["groups"] = groups.Count,
            ["max_reclaim"] = groups.Sum(g => (long)(g.Count() - 1) * g.Key),   // 전부 중복이라면 줄어들 용량
        };
    }

    /// <summary>실제로 내용을 읽어 확인한다. 묶음 수, 지울 수 있는 파일 수, 확보 용량.</summary>
    public static Dictionary<string, object?> ExactCheck(List<FileRec> files, long minSize, Action<long, long>? progress = null)
    {
        var dupes = FindDuplicates(files.Where(f => f.Risk != "block" && f.Size >= minSize).ToList(), progress);
        return new()
        {
            ["groups"] = dupes.Count,
            ["duplicates"] = dupes.Sum(g => g.Count - 1),
            ["reclaim"] = dupes.Sum(g => (long)(g.Count - 1) * g[0].Size),
        };
    }

    /// <summary>progress(읽은 바이트, 최대 예상 바이트). 크기가 같은 파일만 읽으므로 대부분의 파일은 읽지 않는다.</summary>
    public static List<List<FileRec>> FindDuplicates(List<FileRec> files, Action<long, long>? progress = null)
    {
        var result = new List<List<FileRec>>();
        var groups = files.GroupBy(f => f.Size).Where(g => g.Count() > 1).Select(g => (size: g.Key, list: g.ToList())).ToList();
        long budget = groups.Sum(g => g.size * g.list.Count);
        long done = 0;
        void Add(long b) { var d = Interlocked.Add(ref done, b); progress?.Invoke(d, budget); }
        foreach (var (size, grp) in groups)
        {
            var stage1 = size > Head ? Group(grp, Head, Add) : new List<List<FileRec>> { grp };
            foreach (var g in stage1) result.AddRange(Group(g, null, Add));
            long skipped = (grp.Count - stage1.Sum(g => g.Count)) * size;
            if (skipped > 0) Add(skipped);
        }
        return result;
    }
}
