using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace X32VolumeHijacker;

/// <summary>PNG button glyphs embedded in the main assembly at build time.</summary>
internal static class ButtonIconResources {
	static readonly Lazy<Bitmap> AddLazy = new(() => LoadBitmap("add.png"));
	static readonly Lazy<Bitmap> CloseLazy = new(() => LoadBitmap("close.png"));
	static readonly Lazy<Bitmap> DeleteLazy = new(() => LoadBitmap("delete.png"));

	public static Image Add => AddLazy.Value;
	public static Image Close => CloseLazy.Value;
	public static Image Delete => DeleteLazy.Value;

	static Bitmap LoadBitmap(string fileName) {
		Assembly asm = typeof(ButtonIconResources).Assembly;
		string needle = "buttons." + fileName;
		string? resourceName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(needle, StringComparison.OrdinalIgnoreCase));
		if (resourceName == null)
			throw new InvalidOperationException($"Embedded resource not found for '{fileName}'. Available: {string.Join(", ", asm.GetManifestResourceNames())}");
		using Stream? stream = asm.GetManifestResourceStream(resourceName);
		if (stream == null)
			throw new InvalidOperationException($"Could not open manifest resource stream '{resourceName}'.");
		using Bitmap raw = new(stream);
		int target = Math.Clamp(SystemInformation.SmallIconSize.Width, 14, 22);
		if (raw.Width <= target && raw.Height <= target)
			return new Bitmap(raw);
		return new Bitmap(raw, new Size(target, target));
	}
}
