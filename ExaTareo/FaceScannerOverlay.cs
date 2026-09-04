using System.Diagnostics;

namespace ExaTareo;

// Visual framing guide only: never changes face validation or implies a match.
public sealed class FaceScannerOverlay : GraphicsView, IDrawable
{
    private readonly Stopwatch clock = new();
    private readonly IDispatcherTimer timer;

    public FaceScannerOverlay()
    {
        Drawable = this;
        InputTransparent = true;
        BackgroundColor = Colors.Transparent;
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(33);
        timer.Tick += (_, _) => Invalidate();
        Unloaded += (_, _) => Stop();
    }

    public void Start() { clock.Restart(); timer.Start(); }
    public void Stop() { timer.Stop(); clock.Stop(); }

    public void Draw(ICanvas canvas, RectF bounds)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        float width = Math.Min(210, bounds.Width * .64f);
        float height = Math.Min(270, bounds.Height * .7f);
        var oval = new RectF((bounds.Width - width) / 2, (bounds.Height - height) / 2, width, height);
        canvas.SaveState();
        var mask = new PathF();
        mask.AppendRectangle(bounds);
        mask.AppendEllipse(oval);
        canvas.FillColor = Color.FromRgba(4, 14, 20, 105);
        canvas.FillPath(mask, WindingMode.EvenOdd);
        var green = Color.FromArgb("#2FBF71");
        for (int i = 4; i >= 1; i--)
        {
            canvas.StrokeColor = green.WithAlpha(.025f * (5 - i));
            canvas.StrokeSize = 3 + i * 3;
            canvas.DrawEllipse(oval);
        }
        canvas.StrokeColor = green.WithAlpha(.9f);
        canvas.StrokeSize = 3;
        canvas.DrawEllipse(oval);

        // HTML reference: 1.1 seconds in each direction, from 17% to 78% height.
        float phase = (float)(.5 - .5 * Math.Cos(clock.Elapsed.TotalSeconds * Math.PI / 1.1));
        float y = bounds.Height * (.17f + .61f * phase);
        float left = bounds.Width * .12f, lineWidth = bounds.Width * .76f;
        for (int i = 4; i >= 1; i--)
        {
            canvas.FillColor = green.WithAlpha(.04f);
            canvas.FillRectangle(left, y - i * 2, lineWidth, 3 + i * 4);
        }
        canvas.FillColor = green;
        canvas.FillRectangle(left, y, lineWidth, 3);
        canvas.RestoreState();
    }
}
