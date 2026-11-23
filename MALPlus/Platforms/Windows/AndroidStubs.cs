#if WINDOWS
using System;
using System.Threading.Tasks;

namespace Android.Util
{
    public static class Log
    {
        public static void Info(string tag, string msg) => System.Diagnostics.Debug.WriteLine($"[{tag}] {msg}");
        public static void Warn(string tag, string msg) => System.Diagnostics.Debug.WriteLine($"[{tag}] WARN: {msg}");
        public static void Error(string tag, string msg) => System.Diagnostics.Debug.WriteLine($"[{tag}] ERROR: {msg}");
        public static void Debug(string tag, string msg) => System.Diagnostics.Debug.WriteLine($"[{tag}] DEBUG: {msg}");
        public static void Verbose(string tag, string msg) => System.Diagnostics.Debug.WriteLine($"[{tag}] VERBOSE: {msg}");
    }
}

namespace Android.Views
{
    public class View
    {
        public void Post(Action action) { }
    }
}

namespace Android.Webkit
{
    public class WebView : Android.Views.View
    {
        public void LoadDataWithBaseURL(string baseUrl, string data, string mimeType, string encoding, string historyUrl) { }
    }
}

namespace Android.Widget
{
    public class EditText : Android.Views.View
    {
        public object Background { get; set; }
        public void SetPadding(int left, int top, int right, int bottom) { }
        public int Gravity { get; set; }
    }
}

namespace MALPlus
{
    public static class MainActivity
    {
        public static void ArmMenuBackCallback(Action callback) { }
        public static void DisarmMenuBackCallback() { }
        public static void ArmVideoBackCallback() { }
        public static void DisarmVideoBackCallback() { }
        public static void ArmBackNavCallback() { }
        public static void DisarmBackNavCallback() { }
        public static void SetWindowThemeColors(bool isAmoled) { }
        public static Task ApplyPendingDeepLinkAsync() => Task.CompletedTask;
    }
}
#endif
