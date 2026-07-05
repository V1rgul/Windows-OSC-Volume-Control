namespace WindowsOscVolumeControl.Diagnostics;

public interface IStatusRegister {
	public event Action? changed;
	public IReadOnlyCollection<StatusError> getStatusErrors();
}

public sealed class StatusRegister<TError> : IStatusRegister where TError : StatusError {
	readonly object _lock = new();
	readonly HashSet<TError> _activeStatusErrors = [];

	public event Action? changed;

	public IReadOnlyCollection<TError> activeStatusErrors {
		get {
			lock (_lock)
				return _activeStatusErrors.ToArray();
		}
	}

	public void setStatusError(TError statusError, bool enabled) {
		ArgumentNullException.ThrowIfNull(statusError);
		bool changedState;
		lock (_lock) {
			changedState = enabled
				? _activeStatusErrors.Add(statusError)
				: _activeStatusErrors.Remove(statusError);
		}
		if (changedState)
			changed?.Invoke();
	}

	public void clearAll() {
		bool changedState;
		lock (_lock) {
			changedState = _activeStatusErrors.Count > 0;
			if (changedState)
				_activeStatusErrors.Clear();
		}
		if (changedState)
			changed?.Invoke();
	}

	IReadOnlyCollection<StatusError> IStatusRegister.getStatusErrors() {
		lock (_lock)
			return _activeStatusErrors.Cast<StatusError>().ToArray();
	}
}
