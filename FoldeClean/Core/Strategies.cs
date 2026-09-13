using System.IO;
using System.Text.Json;

namespace FoldeClean;

/// <summary>정리 방식. 파일 하나를 받아 목적지 상대 폴더를 돌려준다. null 이면 이동하지 않음.</summary>
public abstract class Strategy
{
    public abstract string Id { get; }
    protected readonly Dictionary<string, JsonElement> Opts;
    protected readonly bool KeepSubdirs;

    protected Strategy(Dictionary<string, JsonElement> opts)
    {
        Opts = opts;
        KeepSubdirs = Bool("keep_subdirs", false);
    }

    protected bool Bool(string k, bool def) => Opts.TryGetValue(k, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : def;
    protected double Num(string k, double def) => Opts.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
    protected string Str(string k, string def) => Opts.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? def : def;

    /// <summary>계획 전에 한 번 호출. progress(phase, count, total) 로 진행을 알릴 수 있다.</summary>
    public virtual void Prepare(List<FileRec> files, string root, Action<string, long, long>? progress = null) { }
    public abstract string? DestDir(FileRec f, string root);
    /// <summary>이동 목록에 함께 보여줄 부가 설명 (예: 중복의 원본 경로).</summary>
    public virtual string? Note(FileRec f) => null;

    protected string Sub(FileRec f, string root)
    {
        if (!KeepSubdirs) return "";
        var rel = Path.GetRelativePath(root, Path.GetDirectoryName(f.Path) ?? root);
        return rel == "." ? "" : rel;
    }

    protected static string Ym(double ts, string fmt) => TimeUtil.FromUnix(ts).ToString(fmt);
    protected static double AgeDays(FileRec f) => (TimeUtil.NowUnix - f.Mtime) / 86400.0;
}

public static class Categories
{
    // label 은 사용하지 않고 I18n.Name(key) 로 현재 언어의 폴더 이름을 만든다
    public static readonly (string key, string label, string[] exts)[] All =
    {
        ("documents", "문서", new[] { "doc","docx","hwp","hwpx","pdf","txt","rtf","odt","md","ppt","pptx","xls","xlsx","csv","odp","ods","pages","key","numbers","epub" }),
        ("images", "이미지", new[] { "jpg","jpeg","png","gif","bmp","webp","heic","heif","tif","tiff","svg","raw","cr2","nef","arw","psd","ai" }),
        ("videos", "동영상", new[] { "mp4","mkv","avi","mov","wmv","flv","webm","m4v","mpg","mpeg","ts","3gp" }),
        ("audio", "음악", new[] { "mp3","wav","flac","aac","m4a","ogg","wma","opus","mid","midi" }),
        ("archives", "압축", new[] { "zip","rar","7z","tar","gz","bz2","xz","iso","cab","alz","egg" }),
        ("installers", "설치파일", new[] { "exe","msi","msix","appx","apk","dmg","pkg","deb","rpm" }),
        ("code", "코드", new[] { "py","js","ts","tsx","jsx","html","css","scss","json","xml","yaml","yml","toml","ini","cfg","c","cpp","h","hpp","cs","java","kt","go","rs","rb","php","sh","bat","ps1","sql","ipynb" }),
        ("fonts", "폰트", new[] { "ttf","otf","woff","woff2","eot" }),
        ("shortcuts", "바로가기", new[] { "lnk","url" }),
    };
    static readonly Dictionary<string, (string key, string label)> ExtMap = All.SelectMany(c => c.exts.Select(e => (e, c.key, c.label)))
        .GroupBy(t => t.e).ToDictionary(g => g.Key, g => (g.First().key, g.First().label));   // 같은 확장자는 먼저 나온 분류 우선
    public static readonly string[] JohnnyOrder = { "documents", "images", "videos", "audio", "archives", "installers", "code", "fonts", "other" };

    public static (string key, string label) Of(FileRec f)
    {
        var key = ExtMap.TryGetValue(f.Ext, out var c) ? c.key : "other";
        return (key, I18n.Name(key));
    }

    public static List<Dictionary<string, object?>> Breakdown(List<FileRec> files) =>
        files.GroupBy(f => Of(f).key)
             .Select(g => new Dictionary<string, object?> { ["key"] = g.Key, ["label"] = I18n.Name(g.Key), ["count"] = g.Count(), ["size"] = g.Sum(x => x.Size) })
             .OrderByDescending(d => (long)d["size"]!).ToList();
}

public sealed class ByType : Strategy
{
    public ByType(Dictionary<string, JsonElement> o) : base(o) { }
    public override string Id => "type";
    public override string? DestDir(FileRec f, string root) => Path.Combine(Categories.Of(f).label, Sub(f, root));
}

public sealed class ByDate : Strategy
{
    public ByDate(Dictionary<string, JsonElement> o) : base(o) { }
    public override string Id => "date";
    public override string? DestDir(FileRec f, string root) => Path.Combine(Ym(f.Mtime, Str("granularity", "month") == "month" ? "yyyy/MM" : "yyyy"), Sub(f, root));
}

public sealed class ByTypeDate : Strategy
{
    public ByTypeDate(Dictionary<string, JsonElement> o) : base(o) { }
    public override string Id => "type_date";
    public override string? DestDir(FileRec f, string root) => Path.Combine(Categories.Of(f).label, Ym(f.Mtime, "yyyy"), Sub(f, root));
}

public sealed class Para : Strategy
{
    public Para(Dictionary<string, JsonElement> o) : base(o) { }
    public override string Id => "para";
    public override string? DestDir(FileRec f, string root)
    {
        var age = AgeDays(f);
        if (age <= Num("active_days", 30)) return Path.Combine("1_Projects", Sub(f, root));
        if (age <= Num("resource_days", 180)) return Path.Combine("3_Resources", Categories.Of(f).label, Sub(f, root));
        return Path.Combine("4_Archives", Ym(f.Mtime, "yyyy"), Sub(f, root));
    }
}

public sealed class Johnny : Strategy
{
    public Johnny(Dictionary<string, JsonElement> o) : base(o) { }
    public override string Id => "johnny";
    public override string? DestDir(FileRec f, string root)
    {
        var (key, label) = Categories.Of(f);
        if (!Categories.JohnnyOrder.Contains(key)) { key = "other"; label = I18n.Name("other"); }
        int idx = Array.IndexOf(Categories.JohnnyOrder, key) + 1;
        return Path.Combine($"{idx}0-{idx}9 {label}", $"{idx}1 {label}", Sub(f, root));
    }
}

public sealed class ArchiveOld : Strategy
{
    public ArchiveOld(Dictionary<string, JsonElement> o) : base(o) { }
    public override string Id => "archive_old";
    public override string? DestDir(FileRec f, string root) =>
        AgeDays(f) < Num("days", 180) ? null : Path.Combine("Archive", Ym(f.Mtime, "yyyy"), Sub(f, root));
}

public sealed class Dedupe : Strategy
{
    readonly Dictionary<string, string> _orig = new(StringComparer.OrdinalIgnoreCase);   // 중복 → 남길 원본
    public Dedupe(Dictionary<string, JsonElement> o) : base(o) { }
    public override string Id => "dedupe";
    public override void Prepare(List<FileRec> files, string root, Action<string, long, long>? progress = null)
    {
        long min = (long)Num("min_size", 1024);
        foreach (var grp in Dupes.FindDuplicates(files.Where(f => f.Size >= min).ToList(), (d, t) => progress?.Invoke("hash", d, t)))
        {
            var ordered = grp.OrderBy(x => x.Ctime).ThenBy(x => x.Path.Length).ToList();   // 가장 먼저 만든 파일을 원본으로
            foreach (var d in ordered.Skip(1)) _orig[d.Path] = ordered[0].Path;
        }
    }
    public override string? DestDir(FileRec f, string root) => _orig.ContainsKey(f.Path) ? Path.Combine(I18n.Name("dupes"), Sub(f, root)) : null;
    public override string? Note(FileRec f) => _orig.TryGetValue(f.Path, out var o) ? "dup|" + o : null;   // 화면에서 번역
}

public static class Strategies
{
    public static Strategy Make(string id, Dictionary<string, JsonElement> opts) => id switch
    {
        "type" => new ByType(opts), "date" => new ByDate(opts), "type_date" => new ByTypeDate(opts), "para" => new Para(opts),
        "johnny" => new Johnny(opts), "archive_old" => new ArchiveOld(opts), "dedupe" => new Dedupe(opts),
        _ => throw new ArgumentException($"알 수 없는 정리 방식: {id}"),
    };

    public static readonly string[] Ids = { "type", "date", "type_date", "para", "johnny", "archive_old", "dedupe" };

    // 아래 한국어 설명은 참고용. 실제 화면 문구는 ui/i18n.js 의 strat.* 키를 쓴다.
    public static readonly object[] Meta =
    {
        new { id = "type", name = "종류별 정리", tag = "기본", desc = "문서·이미지·동영상·음악·압축·설치파일·코드로 나눕니다. 가장 직관적이고, 파일 이름을 몰라도 찾기 쉽습니다.", best = "다운로드 폴더, 바탕화면처럼 잡다한 파일이 섞인 곳", example = "문서/보고서.pdf · 이미지/사진.jpg" },
        new { id = "date", name = "날짜별 정리", tag = "기록", desc = "수정한 연도/월 폴더로 넣습니다. 사진이나 회의록처럼 '언제'가 중요한 자료에 맞습니다.", best = "사진, 스캔본, 기간별 기록물", example = "2025/03/사진.jpg" },
        new { id = "type_date", name = "종류 → 연도", tag = "혼합", desc = "먼저 종류로 나누고 그 안을 연도로 다시 나눕니다. 파일이 아주 많을 때 한 폴더가 비대해지는 걸 막습니다.", best = "수천 개 이상의 파일, 장기 보관용 자료", example = "문서/2024/계약서.pdf" },
        new { id = "para", name = "PARA", tag = "생산성", desc = "Tiago Forte의 PARA 체계입니다. 최근에 만진 파일은 '1_Projects', 가끔 쓰는 자료는 '3_Resources', 오래된 것은 '4_Archives'로 보냅니다. 이 프로그램은 사용 시점으로 근사하고, 폴더 이름은 그대로 두어 나중에 손으로 다듬기 쉽습니다.", best = "업무 자료, 지식 관리를 시작하려는 사람", example = "1_Projects/기획안.docx · 4_Archives/2022/옛자료.xlsx" },
        new { id = "johnny", name = "Johnny.Decimal", tag = "번호체계", desc = "10-19 문서, 20-29 이미지처럼 번호를 붙여 카테고리 수를 10개 이하로 제한합니다. 폴더가 항상 같은 순서로 정렬되고 어디에 넣을지 고민이 줄어듭니다.", best = "체계적인 걸 좋아하는 사람, 여러 PC에서 같은 구조를 쓰고 싶은 경우", example = "10-19 문서/11 문서/보고서.pdf" },
        new { id = "archive_old", name = "오래된 파일 보관", tag = "최소변경", desc = "지정한 기간(기본 180일) 동안 손대지 않은 파일만 'Archive/연도'로 옮깁니다. 최근 파일은 그대로 두므로 작업 흐름이 끊기지 않습니다.", best = "지금 구조는 유지하면서 묵은 파일만 치우고 싶을 때", example = "Archive/2023/오래된자료.pdf" },
        new { id = "dedupe", name = "중복 파일 정리", tag = "용량확보", desc = "내용이 완전히 같은 파일을 찾아 하나만 남기고 나머지를 '_중복' 폴더로 모읍니다. 크기 → 앞부분 해시 → 전체 해시 순으로 비교해 큰 폴더에서도 빠릅니다.", best = "여러 번 다운로드한 파일, 복사본이 많은 폴더", example = "_중복/사진 (2).jpg" },
    };
}
