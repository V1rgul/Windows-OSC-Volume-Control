using System.Diagnostics;

namespace WindowsOscVolumeControl.Diagnostics;

public sealed class StatusController {
	public enum MergedState {
		OK,
		STARTING_OR_INVALID_CONFIG,
		NETWORK_ERROR,
	}

	readonly object _lock = new();
	readonly Dictionary<string, IStatusRegister> _registersByKey = new(StringComparer.Ordinal);
	IReadOnlyCollection<StatusError> _visibleStatusErrors = [];
	MergedState _mergedState = MergedState.OK;

	public event Action<MergedState>? mergedStateChanged;
	public event Action? visibleStatusErrorsChanged;

	public void attach<TError>(string controllerKey, StatusRegister<TError> statusRegister)
		where TError : StatusError {
		ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);
		ArgumentNullException.ThrowIfNull(statusRegister);

		lock (_lock) {
			if (!_registersByKey.TryAdd(controllerKey, statusRegister))
				throw new InvalidOperationException($"StatusController already attached '{controllerKey}'.");
		}

		statusRegister.changed += recomputeState;
		recomputeState();
	}

	public MergedState getMergedState() {
		lock (_lock)
			return _mergedState;
	}

	public IReadOnlyCollection<StatusError> getVisibleStatusErrors() {
		lock (_lock)
			return _visibleStatusErrors;
	}

	void recomputeState() {
		MergedState? mergedChanged = null;
		bool detailsChanged = false;
		IReadOnlyCollection<StatusError>? visibleSnapshot = null;

		lock (_lock) {
			StatusError[] visibleStatusErrors = _registersByKey.Values
				.SelectMany(static register => register.getStatusErrors())
				.ToArray();
			MergedState mergedState = mergeStatusErrors(visibleStatusErrors);

			if (!visibleStatusErrors.SequenceEqual(_visibleStatusErrors)) {
				_visibleStatusErrors = visibleStatusErrors;
				detailsChanged = true;
				visibleSnapshot = _visibleStatusErrors;
			}

			if (mergedState != _mergedState) {
				_mergedState = mergedState;
				mergedChanged = mergedState;
			}
		}

		bool traceInformation = AppTrace.StatusController.Switch.ShouldTrace(TraceEventType.Information);

		if (mergedChanged is MergedState merged) {
			if (traceInformation)
				AppTrace.StatusController.TraceEvent(
					TraceEventType.Information,
					0,
					$"Merged state changed to {merged}");
			mergedStateChanged?.Invoke(merged);
		}

		if (detailsChanged) {
			if (traceInformation)
				AppTrace.StatusController.TraceEvent(
					TraceEventType.Information,
					0,
					$"Visible status errors changed ({visibleSnapshot?.Count ?? 0})");
			visibleStatusErrorsChanged?.Invoke();
		}
	}

	static MergedState mergeStatusErrors(IEnumerable<StatusError> statusErrors) {
		if (statusErrors.Any(static e => e is StatusError.MixerController.Network || e is StatusError.Application.StartupHealthFault))
			return MergedState.NETWORK_ERROR;
		if (statusErrors.Any())
			return MergedState.STARTING_OR_INVALID_CONFIG;
		return MergedState.OK;
	}
}
