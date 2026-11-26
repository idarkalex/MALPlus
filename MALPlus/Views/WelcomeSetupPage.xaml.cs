using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using MALPlus.Controls;
using MALPlus.Services;
using MALPlus.XShared.Utils;
using MALPlus.XShared.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using SkiaSharp;

namespace MALPlus.Views;

public partial class WelcomeSetupPage : ContentPage
{
    private sealed record PreviewAnimeSample(
        string EnglishTitle,
        string RomajiTitle,
        string RemotePosterUrl,
        string Format,
        string CommunityScore,
        string PersonalScore,
        string Season,
        string StatusShort,
        Color StatusColor,
        string Progress,
        string AiredBadge,
        string Countdown,
        Color GradTop,
        Color GradBottom);

    private static readonly PreviewAnimeSample[] Samples =
    {
        new("Frieren: Beyond Journey's End", "Sousou no Frieren",
            "https://cdn.myanimelist.net/images/anime/1015/138006.jpg",
            "TV", "★ 9.2", "★ 10", "Fall 23",
            "W", Color.FromArgb("#4ADE80"), "18/28", null, null,
            Color.FromArgb("#1D3557"), Color.FromArgb("#457B9D")),
        new("Solo Leveling", "Ore dake Level Up na Ken",
            "https://cdn.myanimelist.net/images/anime/1801/142390.jpg",
            "TV", "★ 8.3", "★ 9", "Win 24",
            "W", Color.FromArgb("#4ADE80"), "8/12", "EP 10/12", "2D",
            Color.FromArgb("#1A1336"), Color.FromArgb("#3B82F6")),
        new("Your Name.", "Kimi no Na wa.",
            "https://cdn.myanimelist.net/images/anime/5/87048.jpg",
            "Movie", "★ 8.8", "★ 9", "Sum 16",
            "C", Color.FromArgb("#60A5FA"), "1/1", null, null,
            Color.FromArgb("#3B1F5E"), Color.FromArgb("#E879F9")),
        new("The Apothecary Diaries", "Kusuriya no Hitorigoto",
            "https://cdn.myanimelist.net/images/anime/1708/138033.jpg",
            "TV", "★ 8.9", "★ 8", "Fall 23",
            "W", Color.FromArgb("#4ADE80"), "12/24", "EP 20/24", "5D",
            Color.FromArgb("#0F3D3E"), Color.FromArgb("#10B981")),
        new("Dandadan", "Dandadan",
            "https://cdn.myanimelist.net/images/anime/1584/143719.jpg",
            "ONA", "★ 8.6", "★ 9", "Fall 24",
            "P", Color.FromArgb("#FFFFFF"), "0/12", "EP 6/12", "Airing",
            Color.FromArgb("#4C1D24"), Color.FromArgb("#F97316")),
        new("Attack on Titan", "Shingeki no Kyojin",
            "https://cdn.myanimelist.net/images/anime/10/47347.jpg",
            "TV", "★ 8.6", "★ 10", "Spr 13",
            "C", Color.FromArgb("#60A5FA"), "25/25", null, null,
            Color.FromArgb("#2D1810"), Color.FromArgb("#9A3412")),
        new("Steins;Gate", "Steins;Gate",
            "https://cdn.myanimelist.net/images/anime/1935/127974.jpg",
            "TV", "★ 9.1", "★ 10", "Spr 11",
            "C", Color.FromArgb("#60A5FA"), "24/24", null, null,
            Color.FromArgb("#1E293B"), Color.FromArgb("#475569")),
        new("Chainsaw Man", "Chainsaw Man",
            "https://cdn.myanimelist.net/images/anime/1806/126216.jpg",
            "TV", "★ 8.4", "★ 9", "Fall 22",
            "W", Color.FromArgb("#4ADE80"), "9/12", null, null,
            Color.FromArgb("#3F1215"), Color.FromArgb("#DC2626")),
        new("Spy x Family", "Spy x Family",
            "https://cdn.myanimelist.net/images/anime/1441/122795.jpg",
            "TV", "★ 8.4", "★ 8", "Spr 22",
            "W", Color.FromArgb("#4ADE80"), "6/12", "EP 9/12", "4D",
            Color.FromArgb("#143326"), Color.FromArgb("#16A34A")),
        new("Demon Slayer", "Kimetsu no Yaiba",
            "https://cdn.myanimelist.net/images/anime/1286/99889.jpg",
            "TV", "★ 8.4", "★ 9", "Spr 19",
            "C", Color.FromArgb("#60A5FA"), "26/26", null, null,
            Color.FromArgb("#162A2C"), Color.FromArgb("#0D9488"))
    };

    private static readonly HttpClient PosterHttp = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static TaskCompletionSource<bool> _pendingSession;
    private static bool _nextFromSettings;
    private static readonly object SessionLock = new();

    private readonly List<AnimePosterCard> _previewCards = new();
    private readonly string[] _artworkPaths = new string[Samples.Length];
    private readonly bool _fromSettings;
    private bool _finished;
    private double _lastGridHostWidth = -1;

    private SettingsViewModelBase Vm => BindingContext as SettingsViewModelBase;

    public WelcomeSetupPage()
    {
        _fromSettings = _nextFromSettings;
        _nextFromSettings = false;
        InitializeComponent();
        BindingContext = ViewModelLocator.Settings;
        EnsurePreviewArtwork();
        BuildPreviewCards();
        UpdateHeaderCopy();
        RefreshAllVisualState();
    }

    /// <summary>
    ///     Presents the interactive visual setup wizard and resolves when the user completes or skips it.
    /// </summary>
    public static async Task PresentAsync(bool fromSettings = false)
    {
        TaskCompletionSource<bool> tcs;
        lock (SessionLock)
        {
            if (_pendingSession != null && !_pendingSession.Task.IsCompleted)
            {
                tcs = _pendingSession;
            }
            else
            {
                _nextFromSettings = fromSettings;
                tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingSession = tcs;
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        if (Shell.Current != null)
                            await Shell.Current.GoToAsync("welcomesetup");
                        else
                            tcs.TrySetResult(false);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("MALPLUS WelcomeSetup nav failed: " + ex.Message);
                        tcs.TrySetResult(false);
                    }
                });
            }
        }

        await tcs.Task;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ThemeService.ThemeChanged += RefreshAllVisualState;
        RefreshAllVisualState();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeService.ThemeChanged -= RefreshAllVisualState;
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CompleteSetupAsync();
        return true;
    }

    private void UpdateHeaderCopy()
    {
        if (SkipButton != null)
            SkipButton.IsVisible = !_fromSettings;

        if (_fromSettings)
        {
            if (IntroBadgeLabel != null)
                IntroBadgeLabel.Text = "VISUAL STUDIO · LIVE PREVIEW";
            if (ApplyButton != null)
                ApplyButton.Text = "Apply & Save";
        }
        else
        {
            var isUpdate = !string.IsNullOrWhiteSpace(Settings.LastOnboardingVersion) || Credentials.HasStoredAccount();
            if (IntroBadgeLabel != null)
                IntroBadgeLabel.Text = isUpdate ? "WHAT'S NEW · VISUAL SETUP" : "WELCOME TO MAL+";
            if (ApplyButton != null)
                ApplyButton.Text = "Apply & Continue";
        }

        if (VersionPillLabel != null)
            VersionPillLabel.Text = $"v{AppInfo.VersionString}";
    }

    private void EnsurePreviewArtwork()
    {
        try
        {
            var dir = Path.Combine(FileSystem.CacheDirectory, "welcome_preview");
            Directory.CreateDirectory(dir);

            for (int i = 0; i < Samples.Length; i++)
            {
                var realFile = Path.Combine(dir, $"real_v1_{i}.jpg");
                if (File.Exists(realFile) && new FileInfo(realFile).Length > 1024)
                {
                    _artworkPaths[i] = realFile;
                    continue;
                }

                var fallbackFile = Path.Combine(dir, $"poster_v1_{i}.png");
                if (!File.Exists(fallbackFile))
                    RenderSamplePosterArt(fallbackFile, Samples[i], i);
                _artworkPaths[i] = fallbackFile;

                var idx = i;
                var url = Samples[i].RemotePosterUrl;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var bytes = await PosterHttp.GetByteArrayAsync(url);
                        if (bytes != null && bytes.Length > 1024)
                        {
                            File.WriteAllBytes(realFile, bytes);
                            _artworkPaths[idx] = realFile;
                            MainThread.BeginInvokeOnMainThread(() =>
                            {
                                if (idx < _previewCards.Count)
                                    _previewCards[idx].PosterUrl = realFile;
                            });
                        }
                    }
                    catch { }
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS preview artwork generation failed: " + ex.Message);
        }
    }

    private static void RenderSamplePosterArt(string path, PreviewAnimeSample sample, int variant)
    {
        const int w = 240;
        const int h = 340;
        using var surface = SKSurface.Create(new SKImageInfo(w, h));
        var canvas = surface.Canvas;

        var top = new SKColor((byte)(sample.GradTop.Red * 255), (byte)(sample.GradTop.Green * 255), (byte)(sample.GradTop.Blue * 255));
        var bot = new SKColor((byte)(sample.GradBottom.Red * 255), (byte)(sample.GradBottom.Green * 255), (byte)(sample.GradBottom.Blue * 255));

        using (var bgPaint = new SKPaint
        {
            Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(w, h), new[] { top, bot }, SKShaderTileMode.Clamp),
            IsAntialias = true
        })
        {
            canvas.DrawRect(0, 0, w, h, bgPaint);
        }

        // Soft glowing celestial / spotlight accent
        using (var glowPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255, 42),
            IsAntialias = true
        })
        {
            float cx = variant % 2 == 0 ? w * 0.68f : w * 0.34f;
            float cy = h * (0.28f + (variant % 3) * 0.05f);
            canvas.DrawCircle(cx, cy, 52, glowPaint);
            glowPaint.Color = new SKColor(255, 255, 255, 24);
            canvas.DrawCircle(cx, cy, 86, glowPaint);
        }

        // Layered silhouette horizon at lower third for anime key-visual depth
        using (var hillPaint = new SKPaint { Color = new SKColor(0, 0, 0, 72), IsAntialias = true })
        using (var ridge = new SKPath())
        {
            ridge.MoveTo(0, h);
            ridge.LineTo(0, h * 0.68f);
            ridge.CubicTo(w * 0.3f, h * (0.58f + (variant % 2) * 0.05f), w * 0.7f, h * 0.76f, w, h * 0.62f);
            ridge.LineTo(w, h);
            ridge.Close();
            canvas.DrawPath(ridge, hillPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        using var fs = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        data.SaveTo(fs);
    }

    private void BuildPreviewCards()
    {
        _previewCards.Clear();
        for (int i = 0; i < Samples.Length; i++)
        {
            var card = new AnimePosterCard
            {
                PosterUrl = _artworkPaths[i],
                TitleOverlay = true,
                BadgeVisible = true,
                InputTransparent = true
            };
            _previewCards.Add(card);
        }
    }

    private void OnPreviewGridHostSizeChanged(object sender, EventArgs e)
    {
        var width = PreviewGridHost?.Width ?? 0;
        if (width <= 0 || Math.Abs(width - _lastGridHostWidth) < 1)
            return;
        _lastGridHostWidth = width;
        LayoutPreviewGrid();
    }

    private int ResolvePreviewColumns(double availableWidth = -1)
    {
        if (DeviceInfo.Platform != DevicePlatform.WinUI)
        {
            var cols = Settings.GridColumns;
            if (cols >= 2 && cols <= 5)
                return cols;
        }

        double hostW = availableWidth > 0
            ? availableWidth
            : (PreviewGridHost?.Width > 0 ? PreviewGridHost.Width : Width - 32);
        if (hostW <= 0)
            hostW = DeviceDisplay.MainDisplayInfo.Width / DeviceDisplay.MainDisplayInfo.Density;

        if (hostW < 520d)
            return 3;
        if (hostW < 700d)
            return 4;

        const double targetCardWidth = 128d;
        var calculated = (int)Math.Round(hostW / targetCardWidth);
        return Math.Clamp(calculated, 4, Samples.Length);
    }

    private void LayoutPreviewGrid()
    {
        if (PreviewPosterGrid == null || PreviewGridHost == null)
            return;

        var availableW = PreviewGridHost.Width;
        if (availableW <= 0)
            availableW = Math.Max(280, Width - 32);

        int cols = ResolvePreviewColumns(availableW);
        double spacing = Settings.PosterGridSpacing ? 8d : 0d;
        double sidePadding = Settings.PosterGridSpacing ? 8d : 0d;
        double gridWidth = Math.Max(180, availableW - sidePadding * 2);

        double cardWidth = Math.Floor((gridWidth - spacing * (cols - 1)) / cols);
        double maxH = DeviceInfo.Platform == DevicePlatform.WinUI ? 184d : 176d;
        double cardHeight = Math.Round(cardWidth * 1.42);
        if (cardHeight > maxH)
        {
            cardHeight = maxH;
            cardWidth = Math.Round(cardHeight / 1.42);
        }
        else if (cardHeight < 92d)
        {
            cardHeight = 92d;
        }

        PreviewPosterGrid.Children.Clear();
        PreviewPosterGrid.RowDefinitions.Clear();
        PreviewPosterGrid.ColumnDefinitions.Clear();
        PreviewPosterGrid.ColumnSpacing = spacing;
        PreviewPosterGrid.RowSpacing = 0;
        PreviewPosterGrid.RowDefinitions.Add(new RowDefinition { Height = cardHeight });

        for (int c = 0; c < cols && c < _previewCards.Count; c++)
        {
            PreviewPosterGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = cardWidth });
            var card = _previewCards[c];
            card.WidthRequest = cardWidth;
            card.HeightRequest = cardHeight;
            Grid.SetRow(card, 0);
            Grid.SetColumn(card, c);
            PreviewPosterGrid.Children.Add(card);
        }

        UpdateMiniChrome();
    }

    private void UpdatePreviewCardMetadata()
    {
        var english = Settings.PreferEnglishTitles;
        var showFormat = Settings.ShowFormatTag;
        var showSeason = Settings.DisplaySeasonWithType;
        var showAired = Settings.ShowAiredEpisodes;
        var showCountdown = Settings.ShowAiringCountdown;
        var scoreMode = Settings.PosterScoreDisplay; // 0=Community, 1=Personal, 2=Hidden

        for (int i = 0; i < _previewCards.Count && i < Samples.Length; i++)
        {
            var s = Samples[i];
            var card = _previewCards[i];
            card.Title = english ? s.EnglishTitle : s.RomajiTitle;
            card.TypeTag = showFormat ? s.Format : string.Empty;
            card.ScoreTag = scoreMode switch
            {
                0 => s.CommunityScore,
                1 => s.PersonalScore,
                _ => string.Empty
            };
            card.SeasonTag = showSeason ? s.Season : string.Empty;
            card.BadgeAccent = s.StatusShort;
            card.BadgeAccentColor = s.StatusColor;
            card.BadgeMain = s.Progress;
            card.BadgeExtra = string.Empty;

            // Mirror AnimeItemViewModel.BottomAirTag: bottom-right red pill combining
            // aired episode count and/or broadcast countdown according to settings.
            var epText = showAired && !string.IsNullOrEmpty(s.AiredBadge) ? s.AiredBadge : null;
            var countdownText = showCountdown && !string.IsNullOrEmpty(s.Countdown) ? s.Countdown : null;
            if (!string.IsNullOrEmpty(epText) && !string.IsNullOrEmpty(countdownText))
                card.BottomAirTag = $"{epText} · {countdownText}";
            else if (!string.IsNullOrEmpty(epText))
                card.BottomAirTag = epText;
            else if (!string.IsNullOrEmpty(countdownText))
                card.BottomAirTag = countdownText;
            else
                card.BottomAirTag = string.Empty;
        }
    }

    private void RefreshAllVisualState()
    {
        try
        {
            UpdateThemeSelectionCards();
            UpdateOptionChips();
            UpdateMiniChrome();
            UpdatePreviewCardMetadata();
            LayoutPreviewGrid();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS WelcomeSetup refresh failed: " + ex.Message);
        }
    }

    private void UpdateThemeSelectionCards()
    {
        var theme = Settings.AppTheme;
        var cards = new[] { ThemeCard0, ThemeCard1, ThemeCard2 };
        var accent = Color.FromArgb("#0066FF");
        var normalBorder = ThemeService.CurrentBorder;

        for (int i = 0; i < cards.Length; i++)
        {
            if (cards[i] == null) continue;
            bool selected = i == theme;
            cards[i].Stroke = new SolidColorBrush(selected ? accent : normalBorder);
            cards[i].StrokeThickness = selected ? 2.0 : 1.0;
        }
    }

    private void UpdateOptionChips()
    {
        var vm = Vm;
        if (vm == null) return;

        var colChips = new[] { ColChip0, ColChip1, ColChip2, ColChip3, ColChip4 };
        for (int i = 0; i < colChips.Length; i++)
            ApplyChipVisual(colChips[i], i == vm.GridColumnsIndex);

        var scoreChips = new[] { ScoreChip0, ScoreChip1, ScoreChip2 };
        for (int i = 0; i < scoreChips.Length; i++)
            ApplyChipVisual(scoreChips[i], i == vm.PosterScoreDisplayIndex);

        var startNames = new[] { "Anime list", "Manga list", "Discover" };
        var startChips = new[] { StartChip0, StartChip1, StartChip2 };
        for (int i = 0; i < startChips.Length; i++)
            ApplyChipVisual(startChips[i], string.Equals(vm.StartPageOption, startNames[i], StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyChipVisual(Button btn, bool active)
    {
        if (btn == null) return;
        TabChipHelper.ApplyStyle(btn, active);
        btn.HeightRequest = 32;
        btn.Padding = new Thickness(12, 0);
        btn.FontSize = 12;
    }

    private void UpdateMiniChrome()
    {
        var themeLabel = Settings.AppTheme switch
        {
            1 => "Dark AMOLED",
            2 => "Real Dark",
            _ => "Midnight Blue"
        };
        var colLabel = DeviceInfo.Platform == DevicePlatform.WinUI || Settings.GridColumns == 0
            ? $"Auto ({ResolvePreviewColumns()} cols)"
            : $"{Settings.GridColumns} cols";
        var spaceLabel = Settings.PosterGridSpacing ? "Spaced" : "Seamless";

        if (PreviewSummaryLabel != null)
            PreviewSummaryLabel.Text = $"{themeLabel} · {colLabel} · {spaceLabel}";

        var start = Settings.DefaultMenuTab ?? "anime";
        if (PreviewBarTitleLabel != null)
        {
            PreviewBarTitleLabel.Text = start switch
            {
                "manga" => "Reading",
                "discover" => "Discover",
                _ => "Watching"
            };
        }

        var accent = Color.FromArgb("#0066FF");
        var inactive = ThemeService.CurrentChipText;
        UpdateMiniTab(MiniNavDiscover, start == "discover", accent, inactive);
        UpdateMiniTab(MiniNavAnime, start != "discover" && start != "manga", accent, inactive);
        UpdateMiniTab(MiniNavManga, start == "manga", accent, inactive);
        UpdateMiniTab(MiniNavMore, false, accent, inactive);
    }

    private static void UpdateMiniTab(Label lbl, bool active, Color accent, Color inactive)
    {
        if (lbl == null) return;
        lbl.TextColor = active ? accent : inactive;
        lbl.FontFamily = active ? "InterBold" : "InterSemiBold";
    }

    private void OnThemeCardTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is string s && int.TryParse(s, out int idx) && Vm != null)
        {
            Vm.AppThemeIndex = idx;
            RefreshAllVisualState();
        }
    }

    private void OnColumnsChipClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string s && int.TryParse(s, out int idx) && Vm != null)
        {
            Vm.GridColumnsIndex = idx;
            RefreshAllVisualState();
        }
    }

    private void OnScoreChipClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string s && int.TryParse(s, out int idx) && Vm != null)
        {
            Vm.PosterScoreDisplayIndex = idx;
            RefreshAllVisualState();
        }
    }

    private void OnStartPageChipClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string opt && Vm != null)
        {
            Vm.StartPageOption = opt;
            RefreshAllVisualState();
        }
    }

    private void OnOptionToggled(object sender, ToggledEventArgs e)
    {
        RefreshAllVisualState();
    }

    private void OnResetDefaultsClicked(object sender, EventArgs e)
    {
        var vm = Vm;
        if (vm == null) return;

        vm.AppThemeIndex = 0; // Midnight Blue
        vm.GridColumnsIndex = 0; // Auto
        vm.PosterGridSpacing = false;
        vm.PosterScoreDisplayIndex = 2; // Hidden
        vm.ShowFormatTag = false;
        vm.DisplaySeasonWithType = false;
        vm.ShowAiredEpisodes = false;
        vm.ShowAiringCountdown = false;
        vm.PreferEnglishTitles = false;
        vm.StartPageOption = "Anime list";

        if (SpacingSwitch != null)
            SpacingSwitch.IsToggled = false;
        RefreshAllVisualState();
    }

    private async void OnSkipClicked(object sender, EventArgs e)
    {
        await CompleteSetupAsync();
    }

    private async void OnApplyClicked(object sender, EventArgs e)
    {
        await CompleteSetupAsync();
    }

    private async Task CompleteSetupAsync()
    {
        if (_finished)
            return;
        _finished = true;

        try
        {
            Settings.LastOnboardingVersion = Settings.CurrentOnboardingVersion;
            SettingsViewModelBase.RefreshAllLoadedCards(refreshTitles: true);

            if (Shell.Current?.Navigation != null && Shell.Current.Navigation.NavigationStack.Count > 1)
                await Shell.Current.Navigation.PopAsync(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS WelcomeSetup close failed: " + ex.Message);
        }
        finally
        {
            lock (SessionLock)
            {
                _pendingSession?.TrySetResult(true);
            }
        }
    }
}
