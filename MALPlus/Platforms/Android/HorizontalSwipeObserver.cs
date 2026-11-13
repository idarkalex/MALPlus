using Android.App;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using AndroidX.ViewPager2.Widget;
using MALPlus.Controls;

namespace MALPlus;

internal static class HorizontalSwipeObserver
{
    private const string LogTag = "MALPlusSwipe";
    private const double MinTravelDp = 96;
    private const double HorizontalToVerticalRatio = 1.8;
    private const long MaxDurationMs = 700;
    private const double EdgeKeepOutDp = 32;
    private const double TopKeepOutDp = 32;
    private const double BottomKeepOutDp = 72;
    private const double PreviewStartSlopDp = 12;
    private const double PreviewHorizontalRatio = 1.3;

    private static bool _tracking;
    private static bool _cancelled;
    private static float _downX;
    private static float _downY;
    private static long _downTime;
    private static float _density = 1;
    private static ISwipePreviewHost _previewHost;
    private static bool _previewStarted;
    private static bool _previewUnavailable;
    private static bool _suppressChildren;

    public static bool Observe(Activity activity, MotionEvent e)
    {
        if (e == null)
            return false;

        try
        {
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    Begin(activity, e);
                    return false;
                case MotionEventActions.Move:
                    Track(e);
                    return _suppressChildren;
                case MotionEventActions.Up:
                {
                    var suppress = _suppressChildren;
                    End(e);
                    return suppress;
                }
                case MotionEventActions.PointerDown:
                    CancelPreview();
                    _cancelled = true;
                    return _suppressChildren;
                case MotionEventActions.Cancel:
                {
                    var suppress = _suppressChildren;
                    CancelPreview();
                    Reset();
                    return suppress;
                }
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn(LogTag, "observe failed: " + ex.GetType().Name);
            Reset();
        }

        return false;
    }

    private static void Begin(Activity activity, MotionEvent e)
    {
        Reset();
        _tracking = true;
        _downX = e.GetX();
        _downY = e.GetY();
        _downTime = e.EventTime;
        _density = activity?.Resources?.DisplayMetrics?.Density ?? 1;

        if (e.PointerCount > 1)
        {
            _cancelled = true;
            return;
        }

        var decor = activity?.Window?.DecorView;
        if (decor == null || decor.Width <= 0 || decor.Height <= 0)
        {
            _cancelled = true;
            return;
        }

        var edge = EdgeKeepOutDp * _density;
        var top = TopKeepOutDp * _density;
        var bottom = BottomKeepOutDp * _density;
        if (_downX < edge || _downX > decor.Width - edge || _downY < top || _downY > decor.Height - bottom)
        {
            _cancelled = true;
            return;
        }

        if (Microsoft.Maui.Controls.Shell.Current?.CurrentPage is not ITabSwipeHost)
        {
            _cancelled = true;
            return;
        }

        _previewHost = Microsoft.Maui.Controls.Shell.Current?.CurrentPage as ISwipePreviewHost;

        // A page above the roots is served by the shellitem pager, not by the section
        // pager: a drag there has nothing to swipe, and letting the host answer it moved
        // the root section behind the page. Stack depth is the whole test, so every
        // pushed page is covered instead of a list of them. The details preview keeps
        // its gesture; it is the one stacked page with a pager of its own to drive.
        if (_previewHost == null && IsAboveRoot())
        {
            _cancelled = true;
            return;
        }

        if (IsOnHorizontalScroller(decor, _downX, _downY))
            _cancelled = true;
    }

    private static bool IsAboveRoot()
        => (Microsoft.Maui.Controls.Shell.Current?.Navigation?.NavigationStack?.Count ?? 1) > 1;

    private static void Track(MotionEvent e)
    {
        if (!_tracking || _cancelled)
            return;
        if (e.PointerCount > 1)
        {
            CancelPreview();
            _cancelled = true;
            return;
        }

        var dxPx = e.GetX() - _downX;
        var dyPx = e.GetY() - _downY;

        if (_previewStarted)
        {
            _previewHost?.UpdateSwipePreview(ToDips(dxPx));
            return;
        }

        if (_previewHost == null || _previewUnavailable)
            return;

        var slop = PreviewStartSlopDp * _density;
        if (Math.Abs(dxPx) < slop || Math.Abs(dxPx) < Math.Abs(dyPx) * PreviewHorizontalRatio)
            return;

        var direction = dxPx < 0 ? 1 : -1;
        var started = false;
        try
        {
            started = _previewHost.BeginSwipePreview(direction);
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn(LogTag, "preview begin failed: " + ex);
        }

        if (!started)
        {
            _previewUnavailable = true;
            return;
        }

        _previewStarted = true;
        _suppressChildren = true;
        _previewHost.UpdateSwipePreview(ToDips(dxPx));
    }

    private static void End(MotionEvent e)
    {
        if (!_tracking)
        {
            Reset();
            return;
        }

        if (_previewStarted)
        {
            var host = _previewHost;
            var dxDip = ToDips(e.GetX() - _downX);
            var duration = Math.Max(1, e.EventTime - _downTime);
            var velocity = dxDip / (duration / 1000.0);
            if (!_cancelled && host != null)
            {
                Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                {
                    try
                    {
                        host.EndSwipePreview(dxDip, velocity);
                    }
                    catch (Exception ex)
                    {
                        Android.Util.Log.Warn(LogTag, "preview end failed: " + ex);
                    }
                });
            }

            Reset();
            return;
        }

        if (!_cancelled && _previewHost == null && e.PointerCount == 1 && !IsAboveRoot())
        {
            var dx = e.GetX() - _downX;
            var dy = e.GetY() - _downY;
            var duration = e.EventTime - _downTime;
            var minTravel = MinTravelDp * _density;
            var horizontal = Math.Abs(dx) >= minTravel
                             && Math.Abs(dx) >= Math.Abs(dy) * HorizontalToVerticalRatio
                             && duration <= MaxDurationMs;
            if (horizontal)
            {
                var host = Microsoft.Maui.Controls.Shell.Current?.CurrentPage as ITabSwipeHost;
                if (host != null)
                {
                    var direction = dx < 0 ? 1 : -1;
                    Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
                    {
                        try
                        {
                            host.TrySwipe(direction);
                        }
                        catch (Exception ex)
                        {
                            Android.Util.Log.Warn(LogTag, "swipe host failed: " + ex);
                        }
                    });
                }
            }
        }

        Reset();
    }

    private static void CancelPreview()
    {
        if (!_previewStarted)
            return;

        var host = _previewHost;
        _previewStarted = false;
        if (host == null)
            return;

        Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                host.EndSwipePreview(0, 0);
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn(LogTag, "preview cancel failed: " + ex);
            }
        });
    }

    private static double ToDips(float px)
        => px / Math.Max(0.1f, _density);

    private static bool IsOnHorizontalScroller(Android.Views.View decor, float x, float y)
        => FindHorizontalScroller(decor, x, y) != null;

    private static Android.Views.View FindHorizontalScroller(Android.Views.View view, float x, float y)
    {
        if (view == null || !view.IsShown)
            return null;

        var bounds = new Android.Graphics.Rect();
        if (!view.GetGlobalVisibleRect(bounds) || !bounds.Contains((int)x, (int)y))
            return null;

        if (view is HorizontalScrollView horizontal
            && (horizontal.CanScrollHorizontally(1) || horizontal.CanScrollHorizontally(-1)))
            return view;
        if (IsMauiHorizontalScroll(view))
            return view;
        if (view is ViewPager2 pager
            && pager.UserInputEnabled
            && (pager.CanScrollHorizontally(1) || pager.CanScrollHorizontally(-1)))
            return view;
        if (view is RecyclerView recycler
            && recycler.Parent is not ViewPager2
            && recycler.GetLayoutManager() is LinearLayoutManager manager
            && manager.Orientation == LinearLayoutManager.Horizontal
            && (recycler.CanScrollHorizontally(1) || recycler.CanScrollHorizontally(-1)))
            return view;

        if (view is ViewGroup group)
        {
            for (var i = group.ChildCount - 1; i >= 0; i--)
            {
                var found = FindHorizontalScroller(group.GetChildAt(i), x, y);
                if (found != null)
                    return found;
            }
        }

        return null;
    }

    private static bool IsMauiHorizontalScroll(Android.Views.View view)
    {
        var type = view.GetType();
        if (!type.Name.Contains("ScrollView", StringComparison.Ordinal))
            return false;

        var property = type.GetProperty(
            "Orientation",
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance);
        var value = property?.GetValue(view);
        return value != null && string.Equals(value.ToString(), "Horizontal", StringComparison.OrdinalIgnoreCase);
    }

    private static void Reset()
    {
        _tracking = false;
        _cancelled = false;
        _previewHost = null;
        _previewStarted = false;
        _previewUnavailable = false;
        _suppressChildren = false;
    }
}
