using Avalonia;

namespace UwDiff.App;

internal static class Program
{
    /// <summary>
    /// 起動のしかた:
    ///   UwDiff                         空の画面
    ///   UwDiff 旧 新 [uvd のオプション]  比べて見せる
    ///   UwDiff --result 置き場所        uvd -open が書いた結果を見せる（もう一度比べない）
    /// </summary>
    [STAThread]
    public static int Main(string[] args)
    {
        App.StartArgs = args;
        Loc.Load();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex) when (ex.Message.Contains("XOpenDisplay", StringComparison.Ordinal))
        {
            // Linux で画面の表示先が無い（ssh・コンテナなど）。CLI の uvd なら使える
            Console.Error.WriteLine("UwDiff: 画面を出す先（DISPLAY）がありません。画面の無い所では CLI の uvd を使ってください。");
            return 1;
        }
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
