namespace WindowsOscVolumeControl.Diagnostics;

public interface IStatusRegister {
	public event Action? changed;
	public IReadOnlyCollection<Type> getActiveStatusErrorTypes();
}

public sealed class StatusRegister<TError> : IStatusRegister where TError : StatusError {
	readonly object _lock = new();
	readonly HashSet<Type> _activeStatusErrorTypes = [];

	public event Action? changed;

	public IReadOnlyCollection<Type> activeStatusErrorTypes {
		get {
			lock (_lock)
				return _activeStatusErrorTypes.ToArray();
		}
	}

	public void setStatusError<TStatusError>(bool enabled)
		where TStatusError : TError {
		var statusErrorType = typeof(TStatusError);
		bool changedState;
		lock (_lock) {
			changedState = enabled
				? _activeStatusErrorTypes.Add(statusErrorType)
				: _activeStatusErrorTypes.Remove(statusErrorType);
		}
		if (changedState)
			changed?.Invoke();
	}

	public void clearAll() {
		bool changedState;
		lock (_lock) {
			changedState = _activeStatusErrorTypes.Count > 0;
			if (changedState)
				_activeStatusErrorTypes.Clear();
		}
		if (changedState)
			changed?.Invoke();
	}

	IReadOnlyCollection<Type> IStatusRegister.getActiveStatusErrorTypes() {
		lock (_lock)
			return _activeStatusErrorTypes.ToArray();
	}
}
