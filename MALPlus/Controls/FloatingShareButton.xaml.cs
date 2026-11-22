using System;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Ioc;
using MALPlus.Adapters;
using MALPlus.XShared.ViewModels;
using Microsoft.Maui.Controls;

namespace MALPlus.Controls;

public partial class FloatingShareButton : ContentView
{
    private bool _isSubscribed;

    public FloatingShareButton()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, EventArgs e)
    {
        Subscribe();
    }

    private void OnUnloaded(object sender, EventArgs e)
    {
        Unsubscribe();
    }

    public void Subscribe()
    {
        if (_isSubscribed)
            return;

        try
        {
            var manager = ResourceLocator.ShareManager;
            if (manager != null)
            {
                manager.TimerStateChanged += OnTimerStateChanged;
                _isSubscribed = true;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS FloatingShareButton Subscribe failed: " + ex.Message);
        }
    }

    public void Unsubscribe()
    {
        if (!_isSubscribed)
            return;

        try
        {
            var manager = ResourceLocator.ShareManager;
            if (manager != null)
            {
                manager.TimerStateChanged -= OnTimerStateChanged;
            }
        }
        catch { }
        finally
        {
            _isSubscribed = false;
        }
    }

    private void OnTimerStateChanged(object sender, bool isRunning)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (isRunning)
            {
                await ShowAsync();
            }
            else
            {
                await HideAsync();
            }
        });
    }

    private async Task ShowAsync()
    {
        if (!MALPlus.XShared.Utils.Settings.EnableShareButton)
            return;

        IsVisible = true;
        TranslationY = 20;
        Opacity = 0;
        await Task.WhenAll(
            this.FadeTo(1, 250, Easing.CubicOut),
            this.TranslateTo(0, 0, 250, Easing.CubicOut)
        );
    }

    private async Task HideAsync()
    {
        if (!IsVisible)
            return;

        try
        {
            await Task.WhenAll(
                this.FadeTo(0, 200, Easing.CubicIn),
                this.TranslateTo(0, 20, 200, Easing.CubicIn)
            );
        }
        finally
        {
            IsVisible = false;
        }
    }

    private void OnShareTapped(object sender, EventArgs e)
    {
        try
        {
            var msg = ResourceLocator.ShareManager.GenerateMessage();
            if (!string.IsNullOrEmpty(msg))
            {
                var provider = SimpleIoc.Default.GetInstance<IShareProvider>();
                provider?.Share(msg);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS FloatingShareButton OnShareTapped failed: " + ex.Message);
        }
        finally
        {
            ResourceLocator.ShareManager.Dismiss();
        }
    }

    private void OnDismissTapped(object sender, EventArgs e)
    {
        ResourceLocator.ShareManager.Dismiss();
    }
}
