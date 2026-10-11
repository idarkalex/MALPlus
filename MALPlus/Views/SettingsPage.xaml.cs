using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels;

namespace MALPlus.Views;

public partial class SettingsPage : ContentPage
{
    private bool _initialized;
    private SettingsViewModelBase Vm => (SettingsViewModelBase)BindingContext;

    /// <summary>
    ///     Whether the sign-out row is offered: a live session, or anything still stored for the
    ///     account. Hiding it on <c>Authenticated</c> alone took the only way out away exactly when
    ///     a session had gone stale but the vault entry, the username and the website cookies were
    ///     all still on the device.
    /// </summary>
    public bool LogOutButtonVisibility => MALPlus.XShared.Utils.Credentials.HasStoredAccount();

    public SettingsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.Settings;
        UpdateAccountHeader();
        FillContentFilterPicker();
    }

    /// <summary>
    ///     Names for the three content tiers, in the order the enum declares them.
    /// </summary>
    /// <remarks>
    ///     Written out rather than read off the enum because an enum name is a configuration label, and
    ///     "Unfiltered" means nothing to a reader who has not met the rating tiers. The index is the
    ///     whole contract with the view model, so the order here must match the enum and the fallback
    ///     there puts an out of range index on Safe.
    /// </remarks>
    private void FillContentFilterPicker()
    {
        if (ContentFilterPicker == null)
            return;
        ContentFilterPicker.ItemsSource = new[]
        {
            "Safe — hides mild nudity",
            "Mature — keeps mild nudity, drops explicit",
            "Allow adult content"
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateAccountHeader();
        UpdateCacheCount();
        if (_initialized) return;
        _initialized = true;
    }

    /// <summary>
    ///     Shows how many files the cache holds, which is what "Clear caches" removes.
    /// </summary>
    /// <remarks>
    ///     The label was bound to a property nothing ever assigned, so it read "N/A files cached" and
    ///     had for as long as this page existed.
    /// </remarks>
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
            // A count is not worth failing a settings page over.
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

// Drops the api tokens, the user id and the MAL website session cookies together, so
              // the next sign in is the only thing that can bring the community sections back.
              // The same command the login page's own sign-out uses, so the library cache, the
              // http context and the stored list all go too; this page used to reset the
              // credentials alone and left the previous user's library in memory behind it.
              // It lives on LogInViewModel, not on this page's own view model.
              ViewModelLocator.LogIn.LogOutCommand.Execute(null);
              MALPlus.XShared.Comm.Query.RefreshClientAuthHeader();
              UpdateAccountHeader();
              // The shell already navigates to the login page when that command runs. This is the
              // belt-and-braces path for the case where it could not.
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
            // Best-effort: ask the SimpleIoc-resolved data cache to invalidate.
            try
            {
                await ResourceLocator.DataCacheService.ClearApiRelatedCache();
            }
            catch { }

            // Re-counted straight away: the label described what a clear would remove, so leaving the
            // old number up made it look like the button had done nothing.
            UpdateCacheCount();
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS clear caches failed: " + ex.Message);
        }
    }
}
