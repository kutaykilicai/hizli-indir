using System.Windows;
using System.Windows.Media;
using HizliIndir.Core;

namespace HizliIndir.UI;

/// <summary>
/// Dosyanın bölüm başlangıçlarını ve her bölümün inen kısmını tek şeritte çizer.
/// DrawingVisual kullanır; InvalidateVisual/layout tetiklemez, böylece sık güncelleme ucuz kalır.
/// </summary>
public sealed class SegmentBar : FrameworkElement
{
    readonly DrawingVisual _visual = new();
    SegmentState[] _segments = Array.Empty<SegmentState>();
    long _size;

    /// <summary>Bölüm başlangıç işaretleri (HTTP). Torrent parça haritasında kapalıdır.</summary>
    public bool ShowMarkers { get; set; } = true;

    public SegmentBar()
    {
        AddVisualChild(_visual);
        Height = 22;
        ClipToBounds = true;
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Paint(finalSize.Width, finalSize.Height);
        return finalSize;
    }

    public void Update(SegmentState[] segments, long size)
    {
        _segments = segments;
        _size = size;
        if (ActualWidth > 0) Paint(ActualWidth, ActualHeight);
    }

    void Paint(double w, double h)
    {
        if (w <= 0 || h <= 0) return;
        var track = TryBrush("Track") ?? new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C));
        var accent = TryBrush("Accent") ?? new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
        var active = new SolidColorBrush(Color.FromRgb(0x64, 0xD2, 0xFF)); active.Freeze();
        var marker = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)); marker.Freeze();
        var radius = Math.Min(6, h / 2);

        using var dc = _visual.RenderOpen();
        dc.DrawRoundedRectangle(track, null, new Rect(0, 0, w, h), radius, radius);
        if (_size <= 0 || _segments.Length == 0) return;

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h), radius, radius));
        foreach (var s in _segments)
        {
            double x0 = s.Start * w / _size;
            double xDone = Math.Min(s.Done, s.End + 1) * w / _size;
            double xEnd = (s.End + 1) * w / _size;
            if (xDone > x0)
                dc.DrawRectangle(accent, null, new Rect(x0, 0, Math.Max(1, xDone - x0), h));
            // Aktif uç nokta: ince açık mavi işaret
            if (s.Active && !s.Finished && xDone >= x0)
                dc.DrawRectangle(active, null, new Rect(Math.Max(0, xDone - 2), 0, 2, h));
            // Bölüm başlangıç işareti (IDM'deki kırmızı ayırıcı)
            if (ShowMarkers && s.Start > 0)
                dc.DrawRectangle(marker, null, new Rect(Math.Max(0, x0 - 0.5), 0, 1.5, h));
            _ = xEnd;
        }
        dc.Pop();
    }

    Brush? TryBrush(string key)
    {
        try { return FindResource(key) as Brush; } catch { return null; }
    }
}
