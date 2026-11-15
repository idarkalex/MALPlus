using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MALPlus.Models.Models;
using MALPlus.Models.Models.MalSpecific;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Main;

namespace MALPlus.Views;

[QueryProperty(nameof(ThreadId), "id")]
[QueryProperty(nameof(ExplicitThreadId), "threadid")]
[QueryProperty(nameof(Subject), "subject")]
[QueryProperty(nameof(ToUser), "to")]
[QueryProperty(nameof(Comments), "comments")]
public partial class MessageDetailsPage : ContentPage, IQueryAttributable
{
    private string _lastLoadedKey;
    private int _loadSequence;

    public string ThreadId { get; set; }
    public string ExplicitThreadId { get; set; }
    public string Subject { get; set; }
    public string ToUser { get; set; }
    public string Comments { get; set; }
    public MalMessageModel PassedMessage { get; set; }

    private MalMessageDetailsViewModel Vm => (MalMessageDetailsViewModel)BindingContext;

    public MessageDetailsPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.MalMessageDetails;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var idVal)) ThreadId = idVal?.ToString();
        if (query.TryGetValue("threadid", out var tVal)) ExplicitThreadId = tVal?.ToString();
        if (query.TryGetValue("subject", out var sVal)) Subject = sVal?.ToString();
        if (query.TryGetValue("to", out var toVal)) ToUser = toVal?.ToString();
        if (query.TryGetValue("comments", out var cVal)) Comments = cVal?.ToString();
        if (query.TryGetValue("message", out var msgVal) && msgVal is MalMessageModel m)
        {
            PassedMessage = m;
        }

        ScheduleLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScheduleLoad();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _lastLoadedKey = null;
    }

    private void ScheduleLoad()
    {
        var id = !string.IsNullOrEmpty(ThreadId) ? ThreadId : PassedMessage?.Id;
        if (string.IsNullOrEmpty(id) && string.IsNullOrEmpty(ToUser) && string.IsNullOrEmpty(Subject))
            return;

        var key = $"{id}|{Subject}";
        if (_lastLoadedKey == key)
            return;
        _lastLoadedKey = key;

        var currentSeq = ++_loadSequence;
        Dispatcher.Dispatch(() =>
        {
            if (currentSeq != _loadSequence)
                return;
            ExecuteLoad();
        });
    }

    private void ExecuteLoad()
    {
        try
        {
            var subject = Uri.UnescapeDataString(Subject ?? "");
            if (Vm != null)
                Vm.MessageSubject = subject;

            if (Comments == "1")
            {
                Vm.Init(new MalMessageDetailsNavArgs
                {
                    WorkMode = MessageDetailsWorkMode.ProfileComments,
                    Arg = new MalComment
                    {
                        ComToCom = ThreadId,
                        User = new MalUser { Name = Uri.UnescapeDataString(ToUser ?? "") }
                    }
                }, force: true);
                return;
            }

            if (string.IsNullOrEmpty(ThreadId) || ThreadId == "0")
            {
                Vm.NewMessageFieldsVisibility = true;
                if (!string.IsNullOrEmpty(ToUser))
                    Vm.MessageTarget = Uri.UnescapeDataString(ToUser);
                Vm.Init(new MalMessageDetailsNavArgs
                {
                    WorkMode = MessageDetailsWorkMode.Message,
                    Arg = (MalMessageModel)null,
                    NewMessageTarget = Vm.MessageTarget
                }, force: true);
            }
            else
            {
                var msg = PassedMessage ?? new MalMessageModel
                {
                    Id = ThreadId,
                    ThreadId = !string.IsNullOrEmpty(ExplicitThreadId) ? ExplicitThreadId : ThreadId,
                    Subject = subject,
                    Target = Uri.UnescapeDataString(ToUser ?? "")
                };
                if (!string.IsNullOrEmpty(subject))
                    msg.Subject = subject;

                Vm.Init(new MalMessageDetailsNavArgs
                {
                    WorkMode = MessageDetailsWorkMode.Message,
                    Arg = msg
                }, force: true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS MessageDetailsPage ExecuteLoad failed: " + ex.Message);
        }
    }

    private void OnRefreshing(object sender, EventArgs e)
    {
        try
        {
            if (Vm == null || Vm.LoadingVisibility) return;
            if (string.IsNullOrEmpty(ThreadId) || ThreadId == "0") return;
            var msg = PassedMessage ?? new MalMessageModel
            {
                Id = ThreadId,
                ThreadId = !string.IsNullOrEmpty(ExplicitThreadId) ? ExplicitThreadId : ThreadId,
                Subject = Uri.UnescapeDataString(Subject ?? ""),
                Target = Uri.UnescapeDataString(ToUser ?? "")
            };
            Vm.Init(new MalMessageDetailsNavArgs
            {
                WorkMode = MessageDetailsWorkMode.Message,
                Arg = msg
            }, force: true);
        }
        catch { }
    }

    private async void OnBackTapped(object sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync(".."); } catch { }
    }
}
