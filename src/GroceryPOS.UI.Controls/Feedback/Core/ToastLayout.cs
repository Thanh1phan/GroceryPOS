using System.Drawing;

namespace GroceryPOS.UI.Controls.Feedback.Core;

/// <summary>Corner (or edge centre) of the window where toasts stack.</summary>
public enum ToastPosition
{
    BottomRight,
    TopRight,
    BottomCenter,
    TopCenter,
}

/// <summary>Places a stack of toasts inside a window's client area. Pure geometry, in pixels.</summary>
public static class ToastLayout
{
    /// <summary>Toast width: <paramref name="preferred"/>, narrowed to fit small windows but never below <paramref name="minimum"/>.</summary>
    public static int Width(int clientWidth, int preferred, int margin, int minimum = 200) =>
        Math.Max(minimum, Math.Min(preferred, clientWidth - 2 * margin));

    /// <summary>
    /// Bounds for each toast in <paramref name="sizes"/> (oldest first). The newest sits closest to the
    /// anchored edge and older ones are pushed away from it. Toasts that no longer fit get
    /// <see cref="Rectangle.Empty"/> and should be hidden until room frees up.
    /// </summary>
    public static IReadOnlyList<Rectangle> Arrange(Size client, IReadOnlyList<Size> sizes, ToastPosition position, int margin, int gap)
    {
        ArgumentNullException.ThrowIfNull(sizes);
        var result = new Rectangle[sizes.Count];
        var fromBottom = position is ToastPosition.BottomRight or ToastPosition.BottomCenter;
        var centered = position is ToastPosition.TopCenter or ToastPosition.BottomCenter;
        var cursor = fromBottom ? client.Height - margin : margin;

        for (var i = sizes.Count - 1; i >= 0; i--)
        {
            var size = sizes[i];
            var x = centered ? (client.Width - size.Width) / 2 : client.Width - margin - size.Width;
            int y;
            if (fromBottom)
            {
                y = cursor - size.Height;
                if (y < margin)
                {
                    break;
                }

                cursor = y - gap;
            }
            else
            {
                y = cursor;
                if (y + size.Height > client.Height - margin)
                {
                    break;
                }

                cursor = y + size.Height + gap;
            }

            result[i] = new Rectangle(Math.Max(margin, x), y, size.Width, size.Height);
        }

        return result;
    }
}
