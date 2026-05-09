using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsOscVolumeControl.Misc;

static class ProcessWorkingSetTrim {
	[DllImport("kernel32.dll", SetLastError = true)]
	static extern bool SetProcessWorkingSetSize(IntPtr hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

	/// <summary>Best-effort working-set trim; may increase paging. Isolated for easy removal.</summary>
	internal static void tryTrimWorkingSet() {
		try {
			using Process p = Process.GetCurrentProcess();
			_ = SetProcessWorkingSetSize(p.Handle, -1, -1);
		} catch {
			// best-effort
		}
	}
}
