using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WindowsOscVolumeControl;

public sealed class OverflowMarquee : ContentControl {
	public enum ScrollMode {
		ALWAYS,
		ONLY_ON_HOVER,
	}

	public static readonly DependencyProperty scrollModeProperty =
		DependencyProperty.Register(
			nameof(scrollMode),
			typeof(ScrollMode),
			typeof(OverflowMarquee),
			new PropertyMetadata(ScrollMode.ALWAYS, onConfigChanged));

	public static readonly DependencyProperty snapToStartOnHoverExitProperty =
		DependencyProperty.Register(
			nameof(snapToStartOnHoverExit),
			typeof(bool),
			typeof(OverflowMarquee),
			new PropertyMetadata(true, onConfigChanged));

	public static readonly DependencyProperty speedDipPerSecondProperty =
		DependencyProperty.Register(
			nameof(speedDipPerSecond),
			typeof(double),
			typeof(OverflowMarquee),
			new PropertyMetadata(20d, onConfigChanged));

	public static readonly DependencyProperty speedBackDipPerSecondProperty =
		DependencyProperty.Register(
			nameof(speedBackDipPerSecond),
			typeof(double),
			typeof(OverflowMarquee),
			new PropertyMetadata(40d, onConfigChanged));

	public static readonly DependencyProperty pauseAtStartMsProperty =
		DependencyProperty.Register(
			nameof(pauseAtStartMs),
			typeof(int),
			typeof(OverflowMarquee),
			new PropertyMetadata(500, onConfigChanged));

	public static readonly DependencyProperty pauseAtEndMsProperty =
		DependencyProperty.Register(
			nameof(pauseAtEndMs),
			typeof(int),
			typeof(OverflowMarquee),
			new PropertyMetadata(150, onConfigChanged));

	ScrollViewer? _scroller;
	Storyboard? _storyboard;
	bool _hovering;

	static OverflowMarquee() {
		DefaultStyleKeyProperty.OverrideMetadata(typeof(OverflowMarquee), new FrameworkPropertyMetadata(typeof(OverflowMarquee)));
	}

	public ScrollMode scrollMode {
		get => (ScrollMode)GetValue(scrollModeProperty);
		set => SetValue(scrollModeProperty, value);
	}

	public bool snapToStartOnHoverExit {
		get => (bool)GetValue(snapToStartOnHoverExitProperty);
		set => SetValue(snapToStartOnHoverExitProperty, value);
	}

	public double speedDipPerSecond {
		get => (double)GetValue(speedDipPerSecondProperty);
		set => SetValue(speedDipPerSecondProperty, value);
	}

	public double speedBackDipPerSecond {
		get => (double)GetValue(speedBackDipPerSecondProperty);
		set => SetValue(speedBackDipPerSecondProperty, value);
	}

	public int pauseAtStartMs {
		get => (int)GetValue(pauseAtStartMsProperty);
		set => SetValue(pauseAtStartMsProperty, value);
	}

	public int pauseAtEndMs {
		get => (int)GetValue(pauseAtEndMsProperty);
		set => SetValue(pauseAtEndMsProperty, value);
	}

	public override void OnApplyTemplate() {
		base.OnApplyTemplate();
		_scroller = GetTemplateChild("PART_Scroller") as ScrollViewer;
		stopAnimation(snapToStart: true);

		if (_scroller != null) {
			_scroller.Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(updateAnimationState));
			_scroller.SizeChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(updateAnimationState));
			Loaded += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(updateAnimationState));
			SizeChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(updateAnimationState));
		}
	}

	protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e) {
		base.OnMouseEnter(e);
		if (scrollMode != ScrollMode.ONLY_ON_HOVER)
			return;
		_hovering = true;
		updateAnimationState();
	}

	protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e) {
		base.OnMouseLeave(e);
		if (scrollMode != ScrollMode.ONLY_ON_HOVER)
			return;
		_hovering = false;
		stopAnimation(snapToStart: snapToStartOnHoverExit);
	}

	static void onConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) {
		if (d is OverflowMarquee m)
			m.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(m.updateAnimationState));
	}

	bool shouldAnimateForMode() =>
		scrollMode == ScrollMode.ALWAYS || (scrollMode == ScrollMode.ONLY_ON_HOVER && _hovering);

	void updateAnimationState() {
		if (_scroller == null)
			return;

		double maxOffset = _scroller.ScrollableWidth;
		if (!double.IsFinite(maxOffset) || maxOffset <= 0) {
			stopAnimation(snapToStart: true);
			return;
		}

		if (!shouldAnimateForMode()) {
			if (scrollMode == ScrollMode.ONLY_ON_HOVER && snapToStartOnHoverExit)
				stopAnimation(snapToStart: true);
			else
				stopAnimation(snapToStart: false);
			return;
		}

		startOrRestartAnimation(maxOffset);
	}

	void stopAnimation(bool snapToStart) {
		_storyboard?.Stop();
		_storyboard = null;
		if (_scroller != null && snapToStart)
			_scroller.ScrollToHorizontalOffset(0);
	}

	void startOrRestartAnimation(double maxOffset) {
		if (_scroller == null)
			return;

		double speed = speedDipPerSecond;
		double speedBack = speedBackDipPerSecond;
		if (!double.IsFinite(speed) || speed <= 0 || !double.IsFinite(speedBack) || speedBack <= 0) {
			stopAnimation(snapToStart: true);
			return;
		}

		int pauseStart = Math.Max(0, pauseAtStartMs);
		int pauseEnd = Math.Max(0, pauseAtEndMs);

		TimeSpan endPause = TimeSpan.FromMilliseconds(pauseEnd);
		TimeSpan forward = TimeSpan.FromSeconds(maxOffset / speed);
		TimeSpan back = TimeSpan.FromSeconds(maxOffset / speedBack);
		TimeSpan cyclePause = TimeSpan.FromMilliseconds(pauseStart);

		var anim = new DoubleAnimationUsingKeyFrames();
		anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(TimeSpan.Zero)));
		anim.KeyFrames.Add(new LinearDoubleKeyFrame(maxOffset, KeyTime.FromTimeSpan(forward)));
		anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(maxOffset, KeyTime.FromTimeSpan(forward + endPause)));
		anim.KeyFrames.Add(new LinearDoubleKeyFrame(0d, KeyTime.FromTimeSpan(forward + endPause + back)));
		anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(0d, KeyTime.FromTimeSpan(forward + endPause + back + cyclePause)));
		anim.RepeatBehavior = RepeatBehavior.Forever;

		var sb = new Storyboard();
		sb.Children.Add(anim);
		Storyboard.SetTarget(anim, _scroller);
		Storyboard.SetTargetProperty(anim, new PropertyPath(ScrollAnimation.horizontalOffsetProperty));

		_storyboard?.Stop();
		_storyboard = sb;
		sb.Begin();
	}
}

