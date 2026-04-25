using System.Windows;
using System.Windows.Controls;

namespace WindowsOscVolumeControl;

static class ScrollAnimation {
	public static readonly DependencyProperty horizontalOffsetProperty =
		DependencyProperty.RegisterAttached(
			"horizontalOffset",
			typeof(double),
			typeof(ScrollAnimation),
			new PropertyMetadata(0d, onHorizontalOffsetChanged));

	public static void setHorizontalOffset(DependencyObject d, double value) =>
		d.SetValue(horizontalOffsetProperty, value);

	public static double getHorizontalOffset(DependencyObject d) =>
		(double)d.GetValue(horizontalOffsetProperty);

	static void onHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is not ScrollViewer scroller)
			return;
		if (e.NewValue is not double v)
			return;
		if (!double.IsFinite(v))
			return;
		scroller.ScrollToHorizontalOffset(v);
	}
}

