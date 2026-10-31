using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FM.App.Helpers;

namespace FM.App.Tests;

public class DragAdornerTests
{
    private static void RunOnSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                ex = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (ex != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
        }
    }

    [Fact]
    public void DragAdorner_ConstructsCorrectly_AndSetsHitTestFalse()
    {
        RunOnSta(() =>
        {
            var parent = new Grid();
            var child = new Border();

            var adorner = new DragAdorner(parent, child);

            Assert.False(adorner.IsHitTestVisible);
            Assert.Equal(0, adorner.LeftOffset);
            Assert.Equal(0, adorner.TopOffset);
        });
    }

    [Fact]
    public void DragAdorner_SetExplicitPosition_UpdatesOffsets()
    {
        RunOnSta(() =>
        {
            var parent = new Grid();
            var child = new Border();
            var adorner = new DragAdorner(parent, child);

            adorner.SetExplicitPosition(120.5, 85.0);

            Assert.Equal(120.5, adorner.LeftOffset);
            Assert.Equal(85.0, adorner.TopOffset);
        });
    }

    [Fact]
    public void DragAdorner_GetDesiredTransform_IncludesTranslation()
    {
        RunOnSta(() =>
        {
            var parent = new Grid();
            var child = new Border();
            var adorner = new DragAdorner(parent, child);

            adorner.SetExplicitPosition(50, 100);

            var transform = adorner.GetDesiredTransform(Transform.Identity);
            Assert.NotNull(transform);
            var point = transform.Transform(new Point(0, 0));
            Assert.Equal(50, point.X);
            Assert.Equal(100, point.Y);
        });
    }

    [Fact]
    public void DragAdorner_UpdatePosition_WhenDetached_DoesNotThrow()
    {
        RunOnSta(() =>
        {
            var parent = new Grid();
            var child = new Border();
            var adorner = new DragAdorner(parent, child);

            // AdornedElement has no PresentationSource in headless mode; must safely return without throwing
            adorner.UpdatePosition(10, 10);
        });
    }
}
