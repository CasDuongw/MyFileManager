using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FM.App.Helpers;

/// <summary>
/// A lightweight, GPU-accelerated WPF Adorner that renders a floating visual preview
/// directly under the cursor during Drag & Drop operations, mimicking modern OS tactile drag physics.
/// </summary>
public class DragAdorner : Adorner
{
    private readonly UIElement _child;
    private double _leftOffset;
    private double _topOffset;

    [StructLayout(LayoutKind.Sequential)]
    private struct Win32Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Win32Point lpPoint);

    public DragAdorner(UIElement adornedElement, UIElement child)
        : base(adornedElement)
    {
        _child = child ?? throw new ArgumentNullException(nameof(child));
        AddVisualChild(_child);
        AddLogicalChild(_child);
        IsHitTestVisible = false;
    }

    /// <summary>
    /// Updates the position of the adorner based on the physical screen cursor position.
    /// </summary>
    public void UpdatePosition(double cursorOffsetX = 10, double cursorOffsetY = 10)
    {
        if (AdornedElement == null || PresentationSource.FromVisual(AdornedElement) == null)
        {
            return;
        }

        if (GetCursorPos(out var win32Pt))
        {
            try
            {
                var screenPoint = new Point(win32Pt.X, win32Pt.Y);
                var elementPoint = AdornedElement.PointFromScreen(screenPoint);

                _leftOffset = elementPoint.X + cursorOffsetX;
                _topOffset = elementPoint.Y + cursorOffsetY;

                var layer = AdornerLayer.GetAdornerLayer(AdornedElement);
                layer?.Update();
            }
            catch
            {
                // Ignored if element is detaching or window minimizing
            }
        }
    }

    /// <summary>
    /// Sets an explicit coordinate offset relative to the adorned element.
    /// Useful for programmatic layout and headless unit tests.
    /// </summary>
    public void SetExplicitPosition(double left, double top)
    {
        _leftOffset = left;
        _topOffset = top;
        var layer = AdornerLayer.GetAdornerLayer(AdornedElement);
        layer?.Update();
    }

    public double LeftOffset => _leftOffset;
    public double TopOffset => _topOffset;

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index)
    {
        if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
        return _child;
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return _child.DesiredSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _child.Arrange(new Rect(_child.DesiredSize));
        return _child.DesiredSize;
    }

    public override GeneralTransform GetDesiredTransform(GeneralTransform transform)
    {
        var group = new GeneralTransformGroup();
        var baseTransform = base.GetDesiredTransform(transform);
        if (baseTransform != null)
        {
            group.Children.Add(baseTransform);
        }
        group.Children.Add(new TranslateTransform(_leftOffset, _topOffset));
        return group;
    }
}
