using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfScrollBar = System.Windows.Controls.Primitives.ScrollBar;

namespace WindowsOscVolumeControl.UI.Wpf.Controls;

/// <summary>
/// Pure delta → vertical pixel offset for wheel handling (testable).
/// <see cref="Mouse.MouseWheelDeltaForOneLine"/> (120) is wheel delta units, not DIP per line.
/// One line of scroll distance is <see cref="SystemParameters.ScrollHeight"/>; one notch is
/// <c>Delta / 120 × WheelScrollLines × ScrollHeight</c> DIP (page mode uses <c>ViewportHeight</c> instead of lines×height).
/// </summary>
internal static class SmoothScrollMath {
	internal static double computeVerticalWheelDeltaPixels(
		int wheelDelta,
		int wheelScrollLines,
		double scrollHeight,
		double viewportHeight,
		int mouseWheelDeltaForOneLine) {
		if (mouseWheelDeltaForOneLine == 0 || wheelScrollLines == 0)
			return 0;
		if (double.IsNaN(scrollHeight) || double.IsNaN(viewportHeight))
			return 0;

		double distancePerLineUnit = wheelScrollLines < 0
			? viewportHeight
			: wheelScrollLines * scrollHeight;

		return -(double)wheelDelta / mouseWheelDeltaForOneLine * distancePerLineUnit;
	}

	internal static double clampVerticalOffset(double offset, double maxOffset) {
		if (double.IsNaN(offset) || double.IsNaN(maxOffset) || maxOffset < 0)
			return 0;
		if (offset < 0)
			return 0;
		if (offset > maxOffset)
			return maxOffset;
		return offset;
	}
}

/// <summary>
/// <see cref="ScrollViewer"/> with system-consistent wheel deltas (trackpads) and eased motion toward a target offset.
/// </summary>
public class SmoothScrollViewer : ScrollViewer {
	const double STOP_EPSILON = 0.35;
	const double NESTED_SCROLLABLE_MIN = 0.5;
	/// <summary>Seconds; smaller = shorter ease (exponential toward target).</summary>
	const double EASE_TAU_SEC = 0.055;
	const double MAX_DT_SEC = 0.2;

	bool _renderingHooked;
	bool _animating;
	bool _programmaticScroll;
	bool _suppressBubblingMouseWheel;
	double _targetVerticalOffset;
	TimeSpan? _lastRenderTime;
	WpfScrollBar? _verticalScrollBar;

	public SmoothScrollViewer() {
		// Default true uses logical scroll units (coarse with StackPanel); fractional lerp then no-ops → no visible easing.
		SetCurrentValue(CanContentScrollProperty, false);
		// Fluent theme can add non-zero ScrollViewer padding; keep content flush to the viewport edges.
		SetCurrentValue(PaddingProperty, new Thickness(0));
		Loaded += onLoaded;
		Unloaded += onUnloaded;
		ScrollChanged += onScrollChanged;
		CommandManager.AddPreviewExecutedHandler(this, onPreviewExecutedScrollLineCommands);
	}

	void onLoaded(object sender, RoutedEventArgs e) {
		SetCurrentValue(CanContentScrollProperty, false);
		_targetVerticalOffset = VerticalOffset;
	}

	public override void OnApplyTemplate() {
		base.OnApplyTemplate();
		_verticalScrollBar = GetTemplateChild("PART_VerticalScrollBar") as WpfScrollBar;
	}

	static bool isVisualDescendant(DependencyObject? node, DependencyObject? ancestor) {
		for (DependencyObject? n = node; n != null; n = VisualTreeHelper.GetParent(n)) {
			if (ReferenceEquals(n, ancestor))
				return true;
		}
		return false;
	}

	void onPreviewExecutedScrollLineCommands(object sender, ExecutedRoutedEventArgs e) {
		if (e.Command != WpfScrollBar.LineUpCommand && e.Command != WpfScrollBar.LineDownCommand)
			return;
		if (ScrollableHeight <= 0 || _verticalScrollBar == null)
			return;
		if (e.OriginalSource is not DependencyObject src || !isVisualDescendant(src, _verticalScrollBar))
			return;

		double line = SystemParameters.ScrollHeight;
		if (line <= 0)
			return;

		double delta = ReferenceEquals(e.Command, WpfScrollBar.LineUpCommand) ? -line : line;
		double maxOffset = maxVerticalScroll();
		_targetVerticalOffset = SmoothScrollMath.clampVerticalOffset(_targetVerticalOffset + delta, maxOffset);
		e.Handled = true;
		ensureRenderingHook();
	}

	void onUnloaded(object sender, RoutedEventArgs e) {
		CommandManager.RemovePreviewExecutedHandler(this, onPreviewExecutedScrollLineCommands);
		stopRenderingHook();
	}

	void onScrollChanged(object sender, ScrollChangedEventArgs e) {
		if (_programmaticScroll || _animating)
			return;
		_targetVerticalOffset = VerticalOffset;
	}

	void stopRenderingHook() {
		if (!_renderingHooked)
			return;
		CompositionTarget.Rendering -= onRendering;
		_renderingHooked = false;
		_animating = false;
		_lastRenderTime = null;
	}

	void ensureRenderingHook() {
		if (_renderingHooked)
			return;
		_renderingHooked = true;
		_animating = true;
		_lastRenderTime = null;
		CompositionTarget.Rendering += onRendering;
	}

	void onRendering(object? sender, EventArgs e) {
		if (e is not RenderingEventArgs re)
			return;

		double maxOffset = maxVerticalScroll();
		if (maxOffset <= 0) {
			stopRenderingHook();
			return;
		}

		double current = VerticalOffset;
		double target = SmoothScrollMath.clampVerticalOffset(_targetVerticalOffset, maxOffset);
		_targetVerticalOffset = target;

		if (Math.Abs(current - target) < STOP_EPSILON) {
			_programmaticScroll = true;
			try {
				ScrollToVerticalOffset(target);
			} finally {
				_programmaticScroll = false;
			}
			stopRenderingHook();
			return;
		}

		TimeSpan now = re.RenderingTime;
		double dt;
		if (_lastRenderTime is null) {
			dt = 1.0 / 60.0;
		} else {
			dt = (now - _lastRenderTime.Value).TotalSeconds;
			if (dt <= 0 || dt > MAX_DT_SEC)
				dt = 1.0 / 60.0;
		}
		_lastRenderTime = now;

		double alpha = 1 - Math.Exp(-dt / EASE_TAU_SEC);
		double next = current + (target - current) * alpha;
		if (target > current)
			next = Math.Min(next, target);
		else
			next = Math.Max(next, target);

		_programmaticScroll = true;
		try {
			ScrollToVerticalOffset(next);
		} finally {
			_programmaticScroll = false;
		}
	}

	double maxVerticalScroll() {
		double h = ExtentHeight - ViewportHeight;
		if (double.IsNaN(h) || h < 0)
			return 0;
		return h;
	}

	/// <summary>
	/// If the wheel applies to a nested <see cref="ScrollViewer"/> that can scroll, let default handling run.
	/// </summary>
	static bool shouldDeferToInnerScrollViewer(MouseWheelEventArgs e, ScrollViewer outer) {
		for (DependencyObject? node = e.OriginalSource as DependencyObject;
		     node != null;
		     node = VisualTreeHelper.GetParent(node)) {
			if (!ReferenceEquals(node, outer) && node is ScrollViewer inner && inner.ScrollableHeight > NESTED_SCROLLABLE_MIN)
				return true;
			if (ReferenceEquals(node, outer))
				break;
		}
		return false;
	}

	protected override void OnPreviewMouseWheel(MouseWheelEventArgs e) {
		_suppressBubblingMouseWheel = false;

		if (e.Handled || ScrollableHeight <= 0) {
			base.OnPreviewMouseWheel(e);
			return;
		}

		if (shouldDeferToInnerScrollViewer(e, this)) {
			base.OnPreviewMouseWheel(e);
			return;
		}

		int lines = SystemParameters.WheelScrollLines;
		double deltaY = SmoothScrollMath.computeVerticalWheelDeltaPixels(
			e.Delta,
			lines,
			SystemParameters.ScrollHeight,
			ViewportHeight,
			Mouse.MouseWheelDeltaForOneLine);

		if (Math.Abs(deltaY) < double.Epsilon) {
			base.OnPreviewMouseWheel(e);
			return;
		}

		double maxOffset = maxVerticalScroll();
		_targetVerticalOffset = SmoothScrollMath.clampVerticalOffset(_targetVerticalOffset + deltaY, maxOffset);

		e.Handled = true;
		_suppressBubblingMouseWheel = true;
		ensureRenderingHook();
	}

	protected override void OnMouseWheel(MouseWheelEventArgs e) {
		if (_suppressBubblingMouseWheel) {
			_suppressBubblingMouseWheel = false;
			e.Handled = true;
			return;
		}
		base.OnMouseWheel(e);
	}
}
