using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MALPlus.Controls;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

public partial class MessagingPage : ContentPage, ITabSwipeHost
{
    private bool _initialized;
    private int _messagingTabIndex = 0;
    private MalMessagingViewModel Vm => (MalMessagingViewModel)BindingContext;

    public int MessagingTabIndex
    {
        get => _messagingTabIndex;
        set { _messagingTabIndex = value; OnPropertyChanged(); }
    }

    public MessagingPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.MalMessaging;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        SetMessagingTab(_messagingTabIndex);
        if (_initialized) return;
        _initialized = true;
        try
        {
            if (Vm == null)
            {
                Console.WriteLine("MALPLUS MessagingPage: VM is null!");
                return;
            }
            Vm.Init(false);
            for (int i = 0; i < 60; i++)
            {
                await Task.Delay(200);
                if (Vm.LoadingVisibility == false) break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS MessagingPage Init failed: " + ex);
        }
    }

    private void SetMessagingTab(int index)
    {
        _messagingTabIndex = index;
        OnPropertyChanged(nameof(MessagingTabIndex));
        if (TabInbox != null && TabSent != null)
        {
            TabChipHelper.ApplyStyle(TabInbox, index == 0);
            TabChipHelper.ApplyStyle(TabSent, index == 1);
        }
        if (Vm != null)
        {
            Vm.DisplaySentMessages = index == 1;
        }
    }

    private void OnTabButtonClicked(object sender, EventArgs e)
    {
        if (sender is Button b && b.CommandParameter is string s && int.TryParse(s, out int idx))
        {
            SetMessagingTab(idx);
        }
    }

    bool ITabSwipeHost.TrySwipe(int direction)
    {
        var current = _messagingTabIndex;
        var next = current + (direction > 0 ? 1 : -1);
        if (next < 0 || next > 1)
            return false;

        SetMessagingTab(next);
        return true;
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        if (Vm.LoadingVisibility) return;
        try { Vm.Init(true); } catch { }
    }

    private async void OnComposeClicked(object sender, EventArgs e)
    {
        try
        {
            await Shell.Current.GoToAsync("messagedetails?id=0&subject=");
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS compose failed: " + ex.Message);
        }
    }

    private async void OnMessageTapped(object sender, TappedEventArgs e)
    {
        try
        {
            if (e.Parameter is MalMessageModel msg)
            {
                var id = !string.IsNullOrEmpty(msg.Id) && msg.Id != "0" ? msg.Id : (msg.ThreadId ?? "0");
                var threadId = !string.IsNullOrEmpty(msg.ThreadId) ? msg.ThreadId : id;
                var subject = msg.Subject ?? "";
                var to = msg.Target ?? msg.Sender ?? "";
                await Shell.Current.GoToAsync(
                    $"messagedetails?id={Uri.EscapeDataString(id)}&threadid={Uri.EscapeDataString(threadId)}&subject={Uri.EscapeDataString(subject)}&to={Uri.EscapeDataString(to)}",
                    new Dictionary<string, object>
                    {
                        { "message", msg }
                    });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS message nav failed: " + ex.Message);
        }
    }
}
