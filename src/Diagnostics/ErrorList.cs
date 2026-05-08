namespace WindowsOscVolumeControl.Diagnostics;

public interface IErrorList {
	public event Action? changed;
	public IReadOnlyCollection<Error> getErrors();
}

public sealed class ErrorList<TError> : IErrorList where TError : Error {
	readonly object _lock = new();
	readonly HashSet<TError> _activeErrors = [];

	public event Action? changed;

	public IReadOnlyCollection<TError> activeErrors {
		get {
			lock (_lock)
				return _activeErrors.ToArray();
		}
	}

	public void setError(TError error, bool enabled) {
		ArgumentNullException.ThrowIfNull(error);
		bool changedState;
		lock (_lock) {
			changedState = enabled
				? _activeErrors.Add(error)
				: _activeErrors.Remove(error);
		}
		if (changedState)
			changed?.Invoke();
	}

	public void clearAll() {
		bool changedState;
		lock (_lock) {
			changedState = _activeErrors.Count > 0;
			if (changedState)
				_activeErrors.Clear();
		}
		if (changedState)
			changed?.Invoke();
	}

	IReadOnlyCollection<Error> IErrorList.getErrors() {
		lock (_lock)
			return _activeErrors.Cast<Error>().ToArray();
	}
}
