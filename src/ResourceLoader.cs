using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace WindowsOscVolumeControl;

/// <summary>Lazy-loaded tray icons from <c>Assets/Icon/app</c> and embedded button PNGs.</summary>
public sealed class ResourceLoader {
	readonly Lazy<Icon> _trayErrorGlobal;
	readonly Lazy<Icon> _trayErrorNetwork;
	readonly Lazy<Icon> _trayOk;
	readonly Lazy<Bitmap> _buttonAdd;
	readonly Lazy<Bitmap> _buttonClose;
	readonly Lazy<Bitmap> _buttonDelete;

	public ResourceLoader() {
		_trayErrorGlobal = new Lazy<Icon>(() => LoadTrayIconFile("error_global.ico"));
		_trayErrorNetwork = new Lazy<Icon>(() => LoadTrayIconFile("error_network.ico"));
		_trayOk = new Lazy<Icon>(() => LoadTrayIconFile("ok.ico"));
		_buttonAdd = new Lazy<Bitmap>(() => LoadEmbeddedButtonBitmap("add.png"));
		_buttonClose = new Lazy<Bitmap>(() => LoadEmbeddedButtonBitmap("close.png"));
		_buttonDelete = new Lazy<Bitmap>(() => LoadEmbeddedButtonBitmap("delete.png"));
	}

	public Icon TrayIconErrorGlobal => _trayErrorGlobal.Value;
	public Icon TrayIconErrorNetwork => _trayErrorNetwork.Value;
	public Icon TrayIconOk => _trayOk.Value;

	public Image ButtonAdd => _buttonAdd.Value;
	public Image ButtonClose => _buttonClose.Value;
	public Image ButtonDelete => _buttonDelete.Value;

	static Icon LoadTrayIconFile(string fileName) {
		string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Icon", "app", fileName);
		try {
			return new Icon(path);
		} catch {
			return SystemIcons.Application;
		}
	}

	static Bitmap LoadEmbeddedButtonBitmap(string fileName) {
		Assembly asm = typeof(ResourceLoader).Assembly;
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
