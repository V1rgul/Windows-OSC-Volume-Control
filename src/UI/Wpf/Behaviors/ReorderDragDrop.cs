using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace WindowsOscVolumeControl.UI.Wpf.Behaviors;

internal static class ReorderDragDrop {
	internal const string reorderDragFormat = "WindowsOscVolumeControl.ReorderItem";

	internal static int dropIndexToMoveIndex(int oldIndex, int dropIndex, int count) {
		int idx = Math.Clamp(dropIndex, 0, count);
		int moveIdx = idx > oldIndex ? idx - 1 : idx;
		return Math.Clamp(moveIdx, 0, Math.Max(0, count - 1));
	}

	internal static int computeDropIndex(ItemsControl list, Point p, object dragged, out double insertionLineY) {
		insertionLineY = 0d;
		int n = list.Items.Count;
		if (n <= 0)
			return 0;

		int draggedIndex = list.Items.IndexOf(dragged);

		for (int i = 0; i < n; i++) {
			if (list.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
				continue;
			Point topLeft = container.TranslatePoint(new Point(0, 0), list);
			double height = container.ActualHeight;
			double midY = topLeft.Y + height / 2d;

			// Treat the dragged item's placeholder like any other item:
			// - hover top half  => line at top (insert before)
			// - hover bottom half => line at bottom (insert after)
			if (i == draggedIndex) {
				double bottomY = topLeft.Y + height;
				if (p.Y >= topLeft.Y && p.Y <= bottomY) {
					if (p.Y < midY) {
						insertionLineY = topLeft.Y;
						return i;
					}
					insertionLineY = bottomY;
					return Math.Min(n, i + 1);
				}
			}

			if (p.Y < midY) {
				insertionLineY = topLeft.Y;
				return i;
			}
		}

		// drop at end
		int lastIndex = n - 1;
		if (lastIndex == draggedIndex)
			lastIndex = n - 2;
		double endY = list.ActualHeight;
		if (lastIndex >= 0 && list.ItemContainerGenerator.ContainerFromIndex(lastIndex) is FrameworkElement lastContainer) {
			Point lastTopLeft = lastContainer.TranslatePoint(new Point(0, 0), list);
			endY = lastTopLeft.Y + lastContainer.ActualHeight;
		}
		insertionLineY = endY;
		return n;
	}
}

internal sealed class DragGhostAdorner : Adorner {
	readonly ImageSource? _snapshot;
	readonly Size _size;
	readonly Point _cursorOffset;
	Point _mousePoint;
	readonly double _opacity;
	readonly double _cornerRadius;

	public DragGhostAdorner(
		UIElement adornedElement,
		ImageSource? snapshot,
		Point mousePointInAdornedElement,
		Point cursorOffset,
		Size size,
		double opacity,
		double cornerRadius)
		: base(adornedElement) {
		IsHitTestVisible = false;
		_snapshot = snapshot;
		_cornerRadius = Math.Max(0d, cornerRadius);
		_size = size;
		_mousePoint = mousePointInAdornedElement;
		_opacity = Math.Clamp(opacity, 0d, 1d);
		_cursorOffset = cursorOffset;
	}

	public void setMousePoint(Point p) {
		_mousePoint = p;
		InvalidateVisual();
	}

	protected override void OnRender(DrawingContext drawingContext) {
		Point topLeft = new Point(_mousePoint.X - _cursorOffset.X, _mousePoint.Y - _cursorOffset.Y);
		var rect = new Rect(topLeft, _size);
		if (rect.Width <= 0d || rect.Height <= 0d)
			return;
		drawingContext.PushOpacity(_opacity);
		if (_cornerRadius > 0d) {
			drawingContext.PushClip(new RectangleGeometry(rect, _cornerRadius, _cornerRadius));
		}
		if (_snapshot != null)
			drawingContext.DrawImage(_snapshot, rect);
		if (_cornerRadius > 0d) {
			drawingContext.Pop();
		}
		drawingContext.Pop();
	}
}

internal sealed class InsertionLineAdorner : Adorner {
	public double lineY;
	Pen? _pen;

	public InsertionLineAdorner(UIElement adornedElement) : base(adornedElement) {
		IsHitTestVisible = false;
	}

	protected override void OnRender(DrawingContext drawingContext) {
		Rect r = new Rect(AdornedElement.RenderSize);
		if (r.Width <= 0d || r.Height <= 0d)
			return;

		// Adorners live for one drag session, so resolving the theme brush once is enough.
		if (_pen == null) {
			Brush b = (AdornedElement as FrameworkElement)?.TryFindResource("TextFillColorPrimaryBrush") as Brush
			          ?? Brushes.White;
			_pen = new Pen(b, 2.0);
			_pen.Freeze();
		}
		double yy = Math.Round(lineY) + 0.5;
		drawingContext.DrawLine(_pen, new Point(r.Left, yy), new Point(r.Right, yy));
	}
}

