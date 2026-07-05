using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WindowsOscVolumeControl.UI.Config.ViewModels;
using WindowsOscVolumeControl.UI.Wpf.Behaviors;
using Button = System.Windows.Controls.Button;
using Border = System.Windows.Controls.Border;
using ContentPresenter = System.Windows.Controls.ContentPresenter;
using ComboBox = System.Windows.Controls.ComboBox;
using Expander = System.Windows.Controls.Expander;
using FrameworkElement = System.Windows.FrameworkElement;
using ItemsControl = System.Windows.Controls.ItemsControl;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;
using Thumb = System.Windows.Controls.Primitives.Thumb;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
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

namespace WindowsOscVolumeControl.UI.Config;

public partial class BindingsPanelView {
	public BindingsPanelView() {
		InitializeComponent();
		AddHandler(DragDrop.PreviewDragEnterEvent, new System.Windows.DragEventHandler(onReorderPreviewDragOver), handledEventsToo: true);
		AddHandler(DragDrop.PreviewDragOverEvent, new System.Windows.DragEventHandler(onReorderPreviewDragOver), handledEventsToo: true);
		AddHandler(DragDrop.PreviewDropEvent, new System.Windows.DragEventHandler(onReorderPreviewDrop), handledEventsToo: true);
	}

	ConfigWindowViewModel? vm => DataContext as ConfigWindowViewModel;

	AdornerLayer? _dragGhostLayer;
	DragGhostAdorner? _dragGhostAdorner;
	ItemsControl? _dragGhostOwnerList;

	ControlActionEditor? _hotkeyCaptureItem;
	HotkeyGesture _hotkeyCapturePreviousGesture;
	DateTime? _hotkeyCaptureDownUtc;
	HotkeyGesture _hotkeyCaptureGesture;
	bool _hotkeyCaptureAwaitingRelease;

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

	static void bindEditableComboBoxTextForeground(ComboBox combo) {
		combo.ApplyTemplate();
		if (combo.Template?.FindName("PART_EditableTextBox", combo) is not TextBox textBox)
			return;
		BindingOperations.SetBinding(textBox, TextBox.ForegroundProperty, new System.Windows.Data.Binding(nameof(ComboBox.Foreground)) {
			Source = combo,
			Mode = BindingMode.OneWay,
		});
	}

	void oscAddressComboBox_Loaded(object sender, RoutedEventArgs e) {
		if (sender is not ComboBox combo)
			return;
		combo.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => bindEditableComboBoxTextForeground(combo)));
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
		if (sender is FrameworkElement { DataContext: ControlActionEditor item }) {
			clearHotkeyCaptureTracking();
			// Remember the current assignment so abandoning capture (focus moves away) restores it.
			_hotkeyCaptureItem = item;
			_hotkeyCapturePreviousGesture = item.hotkey;
			item.isHotkeyCaptureActive = true;
			item.hotkey = HotkeyGesture.None;
			m.statusFeedback = new UiTextFeedback("", UiTextFeedbackKind.DEFAULT);
		}
	}

	void hotkeyControl_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (sender is FrameworkElement { DataContext: ControlActionEditor item }) {
			item.isHotkeyCaptureActive = false;
			if (ReferenceEquals(_hotkeyCaptureItem, item)) {
				// Capture abandoned without a completed press: restore the assignment cleared at capture start.
				if (item.hotkey.isNone)
					item.hotkey = _hotkeyCapturePreviousGesture;
				clearHotkeyCaptureTracking();
			}
		}
		m?.setConfiguredHotkeysEnabled(true);
	}

	void hotkeyRow_PreviewKeyDown(object sender, KeyEventArgs e) {
		if (sender is FrameworkElement fe && fe.DataContext is ControlActionEditor item && item.isHotkeyCaptureActive)
			beginHotkeyCaptureKeyDown(e, item);
	}

	void hotkeyRow_PreviewKeyUp(object sender, KeyEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (m == null)
			return;
		if (sender is not FrameworkElement fe || fe.DataContext is not ControlActionEditor item)
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
		_hotkeyCapturePreviousGesture = HotkeyGesture.None;
		resetHotkeyCapturePressTracking();
	}

	void resetHotkeyCapturePressTracking() {
		_hotkeyCaptureDownUtc = null;
		_hotkeyCaptureGesture = HotkeyGesture.None;
		_hotkeyCaptureAwaitingRelease = false;
	}

	bool tryParseHotkeyLongPressMsForCapture(ConfigWindowViewModel m, out uint ms) {
		(bool ok, uint parsedMs) = m.hotkeyLongPressMsResult.match(
			v => (true, v),
			_ => (false, KeyboardHook.Config.DEFAULT_LONG_PRESS_MS));
		ms = parsedMs;
		return ok;
	}

	void finalizeHotkeyCapture(ConfigWindowViewModel m, ControlActionEditor item, FrameworkElement focusMoveAnchor) {
		if (!_hotkeyCaptureDownUtc.HasValue)
			return;
		HotkeyGesture g = HotkeyUtil.normalize(_hotkeyCaptureGesture);
		if (g.isNone)
			return;
		if (!HotkeyUtil.tryValidate(g, out UiTextFeedback hkFb)) {
			m.statusFeedback = hkFb;
			// Keep item/previous-gesture tracking so the user can retry or abandon (restoring the old assignment).
			resetHotkeyCapturePressTracking();
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

	void beginHotkeyCaptureKeyDown(KeyEventArgs e, ControlActionEditor item) {
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

	// Drag/drop payload format: keep stable across all reorder lists.

	void beginDragGhost(ItemsControl list, FrameworkElement? draggedContainer, object draggedItem, Point dragStartPointInList) {
		endDragGhost();
		if (draggedContainer == null)
			return;
		AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list)
		                      ?? AdornerLayer.GetAdornerLayer(draggedContainer)
		                      ?? AdornerLayer.GetAdornerLayer(this);
		if (layer == null)
			return;

		var outerMargin = (Thickness)(TryFindResource("ReorderCardOuterMargin") ?? new Thickness(0));

		draggedContainer.UpdateLayout();
		ImageSource? snapshot = tryRenderGhostSnapshot(list, draggedContainer, outerMargin);
		if (snapshot == null)
			return;

		double radius = resolveCardCornerRadiusFromTemplate(draggedContainer, draggedItem);
		double rawW = draggedContainer.ActualWidth;
		double rawH = draggedContainer.ActualHeight;
		System.Windows.Size size = new(
			Math.Max(0d, rawW - (outerMargin.Left + outerMargin.Right)),
			Math.Max(0d, rawH - (outerMargin.Top + outerMargin.Bottom)));
		Point containerTopLeftInList = draggedContainer.TranslatePoint(new Point(0, 0), list);
		Point ghostTopLeftInList = new Point(containerTopLeftInList.X + outerMargin.Left, containerTopLeftInList.Y + outerMargin.Top);
		Point cursorOffset = new Point(
			dragStartPointInList.X - ghostTopLeftInList.X,
			dragStartPointInList.Y - ghostTopLeftInList.Y);

		_dragGhostOwnerList = list;
		_dragGhostLayer = layer;
		_dragGhostAdorner = new DragGhostAdorner(
			list,
			snapshot,
			dragStartPointInList,
			cursorOffset,
			size,
			opacity: ReorderDragDrop.dragGhostOpacity,
			cornerRadius: radius);
		layer.Add(_dragGhostAdorner);
		_dragGhostAdorner.InvalidateVisual();
	}

	void updateDragGhost(ItemsControl list, Point mousePointInList) {
		if (_dragGhostAdorner == null)
			return;
		if (!ReferenceEquals(_dragGhostOwnerList, list))
			return;
		_dragGhostAdorner.setMousePoint(mousePointInList);
	}

	void endDragGhost() {
		if (_dragGhostLayer != null && _dragGhostAdorner != null)
			_dragGhostLayer.Remove(_dragGhostAdorner);
		_dragGhostLayer = null;
		_dragGhostAdorner = null;
		_dragGhostOwnerList = null;
	}

	static ImageSource? tryRenderGhostSnapshot(FrameworkElement list, FrameworkElement draggedContainer, Thickness outerMargin) {
		// Rasterize through the *owning window* (true composited surface, keeps layered Fluent backgrounds),
		// but only the dragged row rectangle: a VisualBrush viewbox avoids allocating a full-window bitmap.
		Window? window = Window.GetWindow(list);
		if (window == null)
			return null;

		double cw = draggedContainer.ActualWidth;
		double ch = draggedContainer.ActualHeight;
		if (cw <= 0d || ch <= 0d)
			return null;

		// Exclude the per-row outer margin so the ghost doesn't carry the darker bars.
		Point topLeft = draggedContainer.TranslatePoint(new Point(0, 0), window);
		double cropX = topLeft.X + outerMargin.Left;
		double cropY = topLeft.Y + outerMargin.Top;
		double cropW = Math.Max(1d, cw - (outerMargin.Left + outerMargin.Right));
		double cropH = Math.Max(1d, ch - (outerMargin.Top + outerMargin.Bottom));

		PresentationSource? ps = PresentationSource.FromVisual(window);
		Matrix toDevice = ps?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
		double dpiScaleX = toDevice.M11;
		double dpiScaleY = toDevice.M22;

		var windowCropBrush = new VisualBrush(window) {
			ViewboxUnits = BrushMappingMode.Absolute,
			Viewbox = new Rect(cropX, cropY, cropW, cropH),
			Stretch = Stretch.Fill,
		};

		var rowVisual = new DrawingVisual();
		using (DrawingContext dc = rowVisual.RenderOpen())
			dc.DrawRectangle(windowCropBrush, null, new Rect(0d, 0d, cropW, cropH));

		int pxW = Math.Max(1, (int)Math.Ceiling(cropW * dpiScaleX));
		int pxH = Math.Max(1, (int)Math.Ceiling(cropH * dpiScaleY));
		var rtb = new RenderTargetBitmap(pxW, pxH, 96d * dpiScaleX, 96d * dpiScaleY, PixelFormats.Pbgra32);
		rtb.Render(rowVisual);
		rtb.Freeze();
		return rtb;
	}

	static double resolveCardCornerRadiusFromTemplate(FrameworkElement container, object draggedItem) {
		string? borderName = draggedItem switch {
			BindingEditor => "BindingOscCardBorder",
			ControlActionEditor => "HotkeyRowBorder",
			_ => null,
		};
		if (borderName == null)
			return 0d;
		Border? b = tryFindNamedDescendant(container, borderName);
		if (b == null)
			return 0d;
		return Math.Max(Math.Max(b.CornerRadius.TopLeft, b.CornerRadius.TopRight),
			Math.Max(b.CornerRadius.BottomLeft, b.CornerRadius.BottomRight));
	}

	static Border? tryFindNamedDescendant(DependencyObject root, string name) {
		int n = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < n; i++) {
			DependencyObject child = VisualTreeHelper.GetChild(root, i);
			if (child is FrameworkElement fe && string.Equals(fe.Name, name, StringComparison.Ordinal))
				return fe as Border;
			Border? nested = tryFindNamedDescendant(child, name);
			if (nested != null)
				return nested;
		}
		return null;
	}

	// (drag ghost uses cropped list snapshot; no brush derivation helpers needed)

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
		if (item is ControlActionEditor he && he.isDeleted)
			return;

		FrameworkElement? container = list switch {
			ListBox lb => lb.ContainerFromElement(thumb) as FrameworkElement,
			_ => null,
		};

		// Create the drag ghost before toggling drag state, because the row will be collapsed to show the placeholder.
		beginDragGhost(list, container, item, Mouse.GetPosition(list));

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
			data.SetData(ReorderDragDrop.reorderDragFormat, item);
			_ = DragDrop.DoDragDrop(thumb, data, DragDropEffects.Move);
		} finally {
			endDragGhost();
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

	// Reorder drags are handled exclusively by the Preview* handlers (which set e.Handled, so these
	// bubbling list handlers never run for them). They only reject foreign drags over the lists.
	void reorderList_DragOver(object sender, DragEventArgs e) {
		if (sender is not ItemsControl list)
			return;
		hideInsertionLine(list);
		e.Effects = DragDropEffects.None;
		e.Handled = true;
	}

	void onReorderPreviewDragOver(object sender, DragEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (m == null || !m.isDragInProgress || m.dragOwnerList == null) {
			return;
		}
		ItemsControl list = m.dragOwnerList;

		if (!e.Data.GetDataPresent(ReorderDragDrop.reorderDragFormat) || m.dragItem == null) {
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		Point p = e.GetPosition(list);
		updateDragGhost(list, p);
		_ = ReorderDragDrop.computeDropIndex(list, p, m.dragItem, out double lineY);
		showInsertionLine(list, lineY);

		e.Effects = DragDropEffects.Move;
		e.Handled = true;
	}

	void onReorderPreviewDrop(object sender, DragEventArgs e) {
		ConfigWindowViewModel? m = vm;
		if (m == null || !m.isDragInProgress || m.dragOwnerList == null || m.dragItem == null) {
			return;
		}
		ItemsControl list = m.dragOwnerList;
		if (!e.Data.GetDataPresent(ReorderDragDrop.reorderDragFormat)) {
			e.Effects = DragDropEffects.None;
			e.Handled = true;
			return;
		}

		Point p = e.GetPosition(list);
		int dropIndex = ReorderDragDrop.computeDropIndex(list, p, m.dragItem, out _);
		tryMoveDraggedItem(m, list, m.dragItem, dropIndex);
		hideInsertionLine(list);
		e.Effects = DragDropEffects.Move;
		e.Handled = true;
	}

	void reorderList_Drop(object sender, DragEventArgs e) {
		if (sender is not ItemsControl list)
			return;
		hideInsertionLine(list);
		e.Effects = DragDropEffects.None;
		e.Handled = true;
	}

	static void tryMoveDraggedItem(ConfigWindowViewModel m, ItemsControl list, object dragged, int dropIndex) {
		if (dragged is BindingEditor bed) {
			int oldIndex = m.bindings.IndexOf(bed);
			if (oldIndex < 0)
				return;
			int newIndex = ReorderDragDrop.dropIndexToMoveIndex(oldIndex, dropIndex, m.bindings.Count);
			if (oldIndex == newIndex)
				return;
			m.bindings.Move(oldIndex, newIndex);
			return;
		}

		if (dragged is ControlActionEditor hed && list.DataContext is BindingEditor owner) {
			var hotkeys = owner.actions;
			int oldIndex = hotkeys.IndexOf(hed);
			if (oldIndex < 0)
				return;
			int newIndex = ReorderDragDrop.dropIndexToMoveIndex(oldIndex, dropIndex, hotkeys.Count);
			if (oldIndex == newIndex)
				return;
			hotkeys.Move(oldIndex, newIndex);
		}
	}

	void showInsertionLine(ItemsControl list, double y) {
		if (!_insertionLineAdorners.TryGetValue(list, out InsertionLineAdorner? ad)) {
			AdornerLayer? layer = AdornerLayer.GetAdornerLayer(list);
			if (layer == null)
				return;
			ad = new InsertionLineAdorner(list);
			_insertionLineAdorners[list] = ad;
			layer.Add(ad);

			// One-time z-order fix: keep the drag ghost above the lazily added insertion line.
			if (_dragGhostLayer != null && _dragGhostAdorner != null && ReferenceEquals(layer, _dragGhostLayer)) {
				_dragGhostLayer.Remove(_dragGhostAdorner);
				_dragGhostLayer.Add(_dragGhostAdorner);
			}
		}

		// The line snaps to whole pixels in OnRender, so sub-quarter-DIP moves don't need a repaint.
		if (Math.Abs(ad.lineY - y) > 0.25d) {
			ad.lineY = y;
			ad.InvalidateVisual();
		}
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

public sealed class CollectionViewGroupNameConverter : IValueConverter {
	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		value is CollectionViewGroup group ? group.Name?.ToString() ?? "" : "";

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
		throw new NotSupportedException();
}
