using System.Diagnostics;

namespace WindowsOscVolumeControl.Diagnostics;

public sealed class StatusController {
	public enum MergedState {
		OK,
		STARTING_OR_INVALID_CONFIG,
		NETWORK_ERROR,
	}

	readonly object _lock = new();
	readonly Dictionary<string, IErrorList> _listsByKey = new(StringComparer.Ordinal);
	IReadOnlyCollection<Error> _visibleErrors = [];
	MergedState _mergedState = MergedState.OK;

	public event Action<MergedState>? mergedStateChanged;
	public event Action? visibleErrorsChanged;

	public void attach<TError>(string controllerKey, ErrorList<TError> errors)
		where TError : Error {
		ArgumentException.ThrowIfNullOrWhiteSpace(controllerKey);
		ArgumentNullException.ThrowIfNull(errors);

		lock (_lock) {
			if (!_listsByKey.TryAdd(controllerKey, errors))
				throw new InvalidOperationException($"StatusController already attached '{controllerKey}'.");
		}

		errors.changed += recomputeState;
		recomputeState();
	}

	public MergedState getMergedState() {
		lock (_lock)
			return _mergedState;
	}

	public IReadOnlyCollection<Error> getVisibleErrors() {
		lock (_lock)
			return _visibleErrors;
	}

	void recomputeState() {
		MergedState? mergedChanged = null;
		bool detailsChanged = false;
		IReadOnlyCollection<Error>? visibleSnapshot = null;

		lock (_lock) {
			Error[] visibleErrors = _listsByKey.Values
				.SelectMany(static list => list.getErrors())
				.ToArray();
			MergedState mergedState = mergeErrors(visibleErrors);

			if (!visibleErrors.SequenceEqual(_visibleErrors)) {
				_visibleErrors = visibleErrors;
				detailsChanged = true;
				visibleSnapshot = _visibleErrors;
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
					$"Visible errors changed ({visibleSnapshot?.Count ?? 0})");
			visibleErrorsChanged?.Invoke();
		}
	}

	static MergedState mergeErrors(IEnumerable<Error> errors) {
		if (errors.Any(static e => e is Error.MixerController.Network || e is Error.Application.StartupHealthFault))
			return MergedState.NETWORK_ERROR;
		if (errors.Any())
			return MergedState.STARTING_OR_INVALID_CONFIG;
		return MergedState.OK;
	}
}
