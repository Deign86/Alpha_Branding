using System.Windows;

namespace Alpha.Branding.Models;

public sealed class RedactStroke
{
    private double _brushSizeNormalized = 0.02;

    public List<Point> Points { get; } = new();

    public double BrushSizeNormalized
    {
        get => _brushSizeNormalized;
        set
        {
            if (double.IsNaN(value) || value <= 0)
            {
                throw new ArgumentException("Brush size must be greater than zero.", nameof(value));
            }

            _brushSizeNormalized = value;
        }
    }

    public byte R { get; set; }

    public byte G { get; set; }

    public byte B { get; set; }

    public void AddPoint(Point point)
    {
        Points.Add(ClampPoint(point));
    }

    public void Reset()
    {
        Points.Clear();
        BrushSizeNormalized = 0.02;
        R = 0;
        G = 0;
        B = 0;
    }

    public RedactStroke Clone()
    {
        var clone = new RedactStroke
        {
            BrushSizeNormalized = BrushSizeNormalized,
            R = R,
            G = G,
            B = B
        };
        clone.CopyPointsFrom(this);
        return clone;
    }

    public void CopyFrom(RedactStroke source)
    {
        ArgumentNullException.ThrowIfNull(source);
        BrushSizeNormalized = source.BrushSizeNormalized;
        R = source.R;
        G = source.G;
        B = source.B;
        CopyPointsFrom(source);
    }

    private void CopyPointsFrom(RedactStroke source)
    {
        Points.Clear();
        foreach (var point in source.Points)
        {
            Points.Add(ClampPoint(point));
        }
    }

    private static Point ClampPoint(Point point)
    {
        return new Point(Math.Clamp(point.X, 0, 1), Math.Clamp(point.Y, 0, 1));
    }
}

public static class RedactStrokeCollection
{
    public static List<RedactStroke> Clone(IEnumerable<RedactStroke>? strokes)
    {
        if (strokes is null)
        {
            return new List<RedactStroke>();
        }

        var result = new List<RedactStroke>();
        foreach (var stroke in strokes)
        {
            if (stroke is not null)
            {
                result.Add(stroke.Clone());
            }
        }

        return result;
    }
}
