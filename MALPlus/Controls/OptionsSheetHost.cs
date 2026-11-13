using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MALPlus.Controls;
using Microsoft.Maui.Controls;
using MALPlus.Views;

namespace MALPlus;

/// <summary>
///     Raises the options sheet from anywhere, and hands back what was chosen.
/// </summary>
/// <remarks>
///     The two places that already use the sheet are pages that declare it in their own layout:
///     PosterSheet on the list, DetailsSheet on the details page. Those are fine as they are and are not
///     going through here. This is for every other page that needs a dialog and has nowhere to put one,
///     which until now meant a native alert or action sheet, and those cannot be styled at all.
///     <para>
///     Everything returns a Task, and that is the point of it. The call sites that read
///     <c>await DisplayActionSheet(...)</c> keep exactly the shape they have and the control flow around
///     them does not move.
///     </para>
///     <para>
///     One dialog at a time, held by a gate for the whole of its life. Two hosts pushing at the same
///     moment would interleave their pages and the caller waiting on whichever landed second would be
///     answered by whichever was dismissed first.
///     </para>
/// </remarks>
public static class OptionsSheetHost
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>
    ///     A list of choices. Returns the chosen row's label, or null if the dialog was dismissed.
    /// </summary>
    public static async Task<string> PickAsync(string title, string subtitle,
        IEnumerable<OptionsSheetItem> rows, string cancelLabel = "Cancel")
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new OptionsSheetPage();

        await Gate.WaitAsync();
        try
        {
            Bind(page, pending);
            // No cancel row: the close button at the top right already says "never mind", and a row
            // saying the same thing underneath it is one more thing to read past the choices.
            page.Sheet.OpenCompactWithSubtitle(title, subtitle, rows, cancelLabel);

            await PushAsync(page);
            return await pending.Task;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options host failed: " + ex.Message);
            await PopAsync(page);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     A list of choices with no title and no explanation, the way the poster long press reads.
    /// </summary>
    public static Task<string> PickCompactAsync(IEnumerable<OptionsSheetItem> rows)
        => PickAsync(null, null, rows, "Cancel");

    /// <summary>
    ///     A question with a commit and a cancel. Returns true only when the commit was chosen.
    /// </summary>
    public static async Task<bool> ConfirmAsync(string title, string message, string okLabel = "OK",
        bool destructive = false, string cancelLabel = null)
    {
        // A caller with a real word for each answer asks for it, and gets the two answers on one
        // row. Without one the shape stays what it has always been, because every existing caller
        // here is a single "OK" and a "Cancel" underneath it reads fine for that.
        if (!string.IsNullOrWhiteSpace(cancelLabel))
        {
            var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var page = new OptionsSheetPage();

            await Gate.WaitAsync();
            try
            {
                BindConfirm(page, answered);
                page.Sheet.OpenConfirm(title, message, okLabel, cancelLabel, destructive, () =>
                {
                    // Popped before the answer is handed over, same as Finish does, so a caller that
                    // navigates on the strength of it does not navigate with the dialog still on top.
                    if (answered.TrySetResult(true))
                        _ = PopAsync(page);
                });
                await PushAsync(page);
                return await answered.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine("MALPLUS options confirm failed: " + ex.Message);
                await PopAsync(page);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        var answer = await PickAsync(title, message, new[]
        {
            new OptionsSheetItem(okLabel, destructive ? "icon_close" : "icon_check", destructive: destructive)
        }, "Cancel");
        return answer == okLabel;
    }

    /// <summary>
    ///     Displays a release update dialog with changelog and actions to update or postpone.
    /// </summary>
    public static async Task<bool> ShowUpdateAsync(XShared.Comm.AppUpdateInfo info,
        Func<Action<double, string>, Task<bool>> onPerformUpdate = null)
    {
        if (info == null)
            return false;

        var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new OptionsSheetPage();

        await Gate.WaitAsync();
        try
        {
            BindConfirm(page, answered);
            page.Sheet.OpenUpdate(
                $"New version available (v{info.Version})",
                info.ReleaseName,
                info.ReleaseNotes,
                "Update",
                "Later",
                onPerformUpdate: onPerformUpdate != null ? async (report) =>
                {
                    try
                    {
                        var ok = await onPerformUpdate(report);
                        answered.TrySetResult(ok);
                        _ = PopAsync(page);
                        return ok;
                    }
                    catch
                    {
                        answered.TrySetResult(false);
                        throw;
                    }
                } : null,
                onConfirm: () =>
                {
                    if (answered.TrySetResult(true))
                        _ = PopAsync(page);
                },
                onCancel: () =>
                {
                    if (answered.TrySetResult(false))
                        _ = PopAsync(page);
                });

            await PushAsync(page);
            return await answered.Task;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ShowUpdateAsync failed: " + ex.Message);
            await PopAsync(page);
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Displays release notes or changelog in a scrollable card.
    /// </summary>
    public static async Task ShowChangelogAsync(string title, string releaseName, string notes)
    {
        var answered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new OptionsSheetPage();

        await Gate.WaitAsync();
        try
        {
            BindConfirm(page, answered);
            page.Sheet.OpenUpdate(
                title,
                releaseName,
                notes,
                "Got it",
                null,
                onConfirm: () =>
                {
                    if (answered.TrySetResult(true))
                        _ = PopAsync(page);
                });

            await PushAsync(page);
            await answered.Task;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS ShowChangelogAsync failed: " + ex.Message);
            await PopAsync(page);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     A field to type into. Returns null for a cancel and an empty string for a commit on an empty
    ///     field, because "leave it blank to clear it" is a real answer.
    /// </summary>
    public static async Task<string> PromptAsync(string title, string subtitle, string initial, string hint,
        int maxLength = 6, Keyboard? keyboard = null)
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new OptionsSheetPage();

        await Gate.WaitAsync();
        try
        {
            Bind(page, pending);
            // The callbacks are what carry the answer, so the panel needs no event wiring of its own: the
            // commit row hands over what is in the field at the moment it is tapped, and the close button
            // is what abandoning it looks like.
            page.Sheet.OpenPrompt(title, subtitle, initial, hint, maxLength,
                true,
                value => Finish(page, pending, value), () => Finish(page, pending, null),
                keyboard: keyboard);
            await PushAsync(page);
            return await pending.Task;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options prompt failed: " + ex.Message);
            await PopAsync(page);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     A list of choices that owns its own sub levels.
    /// </summary>
    /// <remarks>
    ///     Same as <see cref="PickAsync" /> but hands the caller the sheet once it is up. The random
    ///     filters need it: their rows open submenus, and the back row on a submenu comes back as a plain
    ///     Action with no sheet attached, so the only way for it to redraw the level above is to have kept
    ///     a reference.
    /// </remarks>
    public static async Task<string> PickAsync(string title, string subtitle,
        IEnumerable<OptionsSheetItem> rows, string cancelLabel, Action<OptionsSheet> onOpened)
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var page = new OptionsSheetPage();

        await Gate.WaitAsync();
        try
        {
            Bind(page, pending);
            page.Sheet.OpenCompactWithSubtitle(title, subtitle, rows, cancelLabel);
            onOpened?.Invoke(page.Sheet);

            await PushAsync(page);
            return await pending.Task;
        }
        catch (Exception ex)
        {
            Console.WriteLine("MALPLUS options host failed: " + ex.Message);
            await PopAsync(page);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    ///     Choices that can be turned on and off, staying open for as many taps as the reader wants.
    /// </summary>
    /// <remarks>
    ///     The genre picker, which reopened an action sheet after every tap in a loop, so each pick closed
    ///     and reopened the panel and flickered. A row here marks itself and asks for a redraw, which the
    ///     submenu hook is already built for: it replaces the panel's contents and leaves it open, so the
    ///     check marks follow the taps.
    /// </remarks>
    public static async Task<List<int>> PickMultiAsync(string title, string subtitle,
        IEnumerable<int> ids, Func<int, string> label, IEnumerable<int> selected, string doneLabel = "Done")
    {
        var all = (ids ?? Enumerable.Empty<int>()).ToList();
        var chosen = (selected ?? Enumerable.Empty<int>()).ToList();

        // Built through itself rather than held in a variable, so the redraw below sees the new selection
        // without anything having to reach in and clear the panel.
        Func<List<OptionsSheetItem>> build = null;
        build = () => all.Select(id =>
        {
            var on = chosen.Contains(id);
            return new OptionsSheetItem(
                label(id),
                on ? "icon_check" : null,
                submenu: sheet =>
                {
                    // Marks rather than commits, then asks for the panel to be drawn again from the new
                    // selection. Returning true leaves it open, which is what a pick of several needs.
                    if (!chosen.Remove(id))
                        chosen.Add(id);
                    sheet.OpenCompactWithSubtitle(title, subtitle, build(), null);
                    return true;
                });
        }).Append(new OptionsSheetItem(doneLabel, "icon_check")).ToList();

        var picked = await PickAsync(title, subtitle, build(), null);
        return picked == doneLabel ? chosen : null;
    }

    private static void Bind(OptionsSheetPage page, TaskCompletionSource<string> pending)
    {
        page.Bind();
        page.RowChosen += (_, item) => Finish(page, pending, item?.Label);
        page.Dismissed += (_, _) => Finish(page, pending, null);
    }

    /// <summary>
    ///     The same wiring for a question that answers itself: the commit carries the true, and the
    ///     panel closing without a choice is the false.
    /// </summary>
    private static void BindConfirm(OptionsSheetPage page, TaskCompletionSource<bool> pending)
    {
        page.Bind();
        page.Dismissed += (_, _) =>
        {
            if (pending.TrySetResult(false))
                _ = PopAsync(page);
        };
    }

    private static void Finish(OptionsSheetPage page, TaskCompletionSource<string> pending, string result)
    {
        // Popped before the answer is handed over, and the order matters. A caller that navigates on the
        // strength of an answer would otherwise navigate with the dialog still on top, which is exactly
        // the Discover random picker: it picks an entry and opens it.
        if (pending.TrySetResult(result))
            _ = PopAsync(page);
    }

    private static Task PushAsync(OptionsSheetPage page)
    {
        var navigation = Shell.Current?.Navigation;
        return navigation == null ? Task.CompletedTask : navigation.PushModalAsync(page, false);
    }

    private static Task PopAsync(OptionsSheetPage page)
    {
        var navigation = Shell.Current?.Navigation;
        if (navigation == null)
            return Task.CompletedTask;
        if (navigation.ModalStack.Count == 0)
            return Task.CompletedTask;
        return navigation.PopModalAsync(false);
    }
}
