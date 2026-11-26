using System;
using System.ComponentModel;
using System.Linq;
using MALPlus.Controls;
using MALPlus.Services;
using MALPlus.XShared.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace MALPlus.Views;

public partial class SettingsPage : ContentPage
{
    private static readonly (string Title, string Desc)[] ContentFilterOptions = new[]
    {
        ("Safe", "Hides mild nudity & adult content"),
        ("Mature", "Allows mild nudity, drops explicit"),
        ("Unfiltered", "Allow all adult content (18+)")
    };

    private static readonly (string ShortLabel, string SheetTitle, string SheetDesc)[] LibrarySortDisplayOptions = new[]
    {
        ("Title (A-Z)", "Title (A → Z)", "Alphabetical order by series title"),
        ("Personal score", "Personal score", "Highest personal score first"),
        ("Community score", "Community score", "Highest community mean score first"),
        ("Last updated", "Last updated", "Most recently updated entries first"),
        ("Episodes", "Episodes / Volumes", "Most progress watched or read first"),
        ("Start date", "Start date", "Most recently started series first"),
        ("Completion date", "Completion date", "Most recently completed series first")
    };

    private bool _initialized;
    private SettingsViewModelBase Vm => (SettingsViewModelBase)BindingContext;

    public bool LogOutButtonVisibility => MALPlus.XShared.Utils.Credentials.HasStoredAccount();

    public SettingsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.Settings;
        if (CalendarMaxItemsEntry != null)
            CalendarMaxItemsEntry.HandlerChanged += (s, e) => FixCalendarMaxItemsEntry();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!_initialized)
            NavigateToCategory(SettingsCategory.Hub);
        ThemeService.ThemeChanged += OnThemeChanged;
        if (Vm != null)
            Vm.PropertyChanged += OnVmPropertyChanged;
        OnThemeChanged();
        UpdateAccountHeader();
        UpdateSelectorLabels();
        FixCalendarMaxItemsEntry();
        UpdateCacheCount();
        UpdateDiagnosticsAndVersion();
        if (_initialized) return;
        _initialized = true;
    }

    private void FixCalendarMaxItemsEntry()
    {
#if ANDROID
        try
        {
            if (CalendarMaxItemsEntry?.Handler?.PlatformView is Android.Widget.EditText et)
            {
                et.Background = null;
                et.SetBackgroundColor(Android.Graphics.Color.Transparent);
                et.SetPadding(0, 0, 0, 0);
                et.SetMinimumHeight(0);
                et.Gravity = Android.Views.GravityFlags.Center;
            }
        }
        catch { }
#endif
    }

    private void OnCalendarMaxItemsCompleted(object sender, EventArgs e)
    {
        CalendarMaxItemsEntry?.Unfocus();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        ThemeService.ThemeChanged -= OnThemeChanged;
        if (Vm != null)
            Vm.PropertyChanged -= OnVmPropertyChanged;
        // Force refresh of loaded poster cards, titles and preview snapshots on exit from Settings
        SettingsViewModelBase.RefreshAllLoadedCards(refreshTitles: true);
    }

    private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(UpdateSelectorLabels);
    }

    private void UpdateSelectorLabels()
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;

            // 1. Theme
            if (ThemeValueLabel != null)
            {
                var idx = vm.AppThemeIndex;
                ThemeValueLabel.Text = (idx >= 0 && idx < vm.AppThemeOptions.Count)
                    ? vm.AppThemeOptions[idx]
                    : "Midnight Blue";
            }

            // 2. Score chip on posters
            if (ScoreChipValueLabel != null)
            {
                var idx = vm.PosterScoreDisplayIndex;
                ScoreChipValueLabel.Text = idx switch
                {
                    0 => "Community score",
                    1 => "Personal score",
                    2 => "Hidden",
                    _ => "Community score"
                };
            }

            // Grid columns
            if (GridColumnsValueLabel != null)
            {
                var idx = vm.GridColumnsIndex;
                GridColumnsValueLabel.Text = idx switch
                {
                    1 => "2 columns",
                    2 => "3 columns",
                    3 => "4 columns",
                    4 => "5 columns",
                    _ => "Auto"
                };
            }

            // 3. Default start page
            if (StartPageValueLabel != null)
            {
                StartPageValueLabel.Text = vm.StartPageOption ?? "Anime list";
            }

            // 4. Content rating filter
            if (ContentFilterValueLabel != null)
            {
                var idx = vm.ContentFilterIndex;
                ContentFilterValueLabel.Text = (idx >= 0 && idx < ContentFilterOptions.Length)
                    ? ContentFilterOptions[idx].Title
                    : "Safe";
            }

            // 5. Start week on
            if (WeekStartValueLabel != null)
            {
                var idx = vm.CalendarWeekStartIndex;
                WeekStartValueLabel.Text = (idx >= 0 && idx < vm.CalendarWeekStartOptions.Count)
                    ? vm.CalendarWeekStartOptions[idx]
                    : "Sunday";
            }

            // 6. Default library sort
            if (LibrarySortValueLabel != null)
            {
                var idx = vm.LibrarySortIndex;
                LibrarySortValueLabel.Text = (idx >= 0 && idx < LibrarySortDisplayOptions.Length)
                    ? LibrarySortDisplayOptions[idx].ShortLabel
                    : "Title (A-Z)";
            }

            // 6. Broadcast time zone
            if (TimeZoneValueLabel != null)
            {
                var options = vm.CalendarTimeZoneOptions;
                var idx = vm.CalendarTimeZoneIndex;
                TimeZoneValueLabel.Text = (idx >= 0 && idx < options.Count)
                    ? (options[idx].ShortLabel ?? options[idx].Label)
                    : "System default";
            }

            // 7. Share button duration
            if (ShareDurationValueLabel != null)
            {
                ShareDurationValueLabel.Text = $"{vm.ShareTimerDuration} seconds";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS UpdateSelectorLabels failed: " + ex.Message);
        }
    }

    private async void OnPickThemeClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.AppThemeIndex;
            var rows = vm.AppThemeOptions.Select((themeName, idx) =>
                new OptionsSheetItem(themeName, null, value: idx == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Theme",
                "Visual style and OLED black level", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var newIndex = vm.AppThemeOptions.IndexOf(picked);
            if (newIndex >= 0 && newIndex != current)
            {
                vm.AppThemeIndex = newIndex;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickThemeClicked failed: " + ex.Message);
        }
    }

    private async void OnPickScoreChipClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.PosterScoreDisplayIndex;
            var rows = vm.PosterScoreDisplayOptions.Select((opt, idx) =>
                new OptionsSheetItem(opt, null, value: idx == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Score chip on posters",
                "Display community rating, personal rating, or hide score chip", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var newIndex = Array.IndexOf(vm.PosterScoreDisplayOptions, picked);
            if (newIndex >= 0 && newIndex != current)
            {
                vm.PosterScoreDisplayIndex = newIndex;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickScoreChipClicked failed: " + ex.Message);
        }
    }

    private async void OnPickGridColumnsClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.GridColumnsIndex;
            var rows = vm.GridColumnsOptions.Select((opt, idx) =>
                new OptionsSheetItem(opt, null, value: idx == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Grid columns",
                "Number of poster columns displayed in anime and manga grids", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var newIndex = Array.IndexOf(vm.GridColumnsOptions, picked);
            if (newIndex >= 0 && newIndex != current)
            {
                vm.GridColumnsIndex = newIndex;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickGridColumnsClicked failed: " + ex.Message);
        }
    }

    private async void OnPickLibrarySortClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.LibrarySortIndex;
            var rows = LibrarySortDisplayOptions.Select((opt, idx) =>
                new OptionsSheetItem(opt.SheetTitle, null, description: opt.SheetDesc, value: idx == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Default library sort",
                "Choose the default ordering for your anime and manga lists", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var newIndex = -1;
            for (int i = 0; i < LibrarySortDisplayOptions.Length; i++)
            {
                if (LibrarySortDisplayOptions[i].SheetTitle == picked)
                {
                    newIndex = i;
                    break;
                }
            }
            if (newIndex >= 0 && newIndex != current)
            {
                vm.LibrarySortIndex = newIndex;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickLibrarySortClicked failed: " + ex.Message);
        }
    }

    private async void OnResetPerListSortsClicked(object sender, EventArgs e)
    {
        try
        {
            var confirmed = await OptionsSheetHost.ConfirmAsync("Reset per-list sorts",
                "Reset custom sorting for all individual status lists to the default library sort?", "Reset", destructive: true);
            if (!confirmed) return;
            MALPlus.XShared.Utils.Settings.ClearPerListSorts();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnResetPerListSortsClicked failed: " + ex.Message);
        }
    }

    private async void OnPickStartPageClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.StartPageOption;
            var rows = vm.StartPageOptions.Select(opt =>
                new OptionsSheetItem(opt, null, value: opt == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Default start page",
                "Screen opened automatically when launching the app", rows);
            if (string.IsNullOrEmpty(picked)) return;
            if (picked != current)
            {
                vm.StartPageOption = picked;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickStartPageClicked failed: " + ex.Message);
        }
    }

    private async void OnPickContentFilterClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.ContentFilterIndex;
            var rows = ContentFilterOptions.Select((opt, idx) =>
                new OptionsSheetItem(opt.Title, null, description: opt.Desc, value: idx == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Content rating filter",
                "Filter mature or adult content across search, discovery, and library", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var newIndex = -1;
            for (int i = 0; i < ContentFilterOptions.Length; i++)
            {
                if (ContentFilterOptions[i].Title == picked)
                {
                    newIndex = i;
                    break;
                }
            }
            if (newIndex >= 0 && newIndex != current)
            {
                vm.ContentFilterIndex = newIndex;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickContentFilterClicked failed: " + ex.Message);
        }
    }

    private async void OnPickCalendarWeekStartClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.CalendarWeekStartIndex;
            var rows = vm.CalendarWeekStartOptions.Select((opt, idx) =>
                new OptionsSheetItem(opt, null, value: idx == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Start week on",
                "First day column shown in the weekly calendar", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var newIndex = vm.CalendarWeekStartOptions.ToList().IndexOf(picked);
            if (newIndex >= 0 && newIndex != current)
            {
                vm.CalendarWeekStartIndex = newIndex;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickCalendarWeekStartClicked failed: " + ex.Message);
        }
    }

    private async void OnPickCalendarTimeZoneClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var options = vm.CalendarTimeZoneOptions;
            var current = vm.CalendarTimeZoneIndex;
            var rows = options.Select((opt, idx) =>
                new OptionsSheetItem(opt.Label, null, description: opt.Subtitle, value: idx == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Broadcast time zone",
                "Select a time zone to align weekly broadcast days", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var newIndex = -1;
            for (int i = 0; i < options.Count; i++)
            {
                if (options[i].Label == picked)
                {
                    newIndex = i;
                    break;
                }
            }
            if (newIndex >= 0 && newIndex != current)
            {
                vm.CalendarTimeZoneIndex = newIndex;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickCalendarTimeZoneClicked failed: " + ex.Message);
        }
    }

    private static readonly int[] ShareDurationOptions = { 10, 15, 20, 30 };

    private async void OnPickShareDurationClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            var current = vm.ShareTimerDuration;
            var rows = ShareDurationOptions.Select(d =>
                new OptionsSheetItem($"{d} seconds", null, value: d == current ? "✓" : null)
            ).ToList();
            var picked = await OptionsSheetHost.PickAsync("Share button duration",
                "How long the floating share button stays on screen", rows);
            if (string.IsNullOrEmpty(picked)) return;
            var match = ShareDurationOptions.FirstOrDefault(d => $"{d} seconds" == picked);
            if (match > 0)
            {
                vm.ShareTimerDuration = match;
                UpdateSelectorLabels();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnPickShareDurationClicked failed: " + ex.Message);
        }
    }

    private void OnResetShareTemplatesClicked(object sender, EventArgs e)
    {
        try
        {
            var vm = Vm;
            if (vm == null) return;
            vm.ResetShareTemplatesToDefault();
            UpdateSelectorLabels();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnResetShareTemplatesClicked failed: " + ex.Message);
        }
    }

    private async void OnOpenVisualSetupClicked(object sender, EventArgs e)
    {
        try
        {
            await WelcomeSetupPage.PresentAsync(fromSettings: true);
            UpdateSelectorLabels();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS OnOpenVisualSetupClicked failed: " + ex.Message);
        }
    }

    private void OnThemeChanged()
    {
        UpdateSelectorLabels();
    }

    private void UpdateDiagnosticsAndVersion()
    {
        Task.Run(() =>
        {
            try
            {
                var summary = AppLog.GetLogSizeSummary();
                var version = $"MAL+ v{AppInfo.VersionString} (Build {AppInfo.BuildString})";
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (LogSizeLabel != null)
                        LogSizeLabel.Text = summary;
                    if (VersionLabel != null)
                        VersionLabel.Text = version;
                    if (DetailVersionLabel != null)
                        DetailVersionLabel.Text = version;
                });
            }
            catch { }
        });
    }

    private void UpdateCacheCount()
    {
        Task.Run(() =>
        {
            try
            {
                var cache = MALPlus.XShared.ViewModels.ResourceLocator.DataCacheService as MALPlus.Services.MauiDataCache;
                var files = cache?.CountCachedFiles() ?? 0;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (CacheCountLabel != null)
                    {
                        CacheCountLabel.Text = files > 0
                            ? $"{files} files cached"
                            : "No cached files yet";
                    }
                });
            }
            catch { }
        });
    }

    private void UpdateAccountHeader()
    {
        if (AccountNameLabel == null)
            return;
        var name = MALPlus.XShared.Utils.Credentials.UserName;
        AccountNameLabel.Text = string.IsNullOrWhiteSpace(name)
            ? "Not signed in"
            : $"Signed in as {name}";
        if (LogOutButton != null)
            LogOutButton.IsVisible = LogOutButtonVisibility;
    }

    private async void OnLogOutClicked(object sender, EventArgs e)
    {
        try
        {
            var ok = await OptionsSheetHost.ConfirmAsync("Sign out",
                "You will need to sign in again, and the forums, clubs and wall will stay unavailable until then.",
                "Sign out", destructive: true);
            if (!ok)
                return;

            ViewModelLocator.LogIn.LogOutCommand.Execute(null);
            MALPlus.XShared.Comm.Query.RefreshClientAuthHeader();
            UpdateAccountHeader();
            if (MALPlus.XShared.Utils.Credentials.Authenticated)
                await Shell.Current.GoToAsync("login");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS logout failed: " + ex.GetType().Name);
        }
    }

    private async void OnClearCachesClicked(object sender, EventArgs e)
    {
        try
        {
            var ok = await OptionsSheetHost.ConfirmAsync("Clear caches",
                "This will remove all locally cached data and force a fresh fetch on the next open.",
                "Clear", destructive: true);
            if (!ok) return;

            try
            {
                await ResourceLocator.DataCacheService.ClearApiRelatedCache();
            }
            catch { }

            SettingsViewModelBase.InvalidateSettings(resetData: true, refreshTitles: true);
            UpdateCacheCount();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS clear caches failed: " + ex.Message);
        }
    }

    private async void OnExportLogsClicked(object sender, EventArgs e)
    {
        try
        {
            var success = await AppLog.ExportDiagnosticsAsync();
            if (!success)
            {
                await OptionsSheetHost.ConfirmAsync("Diagnostics", "Could not generate diagnostic export.", "OK");
            }
        }
        catch (Exception ex)
        {
            await OptionsSheetHost.ConfirmAsync("Diagnostics", $"Error: {ex.Message}", "OK");
        }
    }

    private async void OnClearLogsClicked(object sender, EventArgs e)
    {
        try
        {
            var ok = await OptionsSheetHost.ConfirmAsync("Clear logs",
                "Do you want to delete all local diagnostic logs from this device?",
                "Clear", destructive: true, cancelLabel: "Cancel");
            if (!ok)
                return;

            AppLog.ClearLogs();
            UpdateDiagnosticsAndVersion();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS clear logs failed: " + ex.Message);
        }
    }

    private async void OnCheckUpdatesClicked(object sender, EventArgs e)
    {
        await AppUpdateService.CheckManualUpdateAsync();
    }

    private async void OnViewChangelogClicked(object sender, EventArgs e)
    {
        await AppUpdateService.ShowCurrentChangelogAsync();
    }

    private async void OnGitHubStarClicked(object sender, EventArgs e)
    {
        await MALPlus.Services.GitHubStarService.OpenGitHubForStarAsync();
    }

    private async void OnGitHubIssuesClicked(object sender, EventArgs e)
    {
        try
        {
            await Launcher.OpenAsync(new Uri("https://github.com/idarkalex/MALPlus/issues"));
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS open github issues failed: " + ex.Message);
        }
    }

    #region Category Navigation (Option 1 Subcategories)

    public enum SettingsCategory
    {
        Hub,
        Appearance,
        Posters,
        Library,
        Calendar,
        Sharing,
        Data
    }

    private SettingsCategory _currentCategory = SettingsCategory.Hub;

    public void NavigateToCategory(SettingsCategory category)
    {
        _currentCategory = category;

        if (category == SettingsCategory.Hub)
        {
            DetailScrollView.IsVisible = false;
            HubScrollView.IsVisible = true;
            HubScrollView.ScrollToAsync(0, 0, false);
            return;
        }

        HubScrollView.IsVisible = false;
        DetailScrollView.IsVisible = true;

        CategoryAppearanceContent.IsVisible = category == SettingsCategory.Appearance;
        CategoryPostersContent.IsVisible = category == SettingsCategory.Posters;
        CategoryLibraryContent.IsVisible = category == SettingsCategory.Library;
        CategoryCalendarContent.IsVisible = category == SettingsCategory.Calendar;
        CategorySharingContent.IsVisible = category == SettingsCategory.Sharing;
        CategoryDataContent.IsVisible = category == SettingsCategory.Data;

        switch (category)
        {
            case SettingsCategory.Appearance:
                DetailTitleLabel.Text = "Appearance & Display";
                DetailDescLabel.Text = DeviceInfo.Platform == DevicePlatform.WinUI
                    ? "Theme, start page, language"
                    : "Theme, grid columns, start page, language";
                break;
            case SettingsCategory.Posters:
                DetailTitleLabel.Text = "Poster Cards & Badges";
                DetailDescLabel.Text = "Score chip, media format, season, badges";
                break;
            case SettingsCategory.Library:
                DetailTitleLabel.Text = "Library & Tracking";
                DetailDescLabel.Text = "List sort, per-list ordering, auto-dates";
                break;
            case SettingsCategory.Calendar:
                DetailTitleLabel.Text = "Broadcast Calendar";
                DetailDescLabel.Text = "Time zone, week start, day filters and limits";
                break;
            case SettingsCategory.Sharing:
                DetailTitleLabel.Text = "Sharing & Social";
                DetailDescLabel.Text = "Floating share button, post templates";
                break;
            case SettingsCategory.Data:
                DetailTitleLabel.Text = "Data & Storage";
                DetailDescLabel.Text = "Clear caches, local logs, updates";
                break;
        }

        DetailScrollView.ScrollToAsync(0, 0, false);
    }

    private void OnCategoryAppearanceTapped(object sender, EventArgs e) => NavigateToCategory(SettingsCategory.Appearance);
    private void OnCategoryPostersTapped(object sender, EventArgs e) => NavigateToCategory(SettingsCategory.Posters);
    private void OnCategoryLibraryTapped(object sender, EventArgs e) => NavigateToCategory(SettingsCategory.Library);
    private void OnCategoryCalendarTapped(object sender, EventArgs e) => NavigateToCategory(SettingsCategory.Calendar);
    private void OnCategorySharingTapped(object sender, EventArgs e) => NavigateToCategory(SettingsCategory.Sharing);
    private void OnCategoryDataTapped(object sender, EventArgs e) => NavigateToCategory(SettingsCategory.Data);

    private void OnBackToHubClicked(object sender, EventArgs e) => NavigateToCategory(SettingsCategory.Hub);

    protected override bool OnBackButtonPressed()
    {
        if (_currentCategory != SettingsCategory.Hub)
        {
            NavigateToCategory(SettingsCategory.Hub);
            return true;
        }
        return base.OnBackButtonPressed();
    }

    #endregion
}
