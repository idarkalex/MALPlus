using Microsoft.UI.Xaml;

namespace MALPlus.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	/// <summary>
	/// Initializes the singleton application object. This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		this.InitializeComponent();
		UnhandledException += (sender, e) =>
		{
			var msg = $"[WINUI UNHANDLED] {e.Message}\nException: {e.Exception}\nStackTrace: {e.Exception?.StackTrace}";
			System.Diagnostics.Debug.WriteLine(msg);
			Console.WriteLine(msg);
			try
			{
				System.IO.File.AppendAllText("winui_crash.log", $"[{System.DateTime.Now:O}] {msg}\n");
			}
			catch { }
			e.Handled = true;
		};
	}

	protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
