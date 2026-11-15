using System;
using System.Threading.Tasks;
using MALPlus.Controls;
using MALPlus.Models.Enums;
using MALPlus.Models.Models.Notifications;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

public partial class NotificationsHubPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private int _activeTab;
    private NotificationsHubViewModel Vm => (NotificationsHubViewModel)BindingContext;

    private static readonly MalNotificationsTypes?[] TabTypes = new MalNotificationsTypes?[]
    {
        null,
        MalNotificationsTypes.NowAiring,
        MalNotificationsTypes.ForumQuoute | MalNotificationsTypes.UserMentions | MalNotificationsTypes.WatchedTopics | MalNotificationsTypes.WatchedTopic,
        MalNotificationsTypes.Messages,
        MalNotificationsTypes.Generic | MalNotificationsTypes.FriendRequest | MalNotificationsTypes.FriendRequestAcceptDeny | MalNotificationsTypes.ProfileComment | MalNotificationsTypes.BlogComment | MalNotificationsTypes.ClubMessages | MalNotificationsTypes.NewRelatedAnime | MalNotificationsTypes.Payment
    };

    public NotificationsHubPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.NotificationsHub;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SetNotificationTab(_activeTab);
        if (_initialized) return;
        _initialized = true;
        try
        {
            Vm.Init(false);
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (!Vm.Loading) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS NotificationsHubPage Init failed: " + ex.Message);
        }
    }

    public void SetNotificationTab(int index)
    {
        if (index < 0 || index >= TabTypes.Length) return;
        _activeTab = index;

        Button[] buttons = { TabAll, TabAiring, TabReplies, TabMessages, TabOthers };
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] != null)
            {
                TabChipHelper.ApplyStyle(buttons[i], i == index);
            }
        }

        try
        {
            if (Vm != null)
                Vm.CurrentNotificationType = TabTypes[index];
        }
        catch { }
    }

    private void OnTabButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string s && int.TryParse(s, out int idx))
        {
            SetNotificationTab(idx);
        }
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        var next = _activeTab + (direction > 0 ? 1 : -1);
        if (next < 0 || next >= TabTypes.Length)
            return false;

        SetNotificationTab(next);
        return true;
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            if (Vm == null || Vm.Loading) return;
            Vm.Init(true);
        }
        catch { }
    }

    private async void OnNotificationItemTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is MalNotification notif)
            {
                // Mark as read first.
                try { Vm.MarkAsReadComand?.Execute(notif); } catch { }
                // Then navigate via the VM command (parses SanitizedLaunchArgs to
                // anime/manga/forum/messages routes).
                try { Vm.NavigateNotificationCommand?.Execute(notif); } catch { }
                await Task.Delay(200);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS notification tap failed: " + ex.Message);
        }
    }
}
