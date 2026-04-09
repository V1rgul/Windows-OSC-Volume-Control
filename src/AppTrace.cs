using System.Diagnostics;

namespace WindowsOscVolumeControl;

static class AppTrace {
	public static readonly TraceSource Application = new(nameof(Application));
	public static readonly TraceSource BindingManager = new(nameof(BindingManager));
	public static readonly TraceSource KeyboardHook = new(nameof(KeyboardHook));
	public static readonly TraceSource OscTransport = new(nameof(OscTransport));
	public static readonly TraceSource StatusController = new(nameof(StatusController));
}
