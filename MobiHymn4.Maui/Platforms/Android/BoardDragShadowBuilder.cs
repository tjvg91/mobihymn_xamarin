using Android.Graphics;
using Android.Views;
using Point = Android.Graphics.Point;
using AView = Android.Views.View;

namespace MobiHymn4.Platforms.Android;

/// <summary>
/// Keeps the drag phantom under the finger at the grab point so left-aligned
/// section rows don't snap to Android's default center hotspot.
/// </summary>
public sealed class BoardDragShadowBuilder : AView.DragShadowBuilder
{
    readonly int touchX;
    readonly int touchY;
    readonly bool hasTouch;

    public BoardDragShadowBuilder(AView view, MotionEvent motionEvent)
        : base(view)
    {
        hasTouch = motionEvent != null;
        if (hasTouch)
        {
            touchX = Math.Max(0, (int)motionEvent.GetX());
            touchY = Math.Max(0, (int)motionEvent.GetY());
        }
        else
        {
            // Fall back to the drag-handle side, not the view center.
            touchX = Math.Min(36, Math.Max(16, (view?.Width ?? 72) / 24));
            touchY = Math.Max(0, (view?.Height ?? 0) / 2);
        }
    }

    public override void OnProvideShadowMetrics(Point shadowSize, Point shadowTouchPoint)
    {
        var view = View;
        if (view == null)
        {
            base.OnProvideShadowMetrics(shadowSize, shadowTouchPoint);
            return;
        }

        shadowSize.Set(view.Width, view.Height);

        var x = hasTouch ? touchX : Math.Min(36, Math.Max(16, view.Width / 24));
        var y = hasTouch ? touchY : view.Height / 2;
        shadowTouchPoint.Set(
            Math.Min(x, Math.Max(0, view.Width - 1)),
            Math.Min(y, Math.Max(0, view.Height - 1)));
    }
}
