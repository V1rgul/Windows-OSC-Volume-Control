using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;
using ComboBox = System.Windows.Controls.ComboBox;
using ContentPresenter = System.Windows.Controls.ContentPresenter;
using ControlTemplate = System.Windows.Controls.ControlTemplate;
using Expander = System.Windows.Controls.Expander;
using FrameworkElement = System.Windows.FrameworkElement;
using Grid = System.Windows.Controls.Grid;
using ItemsControl = System.Windows.Controls.ItemsControl;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using TextBlock = System.Windows.Controls.TextBlock;
using Thumb = System.Windows.Controls.Primitives.Thumb;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using UserControl = System.Windows.Controls.UserControl;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using DrawingContext = System.Windows.Media.DrawingContext;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using DataObject = System.Windows.DataObject;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;
using KeyboardFocusChangedEventArgs = System.Windows.Input.KeyboardFocusChangedEventArgs;
using MouseButton = System.Windows.Input.MouseButton;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using TraversalRequest = System.Windows.Input.TraversalRequest;

namespace WindowsOscVolumeControl;

public partial class BindingsPanelView : UserControl {
	public BindingsPanelView() {
		InitializeComponent();
	}

	ConfigWindowViewModel? vm => DataContext as ConfigWindowViewModel;

	HotkeyActionEditor? _hotkeyCaptureItem;
	DateTime? _hotkeyCaptureDownUtc;
	HotkeyGesture _hotkeyCaptureGesture;
	bool _hotkeyCaptureAwaitingRelease;

	static T? findDataContextAncestor<T>(DependencyObject start) where T : class {
		DependencyObject? cur = start;
		while (cur != null) {
			if (cur is FrameworkElement { DataContext: T match })
				return match;
			cur = VisualTreeHelper.GetParent(cur);
		}
		return null;
	}

	static T? findVisualAncestor<T>(DependencyObject start) where T : DependencyObject {
		DependencyObject? cur = start;
		while (cur != null) {
			if (cur is T match)
				return match;
			cur = VisualTreeHelper.GetParent(cur);
		}
		return null;
	}

	static bool isUnderBindingCardRestore(DependencyObject? hit) {
		while (hit != null) {
			if (hit is Button { Tag: string s } && s == ConfigWindow.BindingCardRestoreTag)
				return true;
			hit = VisualTreeHelper.GetParent(hit);
		}
		return false;
	}

	const double BINDING_CARD_SOFT_DELETE_CHEVRON_OPACITY = 0.38;

	readonly Dictionary<Expander, (BindingEditor bed, PropertyChangedEventHandler handler)> _bindingCardChevronDimHooks = [];
	readonly Dictionary<ItemsControl, InsertionLineAdorner> _insertionLineAdorners = [];

	static bool tryFindFluentExpanderChevronGrid(Expander exp, out UIElement? chevronGrid) {
		chevronGrid = null;
		exp.ApplyTemplate();
		if (exp.Template?.FindName("HeaderSite", exp) is not ToggleButton headerSite)
			return false;
		headerSite.ApplyTemplate();
		if (headerSite.Template?.FindName("ChevronGrid", headerSite) is not UIElement grid)
			return false;
		chevronGrid = grid;
		return true;
	}

	static void applyBindingCardExpanderContentFill(Expander exp) {
		if (exp.TryFindResource("ExpanderHeaderBackground") is Brush headerBg)
			exp.Resources["ExpanderContentBackground"] = headerBg;
		exp.ApplyTemplate();
		if (exp.Template?.FindName("ToggleButtonBorder", exp) is Border headerChrome) {
			headerChrome.BorderThickness = new Thickness(0);
			headerChrome.BorderBrush = Brushes.Transparent;
		}
		if (exp.Template?.FindName("ContentPresenterBorder", exp) is Border contentChrome) {
			contentChrome.SetResourceReference(Border.BackgroundProperty, "ExpanderHeaderBackground");
			contentChrome.BorderThickness = new Thickness(0);
			contentChrome.BorderBrush = Brushes.Transparent;
		}
		if (exp.Template?.FindName("ContentPresenter", exp) is ContentPresenter contentPresenter) {
			Thickness p = exp.Padding;
			contentPresenter.Margin = new Thickness(p.Left, p.Top, p.Right, 0d);
		}
	}

	void applyBindingCardChevronDim(Expander exp) {
		if (!tryFindFluentExpanderChevronGrid(exp, out UIElement? grid))
			return;
		if (grid == null)
			return;
		bool dim = exp.DataContext is BindingEditor { isDeleted: true };
		grid.Opacity = dim ? BINDING_CARD_SOFT_DELETE_CHEVRON_OPACITY : 1d;
	}

	void hookBindingCardChevronDim(Expander exp) {
		unhookBindingCardChevronDim(exp);
		if (exp.DataContext is not BindingEditor bed)
			return;
		PropertyChangedEventHandler h = (_, args) => {
			if (args.PropertyName is not null && args.PropertyName != nameof(BindingEditor.isDeleted))
				return;
			exp.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() => applyBindingCardChevronDim(exp)));
		};
		bed.PropertyChanged += h;
		_bindingCardChevronDimHooks[exp] = (bed, h);
		applyBindingCardChevronDim(exp);
	}

	void unhookBindingCardChevronDim(Expander exp) {
		if (!_bindingCardChevronDimHooks.Remove(exp, out (BindingEditor bed, PropertyChangedEventHandler handler) pair))
			return;
		pair.bed.PropertyChanged -= pair.handler;
	}

	void bindingCard_Expander_Loaded(object sender, RoutedEventArgs e) {
		if (sender is not Expander exp)
			return;
		applyBindingCardExpanderContentFill(exp);
		exp.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => hookBindingCardChevronDim(exp)));
	}

	void bindingCard_Expander_Unloaded(object sender, RoutedEventArgs e) {
		if (sender is not Expander exp)
			return;
		unhookBindingCardChevronDim(exp);
	}

	void bindingCard_Expander_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e) {
		if (sender is not Expander exp)
			return;
		unhookBindingCardChevronDim(exp);
		applyBindingCardExpanderContentFill(exp);
		exp.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => hookBindingCardChevronDim(exp)));
	}

	void bindingCard_Expander_Expanded(object sender, RoutedEventArgs e) {
		if (sender is not Expander exp)
			return;
		if (exp.DataContext is BindingEditor bed && bed.isDeleted)
			exp.IsExpanded = false;
		applyBindingCardExpanderContentFill(exp);
	}

	void bindingCard_Expander_PreviewMouseDown(object sender, MouseButtonEventArgs e) {
		if (e.ChangedButton != MouseButton.Left)
			return;
		if (sender is not Expander exp || exp.DataContext is not BindingEditor bed || !bed.isDeleted)
			return;
		if (isUnderBindingCardRestore(e.OriginalSource as DependencyObject))
			return;
		e.Handled = true;
	}

	void bindingCard_Expander_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is not Expander exp || exp.DataContext is not BindingEditor bed || !bed.isDeleted)
			return;
		if (e.Key != Key.Space && e.Key != Key.Enter)
			return;
		if (isUnderBindingCardRestore(Keyboard.FocusedElement as DependencyObject))
			return;
		e.Handled = true;
	}

	void hotkeyControl_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (m == null)
			return;
		m.setConfiguredHotkeysEnabled(false);
		if (sender is FrameworkElement { DataContext: HotkeyActionEditor item }) {
			item.isHotkeyCaptureActive = true;
			item.hotkey = HotkeyGesture.None;
			m.statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
			clearHotkeyCaptureTracking();
			_hotkeyCaptureItem = item;
		}
	}

	void hotkeyControl_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (sender is FrameworkElement { DataContext: HotkeyActionEditor item }) {
			item.isHotkeyCaptureActive = false;
			if (ReferenceEquals(_hotkeyCaptureItem, item))
				clearHotkeyCaptureTracking();
		}
		m?.setConfiguredHotkeysEnabled(true);
	}

	void hotkeyRow_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is FrameworkElement fe && fe.DataContext is HotkeyActionEditor item && item.isHotkeyCaptureActive)
			beginHotkeyCaptureKeyDown(e, item);
	}

	void hotkeyRow_PreviewKeyUp(object sender, KeyEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (m == null)
			return;
		if (sender is not FrameworkElement fe || fe.DataContext is not HotkeyActionEditor item)
			return;
		if (!item.isHotkeyCaptureActive || !_hotkeyCaptureAwaitingRelease || !ReferenceEquals(_hotkeyCaptureItem, item))
			return;
		HotkeyGesture up = HotkeyUtil.fromKeyEventArgs(e);
		HotkeyGesture normUp = HotkeyUtil.normalize(up);
		HotkeyGesture normDown = HotkeyUtil.normalize(_hotkeyCaptureGesture);
		bool acceptMacroKeyUpOrder = m.hotkeyAcceptMacroChordKeyOrder;
		if (acceptMacroKeyUpOrder) {
			if (normUp.keyCode != normDown.keyCode)
				return;
		} else {
			if (normUp != normDown)
				return;
		}
		e.Handled = true;
		finalizeHotkeyCapture(m, item, fe);
	}

	void clearHotkeyCaptureTracking() {
		_hotkeyCaptureItem = null;
		_hotkeyCaptureDownUtc = null;
		_hotkeyCaptureGesture = HotkeyGesture.None;
		_hotkeyCaptureAwaitingRelease = false;
	}

	bool tryParseHotkeyLongPressMsForCapture(ConfigWindowViewModel m, out uint ms) {
		ms = KeyboardHook.Config.DEFAULT_LONG_PRESS_MS;
		if (!uint.TryParse((m.hotkeyLongPressMsText ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint parsed))
			return false;
		ms = KeyboardHook.Config.Clamped(new KeyboardHook.Config { longPressDurationMs = parsed }).longPressDurationMs;
		return true;
	}

	void finalizeHotkeyCapture(ConfigWindowViewModel m, HotkeyActionEditor item, FrameworkElement focusMoveAnchor) {
		if (!_hotkeyCaptureDownUtc.HasValue)
			return;
		HotkeyGesture g = HotkeyUtil.normalize(_hotkeyCaptureGesture);
		if (g.isNone)
			return;
		if (!HotkeyUtil.tryValidate(g, out UiTextFeedback hkFb)) {
			m.statusFeedback = hkFb;
			clearHotkeyCaptureTracking();
			return;
		}
		item.hotkey = g;
		uint thresholdMs = tryParseHotkeyLongPressMsForCapture(m, out uint lp) ? lp : KeyboardHook.Config.DEFAULT_LONG_PRESS_MS;
		double heldMs = (DateTime.UtcNow - _hotkeyCaptureDownUtc.Value).TotalMilliseconds;
		item.longPress = heldMs >= thresholdMs;
		m.statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
		clearHotkeyCaptureTracking();
		focusMoveAnchor.Dispatcher.BeginInvoke(DispatcherPriority.Background, moveFocusAwayAfterAssign, focusMoveAnchor);
	}

	void beginHotkeyCaptureKeyDown(KeyEventArgs e, HotkeyActionEditor item) {
		ConfigWindowViewModel? m = vm;
		if (m == null)
			return;
		HotkeyGesture hotkey = HotkeyUtil.fromKeyEventArgs(e);
		if (hotkey.isNone) {
			e.Handled = true;
			return;
		}
		if (!HotkeyUtil.tryValidate(hotkey, out UiTextFeedback hkFb)) {
			m.statusFeedback = hkFb;
			e.Handled = true;
			return;
		}
		_hotkeyCaptureGesture = HotkeyUtil.normalize(hotkey);
		_hotkeyCaptureDownUtc = DateTime.UtcNow;
		_hotkeyCaptureAwaitingRelease = true;
		_hotkeyCaptureItem = item;
		m.statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
		e.Handled = true;
	}

	static void moveFocusAwayAfterAssign(object? captureElement) {
		if (captureElement is not FrameworkElement fe)
			return;
		_ = fe.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
	}

	const string REORDER_DRAG_FORMAT = "WindowsOscVolumeControl.ReorderItem";

	void reorderThumb_DragStarted(object sender, DragStartedEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (m == null)
			return;
		if (sender is not Thumb thumb)
			return;
		if (thumb.DataContext == null)
			return;

		ItemsControl? list = findVisualAncestor<ItemsControl>(thumb);
		if (list == null)
			return;

		object item = thumb.DataContext;
		if (item is BindingEditor be && be.isDeleted)
			return;
		if (item is HotkeyActionEditor he && he.isDeleted)
			return;

		FrameworkElement? container = list switch {
			ListBox lb => lb.ContainerFromElement(thumb) as FrameworkElement,
			_ => null,
		};

		m.dragOwnerList = list;
		m.dragItem = item;
		m.isDragInProgress = true;
		// The container height already includes the card's outer margin. The placeholder uses the same margin,
		// so we subtract it here to keep total reserved space identical while dragging.
		double h = container?.ActualHeight ?? 0d;
		var outerMargin = (Thickness)(TryFindResource("ReorderCardOuterMargin") ?? new Thickness(0));
		m.dragPlaceholderHeight = Math.Max(0d, h - (outerMargin.Top + outerMargin.Bottom));

		try {
			hideInsertionLine(list);
			var data = new DataObject();
			data.SetData(REORDER_DRAG_FORMAT, item);
			_ = DragDrop.DoDragDrop(thumb, data, DragDropEffects.Move);
		} finally {
			hideInsertionLine(list);
			m.isDragInProgress = false;
			m.dragItem = null;
			m.dragOwnerList = null;
			m.dragPlaceholderHeight = 0d;
		}
	}

	void reorderList_DragLeave(object sender, DragEventArgs e) {
		if (sender is not ItemsControl list)
			return;
		if (!ReferenceEquals(e.OriginalSource, list))
			return;
		hideInsertionLine(list);
	}

	void reorderList_DragOver(object sender, DragEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (sender is not ItemsControl list || m == null) {
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		if (!m.isDragInProgress || m.dragOwnerList == null || !ReferenceEquals(list, m.dragOwnerList)) {
			hideInsertionLine(list);
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		if (!e.Data.GetDataPresent(REORDER_DRAG_FORMAT) || m.dragItem == null) {
			hideInsertionLine(list);
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		Point p = e.GetPosition(list);
		_ = computeDropIndex(list, p, m.dragItem, out double lineY);
		showInsertionLine(list, lineY);

		e.Effects = DragDropEffects.Move;
		e.Handled = true;
	}

	void reorderList_Drop(object sender, DragEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (sender is not ItemsControl list || m == null) {
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		try {
			if (!m.isDragInProgress || m.dragOwnerList == null || !ReferenceEquals(list, m.dragOwnerList) || m.dragItem == null) {
				e.Effects = DragDropEffects.None;
				return;
			}
			if (!e.Data.GetDataPresent(REORDER_DRAG_FORMAT)) {
				e.Effects = DragDropEffects.None;
				return;
			}

			Point p = e.GetPosition(list);
			int dropIndex = computeDropIndex(list, p, m.dragItem, out _);
			tryMoveDraggedItem(m, list, m.dragItem, dropIndex);
			e.Effects = DragDropEffects.Move;
		} finally {
			hideInsertionLine(list);
			e.Handled = true;
		}
	}

	static int dropIndexToMoveIndex(int oldIndex, int dropIndex, int count) {
		int idx = Math.Clamp(dropIndex, 0, count);
		int moveIdx = idx > oldIndex ? idx - 1 : idx;
		return Math.Clamp(moveIdx, 0, Math.Max(0, count - 1));
	}

	static void tryMoveDraggedItem(ConfigWindowViewModel m, ItemsControl list, object dragged, int dropIndex) {
		if (dragged is BindingEditor bed) {
			int oldIndex = m.bindings.IndexOf(bed);
			if (oldIndex < 0)
				return;
			int newIndex = dropIndexToMoveIndex(oldIndex, dropIndex, m.bindings.Count);
			if (oldIndex == newIndex)
				return;
			m.bindings.Move(oldIndex, newIndex);
			return;
		}

		if (dragged is HotkeyActionEditor hed && list.DataContext is BindingEditor owner) {
			var hotkeys = owner.hotkeys;
			int oldIndex = hotkeys.IndexOf(hed);
			if (oldIndex < 0)
				return;
			int newIndex = dropIndexToMoveIndex(oldIndex, dropIndex, hotkeys.Count);
			if (oldIndex == newIndex)
				return;
			hotkeys.Move(oldIndex, newIndex);
		}
	}

	int computeDropIndex(ItemsControl list, Point p, object dragged, out double insertionLineY) {
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

	void showInsertionLine(ItemsControl list, double y) {
		if (!_insertionLineAdorners.TryGetValue(list, out InsertionLineAdorner? ad)) {
			AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list);
			if (layer == null)
				return;
			ad = new InsertionLineAdorner(list);
			_insertionLineAdorners[list] = ad;
			layer.Add(ad);
		}
		ad.lineY = y;
		ad.InvalidateVisual();
	}

	void hideInsertionLine(ItemsControl list) {
		if (!_insertionLineAdorners.Remove(list, out InsertionLineAdorner? ad))
			return;
		AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list);
		if (layer == null)
			return;
		layer.Remove(ad);
	}
}

sealed class InsertionLineAdorner : Adorner {
	public double lineY;

	public InsertionLineAdorner(UIElement adornedElement) : base(adornedElement) {
		IsHitTestVisible = false;
	}

	protected override void OnRender(DrawingContext drawingContext) {
		var pen = new Pen(Brushes.White, 2);
		Rect r = new Rect(AdornedElement.RenderSize);
		drawingContext.DrawLine(pen, new Point(r.Left, lineY), new Point(r.Right, lineY));
	}
}

