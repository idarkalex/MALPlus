using System;
using MALPlus.Services;
using MALPlus.XShared.ViewModels;
using Microsoft.Maui.ApplicationModel;

namespace MALPlus.Views;

public partial class SettingsPage : ContentPage
{
    private bool _initialized;
    private SettingsViewModelBase Vm => (SettingsViewModelBase)BindingContext;

    public bool LogOutButtonVisibility => MALPlus.XShared.Utils.Credentials.HasStoredAccount();

    public SettingsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.Settings;
        UpdateAccountHeader();
        FillContentFilterPicker();
    }

    private void FillContentFilterPicker()
    {
        if (ContentFilterPicker == null)
            return;
        ContentFilterPicker.ItemsSource = new[]
        {
            "Safe — hides mild nudity & adult",
            "Mature — allows mild nudity, drops explicit",
            "Unfiltered — allow all adult content (18+)"
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateAccountHeader();
        UpdateCacheCount();
        UpdateDiagnosticsAndVersion();
        if (_initialized) return;
        _initialized = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Force refresh of loaded poster cards, titles and preview snapshots on exit from Settings
        SettingsViewModelBase.RefreshAllLoadedCards(refreshTitles: true);
    }

    private void UpdateDiagnosticsAndVersion()
    {
        try
        {
            if (LogSizeLabel != null)
                LogSizeLabel.Text = AppLog.GetLogSizeSummary();
            if (VersionLabel != null)
                VersionLabel.Text = $"MAL+ v{AppInfo.VersionString} (Build {AppInfo.BuildString})";
        }
        catch { }
    }

    private void UpdateCacheCount()
    {
        try
        {
            var cache = MALPlus.XShared.ViewModels.ResourceLocator.DataCacheService as MALPlus.Services.MauiDataCache;
            var files = cache?.CountCachedFiles() ?? 0;
            CacheCountLabel.Text = files > 0
                ? $"{files} files cached"
                : "No cached files yet";
        }
        catch
        {
        }
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
}
