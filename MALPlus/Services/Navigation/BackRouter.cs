using System;
using MALPlus.Models.Enums;
using MALPlus.XShared.ViewModels;

namespace MALPlus.Services.Navigation;

/// <summary>
///     Where the back gesture goes from a page that was opened from somewhere specific.
/// </summary>
/// <remarks>
///     The registration that already existed (the NavMgr stack) was never consulted on Android: the
///     gesture is handled by the platform's back dispatcher, not by anything the app wrote, and the
///     MauiNavMgr it feeds has no caller at all. Pages therefore registered a destination and then it
///     was simply never used, which is why backing out of a page opened from More dropped the user
///     somewhere other than More.
///     <para>
///     This is a single destination rather than a stack, and that is deliberate. A stack would need every
///     page to know what to pop, and the pages that actually have a "go back to X" are the ones opened
///     from the hub, which is exactly one hop. Backing out of a details page opened from a list goes back
///     to the list, and the list does not register anything, so nothing is lost.
///     </para>
/// </remarks>
public static class BackRouter
{
    private static object _target;
    private static object _args;

    /// <summary>
    ///     Where the next back press should go, if anything.
    /// </summary>
    public static bool HasTarget => _target != null;

    /// <summary>
    ///     Says where back should go from the page currently being shown.
    /// </summary>
    public static void Register(object target, object args = null)
    {
        _target = target;
        _args = args;
    }

    public static void Clear()
    {
        _target = null;
        _args = null;
    }

    /// <summary>
    ///     Navigates to the registered page and consumes the registration.
    /// </summary>
    /// <returns>
    ///     False when nothing was registered, which is the signal for the caller to let the platform's
    ///     own back handling take over.
    /// </returns>
    public static bool TryGoBack()
    {
        if (_target == null)
            return false;

        var target = _target;
        var args = _args;
        Clear();

        try
        {
            // A shell-level route is handled by the Shell itself rather than by the main view model:
            // it has to swap the visible tab, and the main model's mapping of PageMore does that.
            if (target is PageIndex index)
            {
                ViewModelLocator.GeneralMain.Navigate(index, args);
                return true;
            }
        }
        catch (Exception)
        {
            // Might not have been usable; fall through to letting the platform decide.
        }

        return false;
    }
}
