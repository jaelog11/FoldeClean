using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FoldeClean;

/// <summary>WebView2 창. JS의 postMessage({id,name,args}) 를 받아 Api를 호출하고 {id,result} 로 답한다.</summary>
public partial class MainWindow : Window
{
    private readonly Api _api = new();
    private static readonly string UiDir = Path.Combine(Paths.AppData, "ui");

    public MainWindow()
    {
        InitializeComponent();
        Title = $"FoldeClean {Api.Version} · 안전한 폴더 정리";
        _api.PickFolder = PickFolderOnUiThread;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        ExtractUi();
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Paths.AppData, "webview2"));
        await webView.EnsureCoreWebView2Async(env);
        var core = webView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.SetVirtualHostNameToFolderMapping("app.foldeclean", UiDir, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnWebMessage;
        core.Navigate("https://app.foldeclean/index.html");
    }

    /// <summary>exe 안에 내장된 화면 파일을 사용자 폴더로 풀어 놓는다.</summary>
    private static void ExtractUi()
    {
        Directory.CreateDirectory(UiDir);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("ui/")))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            using var f = File.Create(Path.Combine(UiDir, name[3..]));
            s.CopyTo(f);
        }
    }

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        long id = 0;
        object? result;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt64();
            var name = root.GetProperty("name").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
            result = name == "pick_folder"
                ? _api.Call(name, args)                         // 대화상자는 UI 스레드에서
                : await Task.Run(() => _api.Call(name, args));  // 나머지는 백그라운드
        }
        catch (Exception ex)
        {
            Log.Error("api call failed", ex);
            result = new Dictionary<string, object?> { ["error"] = ex.Message };
        }
        var json = JsonSerializer.Serialize(new { id, result }, Json.Options);
        webView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private string? PickFolderOnUiThread()
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "정리할 폴더 선택" };
        return dlg.ShowDialog(this) == true ? dlg.FolderName : null;
    }
}
