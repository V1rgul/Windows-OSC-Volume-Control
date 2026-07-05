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
	IReadOnlyCollection<Type> _visibleStatusErrorTypes = [];
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

	public IReadOnlyCollection<Type> getVisibleStatusErrorTypes() {
		lock (_lock)
			return _visibleStatusErrorTypes;
	}

	void recomputeState() {
		MergedState? mergedChanged = null;
		bool detailsChanged = false;
		IReadOnlyCollection<Type>? visibleSnapshot = null;

		lock (_lock) {
			Type[] visibleStatusErrorTypes = _registersByKey.Values
				.SelectMany(static register => register.getActiveStatusErrorTypes())
				.ToArray();
			MergedState mergedState = mergeStatusErrors(visibleStatusErrorTypes);

			if (!visibleStatusErrorTypes.SequenceEqual(_visibleStatusErrorTypes)) {
				_visibleStatusErrorTypes = visibleStatusErrorTypes;
				detailsChanged = true;
				visibleSnapshot = _visibleStatusErrorTypes;
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

	static MergedState mergeStatusErrors(IEnumerable<Type> statusErrorTypes) {
		if (statusErrorTypes.Any(static t =>
				StatusError.isType<StatusError.MixerController.Network>(t)
				|| StatusError.isType<StatusError.Application.StartupHealthFault>(t)))
			return MergedState.NETWORK_ERROR;
		if (statusErrorTypes.Any())
			return MergedState.STARTING_OR_INVALID_CONFIG;
		return MergedState.OK;
	}
}
