/// A single-use, idempotent cancellation token.
///
/// Tokens are owned by one task and disposed with it. [onCancel] returns a
/// subscription so short-lived listeners can unregister, which prevents
/// completed process runs from being retained by a task-long token.
final class CancelSubscription {
  CancelSubscription._(this._owner, this._callback);

  final CancellationToken _owner;
  final void Function() _callback;
  bool _active = true;

  bool get isActive => _active;

  void cancel() {
    if (!_active) return;
    _active = false;
    _owner._unsubscribe(_callback);
  }
}

final class CancellationToken {
  CancellationToken();

  bool _cancelled = false;
  bool _disposed = false;
  final List<void Function()> _listeners = [];

  bool get isCancelled => _cancelled;
  bool get isDisposed => _disposed;
  int get listenerCount => _listeners.length;

  void cancel() {
    if (_cancelled || _disposed) return;
    _cancelled = true;
    final listeners = List<void Function()>.of(_listeners);
    _listeners.clear();
    for (final listener in listeners) {
      listener();
    }
  }

  CancelSubscription onCancel(void Function() callback) {
    if (_cancelled) {
      callback();
      return CancelSubscription._(this, callback).._active = false;
    }
    if (_disposed) {
      return CancelSubscription._(this, callback).._active = false;
    }
    _listeners.add(callback);
    return CancelSubscription._(this, callback);
  }

  void _unsubscribe(void Function() callback) => _listeners.remove(callback);

  void dispose() {
    _disposed = true;
    _listeners.clear();
  }
}
