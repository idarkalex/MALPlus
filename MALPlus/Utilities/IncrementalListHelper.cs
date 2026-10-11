using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MALPlus.Utilities;

public static class IncrementalListHelper
{
    /// <summary>
    ///     Appends <paramref name="source" /> to <paramref name="target" /> in batches, yielding
    ///     between them so a long list does not build one enormous frame.
    ///
    ///     The collection is always mutated on the UI thread, via BeginInvokeOnMainThread. It used
    ///     to be mutated straight from the Task.Run worker, which is the one thing you cannot do to
    ///     an ObservableCollection that a CollectionView is bound to: the view enumerates it while
    ///     the background thread is adding, and that either throws or leaves the visual tree with
    ///     fewer rows than the collection claims to have.
    /// </summary>
    public static CancellationTokenSource Drain<TSource, TTarget>(
        IEnumerable<TSource> source,
        ObservableCollection<TTarget> target,
        Func<TSource, TTarget> selector,
        int batchSize = 20,
        int delayMs = 50)
    {
        var cts = new CancellationTokenSource();
        var sourceList = source.ToList();

        Task.Run(async () =>
        {
            for (var i = 0; i < sourceList.Count; i += batchSize)
            {
                if (cts.Token.IsCancellationRequested)
                    break;

                var batch = sourceList.Skip(i).Take(batchSize).ToList();
                var added = 0;

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    foreach (var item in batch)
                    {
                        target.Add(selector(item));
                        added++;
                    }
                });

                // A selector that throws must not take the whole drain down with an unobserved
                // task exception; the rows that did land stay, and the loop moves on.
                if (added == 0)
                    break;

                try
                {
                    await Task.Delay(delayMs, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }, cts.Token);

        return cts;
    }
}