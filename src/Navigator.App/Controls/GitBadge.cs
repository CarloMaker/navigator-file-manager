using System.Windows;
using System.Windows.Media;
using Navigator.Core.Localization;
using Navigator.Core.Models;

namespace Navigator.App;

/// <summary>
/// Badge di stato Git disegnato sopra l'icona, come gli overlay di TortoiseGit:
/// spunta verde (aggiornato), punto esclamativo rosso (modificato), più blu (aggiunto),
/// punto interrogativo (non versionato), trattino grigio (ignorato), punto esclamativo arancione (conflitto).
/// </summary>
public sealed class GitBadge : FrameworkElement
{
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(GitItemStatus), typeof(GitBadge),
        new FrameworkPropertyMetadata(GitItemStatus.None, FrameworkPropertyMetadataOptions.AffectsRender, OnStatusChanged));

    private static readonly Brush Outline = Frozen(new SolidColorBrush(Colors.White));
    private static readonly Pen OutlinePen = Frozen(new Pen(Outline, 0.09));
    private static readonly Brush GreenFill = Frozen(new SolidColorBrush(Color.FromRgb(0x2E, 0x9E, 0x44)));
    private static readonly Brush RedFill = Frozen(new SolidColorBrush(Color.FromRgb(0xD9, 0x30, 0x25)));
    private static readonly Brush BlueFill = Frozen(new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0xEB)));
    private static readonly Brush SlateFill = Frozen(new SolidColorBrush(Color.FromRgb(0x5B, 0x6B, 0x8C)));
    private static readonly Brush GrayFill = Frozen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)));
    private static readonly Brush OrangeFill = Frozen(new SolidColorBrush(Color.FromRgb(0xF0, 0x8C, 0x00)));
    private static readonly Pen GlyphPen = Frozen(new Pen(Outline, 0.15) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

    private static readonly Geometry Check = Frozen(Geometry.Parse("M0.27,0.52 L0.44,0.68 L0.74,0.34"));
    private static readonly Geometry Exclamation = Frozen(Geometry.Parse("M0.5,0.24 L0.5,0.55"));
    private static readonly Geometry Plus = Frozen(Geometry.Parse("M0.5,0.26 L0.5,0.74 M0.26,0.5 L0.74,0.5"));
    private static readonly Geometry Minus = Frozen(Geometry.Parse("M0.28,0.5 L0.72,0.5"));
    private static readonly Geometry Question = Frozen(Geometry.Parse("M0.36,0.37 C0.36,0.2 0.66,0.2 0.65,0.37 C0.64,0.49 0.5,0.48 0.5,0.6"));

    public GitBadge()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = false;
    }

    public GitItemStatus Status
    {
        get => (GitItemStatus)GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    private static void OnStatusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var badge = (GitBadge)d;
        var status = (GitItemStatus)e.NewValue;
        badge.IsHitTestVisible = status != GitItemStatus.None;
        badge.ToolTip = status switch
        {
            GitItemStatus.Normal => Strings.GitNormal,
            GitItemStatus.Modified => Strings.GitModified,
            GitItemStatus.Added => Strings.GitAdded,
            GitItemStatus.Untracked => Strings.GitUntracked,
            GitItemStatus.Ignored => Strings.GitIgnored,
            GitItemStatus.Conflicted => Strings.GitConflicted,
            _ => null,
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (Status == GitItemStatus.None || size <= 0)
        {
            return;
        }

        var (fill, glyph, dot) = Status switch
        {
            GitItemStatus.Normal => (GreenFill, Check, false),
            GitItemStatus.Modified => (RedFill, Exclamation, true),
            GitItemStatus.Added => (BlueFill, Plus, false),
            GitItemStatus.Untracked => (SlateFill, Question, true),
            GitItemStatus.Ignored => (GrayFill, Minus, false),
            _ => (OrangeFill, Exclamation, true),
        };

        // Si disegna in un quadrato unitario, poi scalato alla dimensione del badge.
        dc.PushTransform(new ScaleTransform(size, size));
        dc.DrawEllipse(fill, OutlinePen, new Point(0.5, 0.5), 0.455, 0.455);
        dc.DrawGeometry(null, GlyphPen, glyph);
        if (dot)
        {
            dc.DrawEllipse(Outline, null, new Point(0.5, 0.75), 0.085, 0.085);
        }

        dc.Pop();
    }

    private static T Frozen<T>(T freezable)
        where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
