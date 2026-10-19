using System;
using System.Threading.Tasks;
using MALPlus.XShared.NavArgs;
using MALPlus.XShared.ViewModels;
using MALPlus.XShared.ViewModels.Forums;

namespace MALPlus.Views;

[QueryProperty(nameof(TopicId), "id")]
public partial class ForumReplyPage : ContentPage
{
    private bool _initialized;
    public string TopicId { get; set; }

    private ForumTopicViewModel Vm => (ForumTopicViewModel)BindingContext;

    public ForumReplyPage()
    {
        InitializeComponent();
        BindingContext = ViewModelLocator.ForumsTopic;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_initialized)
            return;
        _initialized = true;
        try
        {
            if (Vm == null)
                return;
            Vm.ReplyMessage = Vm.ReplyMessage ?? string.Empty;
            if (Vm.CurrentTopicData == null && int.TryParse(TopicId, out _))
            {
                Vm.Init(new ForumsTopicNavigationArgs(null, null) { TopicId = TopicId });
                for (var i = 0; i < 60 && Vm.LoadingTopic; i++)
                    await Task.Delay(200);
            }
        }
        catch
        {
        }
    }

    private async void OnPostReplyTapped(object sender, EventArgs e)
    {
        try
        {
            if (Vm == null)
                return;
            if (await Vm.SendReplyAsync())
                await Shell.Current.GoToAsync("..");
        }
        catch
        {
        }
    }
}