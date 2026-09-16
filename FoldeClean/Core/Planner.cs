using System.IO;

namespace FoldeClean;

public sealed class Plan
{
    public string Root = "", DestRoot = "", StrategyId = "";
    public List<MoveRec> Moves = new();
    public Dictionary<string, object?> Summary = new();
    public List<Dictionary<string, object?>> Flows = new();
    public List<Dictionary<string, object?>> DestTree = new();
    public List<Dictionary<string, object?>> SrcGroups = new();

    public Dictionary<string, object?> ToDict(int maxMoves = 5000) => new()
    {
        ["root"] = Root, ["dest_root"] = DestRoot, ["strategy"] = StrategyId,
        ["moves"] = Moves.Take(maxMoves).ToList(), ["moves_total"] = Moves.Count,
        ["summary"] = Summary, ["flows"] = Flows, ["dest_tree"] = DestTree, ["src_groups"] = SrcGroups,
    };
}

/// <summary>이동 계획: 전략 적용 → 이름 충돌 해결 → 흐름/트리 집계.</summary>
public static class Planner
{
    sealed class Agg { public int Count; public long Size; }

    static string UniqueDest(string dest, HashSet<string> taken)
    {
        if (taken.Add(dest.ToLowerInvariant()) && !File.Exists(dest)) return dest;
        var dir = Path.GetDirectoryName(dest) ?? ""; var stem = Path.GetFileNameWithoutExtension(dest); var ext = Path.GetExtension(dest);
        for (int i = 1; ; i++)
        {
            var cand = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!taken.Contains(cand.ToLowerInvariant()) && !File.Exists(cand)) { taken.Add(cand.ToLowerInvariant()); return cand; }
        }
    }

    /// <summary>
    /// 이동 계획을 세운다. rules 가 주어지면(Pro) 전략보다 먼저 평가하고, 처음 맞는 규칙 하나만 적용한다.
    /// 규칙 동작: folder(목적지 지정) · skip(옮기지 않음) · prefix/suffix(이름 앞뒤에 붙이고 전략 폴더는 그대로)
    /// </summary>
    public static Plan Build(List<FileRec> files, string root, Strategy strategy, bool includeWarn, HashSet<string> excludeExts, long minSize,
                             Action<string, long, long>? progress = null, RuleSet? rules = null)
    {
        root = Path.GetFullPath(root);
        var plan = new Plan { Root = root, DestRoot = root, StrategyId = strategy.Id };
        var candidates = files.Where(f => (f.Risk == "safe" || (includeWarn && f.Risk == "warn")) && !excludeExts.Contains(f.Ext) && f.Size >= minSize).ToList();
        strategy.Prepare(candidates, root, progress);
        progress?.Invoke("plan", 0, candidates.Count);
        int processed = 0;

        var taken = new HashSet<string>();
        int skippedSame = 0, ruleHits = 0, ruleSkips = 0;
        var flows = new Dictionary<(string, string), Agg>();
        var destDirs = new Dictionary<string, Agg>();
        var srcDirs = new Dictionary<string, Agg>();

        foreach (var f in candidates)
        {
            if (++processed % 500 == 0) progress?.Invoke("plan", processed, candidates.Count);

            // 1) 규칙 (Pro) — 처음 맞는 하나만
            string? rel = null, ruleName = null, fileName = f.Name;
            var matched = rules?.FirstMatch(f, root);
            if (matched != null)
            {
                ruleName = string.IsNullOrWhiteSpace(matched.Name) ? matched.Id : matched.Name;
                switch (matched.Action)
                {
                    case "skip":
                        ruleSkips++; continue;
                    case "folder":
                        var expanded = RuleSet.Expand(matched.Value, f).Trim();   // 폴더 이름에서만 공백 정리
                        if (expanded.Length > 0) rel = expanded;
                        break;
                    case "prefix":
                        fileName = RuleSet.Expand(matched.Value, f) + f.Name; break;
                    case "suffix":
                        var stem = Path.GetFileNameWithoutExtension(f.Name);
                        fileName = stem + RuleSet.Expand(matched.Value, f) + Path.GetExtension(f.Name); break;
                }
                ruleHits++;
            }

            // 2) 규칙이 목적지를 정하지 않았으면 전략에 맡긴다
            rel ??= strategy.DestDir(f, root);
            if (rel == null) continue;
            rel = rel.TrimEnd(Path.DirectorySeparatorChar);
            var targetDir = Path.GetFullPath(Path.Combine(root, rel));
            var srcDir = Path.GetDirectoryName(f.Path) ?? "";
            if (string.Equals(targetDir, srcDir, StringComparison.OrdinalIgnoreCase) && fileName == f.Name) { skippedSame++; continue; }
            var dest = UniqueDest(Path.Combine(targetDir, fileName), taken);
            var srcRel = Path.GetRelativePath(root, srcDir);
            srcRel = srcRel == "." ? I18n.Name("root") : srcRel.Split(Path.DirectorySeparatorChar)[0];
            var topDest = rel.Split(Path.DirectorySeparatorChar)[0];
            plan.Moves.Add(new MoveRec
            {
                Src = f.Path, Dst = dest, Name = f.Name, Size = f.Size, Ext = f.Ext, Risk = f.Risk, Reasons = f.Reasons,
                SrcGroup = srcRel, DstGroup = topDest, DstRel = rel, Category = Categories.Of(f).label,
                Renamed = !string.Equals(Path.GetFileName(dest), f.Name, StringComparison.Ordinal),
                Note = strategy.Note(f), Rule = ruleName,
            });
            Bump(flows, (srcRel, topDest), f.Size); Bump(destDirs, rel, f.Size); Bump(srcDirs, srcRel, f.Size);
        }

        plan.Summary = new()
        {
            ["move_count"] = plan.Moves.Count, ["total_size"] = plan.Moves.Sum(m => m.Size),
            ["warn_count"] = plan.Moves.Count(m => m.Risk == "warn"), ["renamed"] = plan.Moves.Count(m => m.Renamed),
            ["skipped_same"] = skippedSame, ["candidates"] = candidates.Count,
            ["rule_hits"] = ruleHits, ["rule_skips"] = ruleSkips,
        };
        plan.Flows = flows.OrderByDescending(kv => kv.Value.Size).Select(kv => new Dictionary<string, object?> { ["src"] = kv.Key.Item1, ["dst"] = kv.Key.Item2, ["count"] = kv.Value.Count, ["size"] = kv.Value.Size }).ToList();
        plan.SrcGroups = srcDirs.OrderByDescending(kv => kv.Value.Size).Select(kv => new Dictionary<string, object?> { ["name"] = kv.Key, ["count"] = kv.Value.Count, ["size"] = kv.Value.Size }).ToList();
        plan.DestTree = Tree(destDirs);
        return plan;
    }

    /// <summary>
    /// 흐름도를 한 단계 안으로 들어가서 본다. dest 가 빈 문자열이면 최상위.
    /// 오른쪽 칸은 dest 바로 아래 한 단계만 보여주고, 더 들어갈 수 있는지(has_children)도 알려 준다.
    /// dest 폴더에 바로 들어가는 파일은 이름이 "" 인 항목으로 모은다.
    /// </summary>
    public static Dictionary<string, object?> Flows(List<MoveRec> moves, string dest)
    {
        var sep = Path.DirectorySeparatorChar;
        dest = (dest ?? "").Trim().Replace('/', sep).Trim(sep);   // 화면은 / 를 쓸 수 있다
        var prefix = dest.Length == 0 ? "" : dest + sep;
        var scoped = dest.Length == 0 ? moves
            : moves.Where(m => m.DstRel.Equals(dest, StringComparison.OrdinalIgnoreCase)
                            || m.DstRel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

        // dest 아래로 남은 경로를 조각으로 나눈다
        string[] Rest(MoveRec m)
        {
            var rest = dest.Length == 0 ? m.DstRel
                     : (m.DstRel.Length > prefix.Length ? m.DstRel[prefix.Length..] : "");
            return rest.Length == 0 ? Array.Empty<string>() : rest.Split(sep);
        }

        var flows = new Dictionary<(string, string), Agg>();
        var srcDirs = new Dictionary<string, Agg>();
        var nodes = new Dictionary<string, Agg>();
        var deeper = new HashSet<string>();     // 더 들어갈 수 있는 노드

        foreach (var m in scoped)
        {
            var parts = Rest(m);
            var seg = parts.Length == 0 ? "" : parts[0];
            if (parts.Length > 1) deeper.Add(seg);
            Bump(flows, (m.SrcGroup, seg), m.Size);
            Bump(srcDirs, m.SrcGroup, m.Size);
            Bump(nodes, seg, m.Size);
        }

        return new()
        {
            ["path"] = dest,
            ["crumbs"] = dest.Length == 0 ? new List<string>() : dest.Split(sep).ToList(),
            ["move_count"] = scoped.Count,
            ["total_size"] = scoped.Sum(m => m.Size),
            ["flows"] = flows.OrderByDescending(kv => kv.Value.Size)
                .Select(kv => new Dictionary<string, object?> { ["src"] = kv.Key.Item1, ["dst"] = kv.Key.Item2, ["count"] = kv.Value.Count, ["size"] = kv.Value.Size }).ToList(),
            ["src_groups"] = srcDirs.OrderByDescending(kv => kv.Value.Size)
                .Select(kv => new Dictionary<string, object?> { ["name"] = kv.Key, ["count"] = kv.Value.Count, ["size"] = kv.Value.Size }).ToList(),
            ["dest_nodes"] = nodes.OrderByDescending(kv => kv.Value.Size)
                .Select(kv => new Dictionary<string, object?> { ["name"] = kv.Key, ["count"] = kv.Value.Count, ["size"] = kv.Value.Size, ["has_children"] = deeper.Contains(kv.Key) }).ToList(),
        };
    }

    static void Bump<K>(Dictionary<K, Agg> d, K key, long size) where K : notnull
    {
        if (!d.TryGetValue(key, out var a)) d[key] = a = new Agg();
        a.Count++; a.Size += size;
    }

    sealed class Node { public int Count; public long Size; public Dictionary<string, Node> Children = new(); }

    static List<Dictionary<string, object?>> Tree(Dictionary<string, Agg> destDirs)
    {
        var root = new Dictionary<string, Node>();
        foreach (var (rel, v) in destDirs)
        {
            var level = root;
            foreach (var part in rel.Split(Path.DirectorySeparatorChar))
            {
                if (!level.TryGetValue(part, out var n)) level[part] = n = new Node();
                n.Count += v.Count; n.Size += v.Size;
                level = n.Children;
            }
        }
        List<Dictionary<string, object?>> Conv(Dictionary<string, Node> d) =>
            d.OrderByDescending(kv => kv.Value.Size).Select(kv => new Dictionary<string, object?>
            { ["name"] = kv.Key, ["count"] = kv.Value.Count, ["size"] = kv.Value.Size, ["children"] = Conv(kv.Value.Children) }).ToList();
        return Conv(root);
    }
}
