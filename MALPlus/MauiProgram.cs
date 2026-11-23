using MALPlus.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MALPlus;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .ConfigureMauiHandlers(handlers =>
            {
                // MAUI 11 RC1 has no MediaElement, so the OP/ED direct video gets ExoPlayer
                // through our own handler over AndroidX Media3. Registered explicitly rather than
                // left to assembly scanning, because nothing else in this app registers a handler.
                handlers.AddHandler<Controls.NativeVideoView, Controls.NativeVideoViewHandler>();
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("inter_regular.ttf", "Inter");
                fonts.AddFont("inter_medium.ttf", "InterMedium");
                fonts.AddFont("inter_semibold.ttf", "InterSemiBold");
                fonts.AddFont("inter_bold.ttf", "InterBold");
                fonts.AddFont("fontawesomewebfont.ttf", "FontAwesome");
            });

        MauiViewModelLocator.RegisterDependencies();

#if WINDOWS
        builder.ConfigureImageSources(services =>
        {
            services.AddService<IFileImageSource, Platforms.Windows.WindowsFileImageSourceService>();
        });

        Microsoft.Maui.Handlers.ImageHandler.Mapper.PrependToMapping("WindowsBareImageFix", (handler, view) =>
        {
            if (view.Source is FileImageSource fis && !string.IsNullOrEmpty(fis.File))
            {
                var f = fis.File;
                if (!f.Contains('.'))
                {
                    fis.File = f + ".png";
                }
                else if (f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    fis.File = f[..^4] + ".png";
                }
            }
        });

        Microsoft.Maui.Handlers.ImageButtonHandler.Mapper.PrependToMapping("WindowsBareImageButtonFix", (handler, view) =>
        {
            if (view.Source is FileImageSource fis && !string.IsNullOrEmpty(fis.File))
            {
                var f = fis.File;
                if (!f.Contains('.'))
                {
                    fis.File = f + ".png";
                }
                else if (f.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    fis.File = f[..^4] + ".png";
                }
            }
        });
#endif

#if ANDROID
        MALPlus.XShared.Comm.AppUpdateQuery.TargetArchitectureProvider = () =>
        {
            try
            {
                var abis = Android.OS.Build.SupportedAbis;
                if (abis != null)
                {
                    foreach (var abi in abis)
                    {
                        if (abi.Equals("x86_64", StringComparison.OrdinalIgnoreCase) ||
                            abi.Equals("x86", StringComparison.OrdinalIgnoreCase))
                            return "x86_64";
                        if (abi.Equals("arm64-v8a", StringComparison.OrdinalIgnoreCase))
                            return "arm64";
                    }
                }
            }
            catch { }
            return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.X64 ? "x86_64" : "arm64";
        };

        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping("FixSingleLineNoWrap", (handler, view) =>
        {
            if (view is Label label && label.LineBreakMode == LineBreakMode.NoWrap)
            {
                handler.PlatformView.SetSingleLine(true);
            }
        });

        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("CleanEntryNoUnderline", (handler, view) =>
        {
            handler.PlatformView.Background = null;
            handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
            handler.PlatformView.SetPadding(0, 0, 0, 0);
            handler.PlatformView.SetMinimumHeight(0);
        });
#endif

        return builder.Build();
    }
}
