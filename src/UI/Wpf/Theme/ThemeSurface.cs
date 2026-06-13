using System.Windows;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WindowsOscVolumeControl.UI.Wpf.Theme;

/// <summary>
/// Resolves the opaque window surface brush that Fluent "card" tints composite over.
/// Translucent Fluent brushes (e.g. <c>ExpanderHeaderBackground</c>) only read as the
/// expected card colour once stacked on top of the solid window background; overlays that
/// float above scrolling content (the ConfigWindow footer, the tray menu) need that same
/// opaque base to match a real card instead of lightening whatever shows through.
/// </summary>
internal static class ThemeSurface {
	// Window Background varies by .NET/WPF Fluent version; probe the well-known keys after the live style.
	static readonly object[] _fallbackBackgroundKeys = [
		"WindowBackground",
		"WindowBackgroundBrush",
		"ApplicationBackground",
		"ApplicationBackgroundBrush",
		"SolidBackgroundFillColorBase",
		"SolidBackgroundFillColorBaseBrush",
		"SolidBackgroundFillColorBaseAlt",
		"SolidBackgroundFillColorBaseAltBrush",
		"LayerFillColorDefault",
		"LayerFillColorDefaultBrush",
		"LayerFillColorAlt",
		"LayerFillColorAltBrush",
	];

	internal static Brush resolveOpaqueWindowSurfaceBrush(FrameworkElement element) {
		// 1) Prefer the actual Window style Background (Fluent theme).
		if (element.TryFindResource(typeof(Window)) is Style windowStyle) {
			foreach (SetterBase sb in windowStyle.Setters) {
				if (sb is not Setter s)
					continue;
				if (s.Property != System.Windows.Controls.Control.BackgroundProperty)
					continue;
				if (tryResolveBrushFromSetterValue(element, s.Value) is { } b1)
					return opacifyBrush(b1);
			}
		}

		// 2) Try common Fluent keys (varies by .NET/WPF version).
		foreach (object key in _fallbackBackgroundKeys) {
			if (element.TryFindResource(key) is Brush b2)
				return opacifyBrush(b2);
			if (element.TryFindResource(key) is Color c)
				return new SolidColorBrush(Color.FromArgb(255, c.R, c.G, c.B));
		}

		throw new InvalidOperationException(
			"Failed to resolve an opaque theme surface background brush. " +
			"Expected a Window Background setter or one of the common Fluent theme resource keys.");
	}

	static Brush? tryResolveBrushFromSetterValue(FrameworkElement element, object? value) {
		if (value is Brush b)
			return b;

		// Fluent theme uses DynamicResource in style setters.
		if (value is DynamicResourceExtension dre && element.TryFindResource(dre.ResourceKey) is Brush db)
			return db;

		return null;
	}

	static Brush opacifyBrush(Brush brush) {
		if (brush is SolidColorBrush scb) {
			Color c = scb.Color;
			if (c.A == 255)
				return brush;
			SolidColorBrush clone = new(Color.FromArgb(255, c.R, c.G, c.B));
			clone.Freeze();
			return clone;
		}
		return brush;
	}
}
