using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FoldeClean;

public static class Paths
{
    public static readonly string AppData = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FoldeClean");
    public static readonly string Journals = Path.Combine(AppData, "journals");
}

public static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}

/// <summary>탐색된 파일 하나.</summary>
public sealed class FileRec
{
    public const int AttrHidden = 0x2, AttrSystem = 0x4, AttrReparse = 0x400, AttrCloud = 0x400000;

    public string Path = "";
    public string Name = "";
    public string Ext = "";        // 소문자, 점 없음
    public long Size;
    public double Mtime;           // unix seconds
    public double Ctime;
    public int Attrs;
    public int Depth;
    public string Risk = "safe";   // safe | warn | block
    public List<string> Reasons = new();   // 사유 코드: "reason.exec", "reason.project|이름" (화면에서 번역)
    public string Group = "";              // 묶음 키: 프로그램/프로젝트 폴더 또는 상위 폴더 경로

    public bool Hidden => (Attrs & AttrHidden) != 0;
    public bool System => (Attrs & AttrSystem) != 0;
    public bool CloudPlaceholder => (Attrs & AttrCloud) != 0;

    public Dictionary<string, object?> ToDict() => new()
    {
        ["path"] = Path, ["name"] = Name, ["ext"] = Ext, ["size"] = Size, ["mtime"] = Mtime,
        ["depth"] = Depth, ["risk"] = Risk, ["reasons"] = Reasons, ["hidden"] = Hidden, ["system"] = System, ["group"] = Group,
    };
}

public sealed class ScanResult
{
    public string Root = "";
    public List<FileRec> Files = new();
    public List<string> Dirs = new();
    public int SkippedLinks, Errors;
    public double Elapsed;

    public Dictionary<string, object?> Summary() => new()
    {
        ["root"] = Root, ["file_count"] = Files.Count, ["dir_count"] = Dirs.Count,
        ["total_size"] = Files.Sum(f => f.Size), ["skipped_links"] = SkippedLinks, ["errors"] = Errors,
        ["elapsed"] = Math.Round(Elapsed, 2),
    };
}

/// <summary>이동 한 건.</summary>
public sealed class MoveRec
{
    [JsonPropertyName("src")] public string Src { get; set; } = "";
    [JsonPropertyName("dst")] public string Dst { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("ext")] public string Ext { get; set; } = "";
    [JsonPropertyName("risk")] public string Risk { get; set; } = "safe";
    [JsonPropertyName("reasons")] public List<string> Reasons { get; set; } = new();
    [JsonPropertyName("src_group")] public string SrcGroup { get; set; } = "";
    [JsonPropertyName("dst_group")] public string DstGroup { get; set; } = "";
    [JsonPropertyName("dst_rel")] public string DstRel { get; set; } = "";
    [JsonPropertyName("category")] public string Category { get; set; } = "";
    [JsonPropertyName("renamed")] public bool Renamed { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
    [JsonPropertyName("rule")] public string? Rule { get; set; }   // 적용된 사용자 규칙 이름 (Pro)
}

public static class TimeUtil
{
    public static double ToUnix(DateTime t) => new DateTimeOffset(t.ToUniversalTime()).ToUnixTimeMilliseconds() / 1000.0;
    public static DateTime FromUnix(double s) => DateTimeOffset.FromUnixTimeMilliseconds((long)(s * 1000)).LocalDateTime;
    public static double NowUnix => ToUnix(DateTime.Now);
}
