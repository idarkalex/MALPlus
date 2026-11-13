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

#if ANDROID
        Microsoft.Maui.Handlers.LabelHandler.Mapper.AppendToMapping("FixSingleLineNoWrap", (handler, view) =>
        {
            if (view is Label label && label.LineBreakMode == LineBreakMode.NoWrap)
            {
                handler.PlatformView.SetSingleLine(true);
            }
        });
#endif

        return builder.Build();
    }
}
