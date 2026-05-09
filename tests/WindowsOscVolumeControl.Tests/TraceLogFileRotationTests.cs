using System.Text;
using WindowsOscVolumeControl.Diagnostics;

namespace WindowsOscVolumeControl.Tests;

public sealed class TraceLogFileRotationTests {
	[Fact]
	public void tryTruncateIfExceeds_whenUnderLimit_doesNotRewriteFile() {
		string path = Path.Combine(Path.GetTempPath(), "wosc-rot-" + Guid.NewGuid().ToString("n") + ".log");
		try {
			const string body = "small log\n";
			File.WriteAllText(path, body, Encoding.UTF8);
			long lenBefore = new FileInfo(path).Length;

			TraceLogFileRotation.tryTruncateIfExceeds(path, 4096);

			Assert.Equal(lenBefore, new FileInfo(path).Length);
			Assert.Equal(body, File.ReadAllText(path, Encoding.UTF8));
			Assert.DoesNotContain(TraceLogFileRotation.TruncationMarkerLine, body);
		} finally {
			try {
				File.Delete(path);
			} catch {
				// ignore
			}
		}
	}

	[Fact]
	public void tryTruncateIfExceeds_whenOverLimit_writesMarkerAndRetainsTail() {
		string path = Path.Combine(Path.GetTempPath(), "wosc-rot-" + Guid.NewGuid().ToString("n") + ".log");
		try {
			const int maxBytes = 4096;
			string head = new string('H', 3000);
			string tailMarker = "TAIL_UNIQUE_" + Guid.NewGuid().ToString("n");
			string content = head + new string('M', maxBytes) + tailMarker + "\n";
			File.WriteAllText(path, content, Encoding.UTF8);
			long before = new FileInfo(path).Length;

			TraceLogFileRotation.tryTruncateIfExceeds(path, maxBytes);

			long after = new FileInfo(path).Length;
			Assert.True(after < before);
			string text = File.ReadAllText(path, Encoding.UTF8);
			Assert.Contains(TraceLogFileRotation.TruncationMarkerLine, text);
			Assert.Contains(tailMarker, text);
		} finally {
			try {
				File.Delete(path);
			} catch {
				// ignore
			}
		}
	}
}
