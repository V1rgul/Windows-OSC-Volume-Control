using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace WindowsOscVolumeControl.Diagnostics;

internal static class TraceLogFileRotation {
	internal const string TruncationMarkerLine = "--- earlier log truncated ---";
	internal const long MAX_TRACE_LOG_FILE_BYTES = 1024 * 1024;

	internal static void writeSessionBanner(TextWriter writer, string logPath) {
		writer.WriteLine(
			Path.GetFullPath(logPath)
				+ " | file=min(Warning) | "
				+ DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture));
		writer.Flush();
	}

	/// <summary>Rewrite the log file to retain a tail when it exceeds <paramref name="maxBytes"/>.</summary>
	internal static void tryTruncateIfExceeds(string path, long maxBytes) {
		try {
			var fi = new FileInfo(path);
			if (!fi.Exists || fi.Length <= maxBytes)
				return;

			long tailBudget = Math.Min(maxBytes / 2, 1024 * 1024);
			using var readFs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
			long len = readFs.Length;
			long start = Math.Max(0, len - tailBudget);
			readFs.Position = start;
			int toRead = (int)Math.Min(int.MaxValue, len - start);
			var buffer = new byte[toRead];
			_ = readFs.ReadAtLeast(buffer, toRead);

			int cut = 0;
			for (int i = 0; i < buffer.Length && i < 4096; i++) {
				if (buffer[i] == (byte)'\n') {
					cut = i + 1;
					break;
				}
			}

			if (cut >= buffer.Length)
				cut = 0;

			string tail = Encoding.UTF8.GetString(buffer.AsSpan(cut));
			using var writeFs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
			using var w = new StreamWriter(writeFs, Encoding.UTF8);
			w.WriteLine(TruncationMarkerLine);
			w.Write(tail);
		} catch {
			// best-effort
		}
	}
}

internal sealed class RotatingFileTraceListener : TextWriterTraceListener {
	readonly string _path;
	readonly long _maxBytes;
	readonly object _gate = new();

	RotatingFileTraceListener(StreamWriter writer, string path, long maxBytes)
		: base(writer) {
		_path = path;
		_maxBytes = maxBytes;
	}

	internal static RotatingFileTraceListener createForNewSession(string path, long maxBytes) {
		var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
		var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
		TraceLogFileRotation.writeSessionBanner(writer, path);
		return new RotatingFileTraceListener(writer, path, maxBytes);
	}

	public override void Flush() {
		lock (_gate) {
			base.Flush();
		}
	}

	public override void Write(string? message) {
		lock (_gate) {
			base.Write(message);
			tryRotateIfNeededUnsynchronized();
		}
	}

	public override void WriteLine(string? message) {
		lock (_gate) {
			base.WriteLine(message);
			tryRotateIfNeededUnsynchronized();
		}
	}

	void tryRotateIfNeededUnsynchronized() {
		Flush();
		if (Writer is not StreamWriter sw || sw.BaseStream is not FileStream fs)
			return;
		if (fs.Length <= _maxBytes)
			return;

		sw.Dispose();
		TraceLogFileRotation.tryTruncateIfExceeds(_path, _maxBytes);
		var nextFs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
		Writer = new StreamWriter(nextFs, Encoding.UTF8) { AutoFlush = true };
	}
}
