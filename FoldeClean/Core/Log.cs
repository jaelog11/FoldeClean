using System.IO;
using System.Text;

namespace FoldeClean;

/// <summary>오류 로그. %LOCALAPPDATA%\FoldeClean\logs\app.log 에 남기고, 오류 보고서에 마지막 부분을 담는다.</summary>
public static class Log
{
    static readonly object _lock = new();
    public static readonly string Dir = Path.Combine(Paths.AppData, "logs");
    public static readonly string File_ = Path.Combine(Dir, "app.log");
    const long MaxBytes = 2 * 1024 * 1024;

    public static void Write(string level, string msg, Exception? ex = null)
    {
        try
        {
            lock (_lock)
            {
                Directory.CreateDirectory(Dir);
                if (File.Exists(File_) && new FileInfo(File_).Length > MaxBytes)
                    File.Move(File_, Path.Combine(Dir, "app.prev.log"), overwrite: true);
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(' ').Append(level).Append(' ').Append(msg);
                if (ex != null) sb.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message).AppendLine().Append(ex.StackTrace);
                File.AppendAllText(File_, sb.AppendLine().ToString(), new UTF8Encoding(false));
            }
        }
        catch { /* 로그 실패는 무시 */ }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", msg, ex);

    public static string Tail(int maxChars = 20000)
    {
        try
        {
            if (!File.Exists(File_)) return "(no log)";
            var s = File.ReadAllText(File_);
            return s.Length <= maxChars ? s : s[^maxChars..];
        }
        catch (Exception ex) { return "(log read failed: " + ex.Message + ")"; }
    }
}
