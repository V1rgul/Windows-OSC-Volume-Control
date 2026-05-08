using System;
using System.Collections.Generic;

namespace WindowsOscVolumeControl.Misc;

public readonly record struct RttStatsSnapshot(
	int completedCount,
	int receivedCount,
	int? minMs,
	int? medianMs,
	int? maxMs);

public sealed class RttStatsAccumulator {
	readonly List<int> _samplesMs = [];

	public int completedCount { get; private set; }
	public int receivedCount { get; private set; }

	public void reset() {
		_samplesMs.Clear();
		completedCount = 0;
		receivedCount = 0;
	}

	/// <summary>Adds a probe result. Null means loss/timeout; non-null is an RTT sample in milliseconds.</summary>
	public void push(int? rttMs) {
		completedCount++;
		if (rttMs == null)
			return;
		int ms = rttMs.Value;
		if (ms < 0)
			return;
		receivedCount++;
		_samplesMs.Add(ms);
	}

	public RttStatsSnapshot snapshot() {
		if (_samplesMs.Count == 0)
			return new RttStatsSnapshot(completedCount, receivedCount, null, null, null);

		int[] sorted = _samplesMs.ToArray();
		Array.Sort(sorted);
		int min = sorted[0];
		int max = sorted[^1];
		int median = sorted.Length % 2 == 1
			? sorted[sorted.Length / 2]
			: sorted[(sorted.Length / 2) - 1];
		return new RttStatsSnapshot(completedCount, receivedCount, min, median, max);
	}
}

