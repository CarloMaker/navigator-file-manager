using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Navigator.App;

/// <summary>Dove finirà l'elemento trascinato rispetto a quello sotto il mouse.</summary>
internal enum InsertionMode
{
    Above,
    Below,
    Into,
    Before,
    After,
}

/// <summary>
/// Segno colorato durante un trascinamento: una linea sopra o sotto l'elemento (inserimento prima o dopo)
/// oppure un riquadro intorno (rilascio dentro una cartella dei segnalibri).
/// </summary>
internal sealed class InsertionAdorner : Adorner
{
    private readonly Pen _pen;

    public InsertionAdorner(UIElement adornedElement, InsertionMode mode, Brush brush)
        : base(adornedElement)
    {
        Mode = mode;
        _pen = new Pen(brush, 2);
        _pen.Freeze();
        IsHitTestVisible = false;
    }

    public InsertionMode Mode { get; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = AdornedElement.RenderSize;
        if (Mode == InsertionMode.Into)
        {
            var rect = new Rect(1, 1, Math.Max(0, size.Width - 2), Math.Max(0, size.Height - 2));
            drawingContext.DrawRoundedRectangle(null, _pen, rect, 4, 4);
            return;
        }

        if (Mode is InsertionMode.Before or InsertionMode.After)
        {
            // Pannelli affiancati: linea verticale a sinistra o a destra.
            var x = Mode == InsertionMode.After ? Math.Max(1, size.Width - 1) : 1;
            drawingContext.DrawLine(_pen, new Point(x, 4), new Point(x, Math.Max(4, size.Height - 4)));
            return;
        }

        var y = Mode == InsertionMode.Below ? Math.Max(1, size.Height - 1) : 1;
        drawingContext.DrawLine(_pen, new Point(4, y), new Point(Math.Max(4, size.Width - 4), y));
    }
}
