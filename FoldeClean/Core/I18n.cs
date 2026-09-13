namespace FoldeClean;

/// <summary>
/// 백엔드가 만드는 "이름"의 번역. 화면 문구는 ui/i18n.js 가 맡고, 여기서는 실제 폴더 이름이 되는 것만 다룬다.
/// 검사 사유·중복 안내 같은 설명은 코드(reason.exec, dup|경로)로만 넘기고 화면에서 번역한다.
/// </summary>
public static class I18n
{
    public static volatile string Lang = "ko";
    public static readonly string[] Supported = { "ko", "en", "zh", "ja" };

    static readonly Dictionary<string, Dictionary<string, string>> Cat = new()
    {
        ["ko"] = new() { ["documents"] = "문서", ["images"] = "이미지", ["videos"] = "동영상", ["audio"] = "음악", ["archives"] = "압축", ["installers"] = "설치파일", ["code"] = "코드", ["fonts"] = "폰트", ["shortcuts"] = "바로가기", ["other"] = "기타", ["dupes"] = "_중복", ["root"] = "(루트)" },
        ["en"] = new() { ["documents"] = "Documents", ["images"] = "Images", ["videos"] = "Videos", ["audio"] = "Music", ["archives"] = "Archives", ["installers"] = "Installers", ["code"] = "Code", ["fonts"] = "Fonts", ["shortcuts"] = "Shortcuts", ["other"] = "Other", ["dupes"] = "_Duplicates", ["root"] = "(root)" },
        ["zh"] = new() { ["documents"] = "文档", ["images"] = "图片", ["videos"] = "视频", ["audio"] = "音乐", ["archives"] = "压缩包", ["installers"] = "安装程序", ["code"] = "代码", ["fonts"] = "字体", ["shortcuts"] = "快捷方式", ["other"] = "其他", ["dupes"] = "_重复", ["root"] = "(根目录)" },
        ["ja"] = new() { ["documents"] = "書類", ["images"] = "画像", ["videos"] = "動画", ["audio"] = "音楽", ["archives"] = "圧縮", ["installers"] = "インストーラー", ["code"] = "コード", ["fonts"] = "フォント", ["shortcuts"] = "ショートカット", ["other"] = "その他", ["dupes"] = "_重複", ["root"] = "(ルート)" },
    };

    public static string Name(string key) => (Cat.TryGetValue(Lang, out var d) ? d : Cat["ko"]).TryGetValue(key, out var v) ? v : key;

    public static void Set(string lang) { if (Supported.Contains(lang)) Lang = lang; }
}
