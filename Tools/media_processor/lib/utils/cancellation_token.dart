/// Lightweight cancellation token for async operation control.
/// Memory-safe: listeners are cleared after cancel to prevent leaks.
class CancellationToken {
  bool _isCancelled = false;
  List<void Function()>? _listeners = [];

  bool get isCancelled => _isCancelled;

  void cancel() {
    if (_isCancelled) return;
    _isCancelled = true;
    final listeners = _listeners;
    _listeners = null; // Release reference to allow GC
    if (listeners != null) {
      for (final listener in listeners) {
        listener();
      }
    }
  }

  void addListener(void Function() listener) {
    if (_isCancelled) {
      listener();
    } else {
      _listeners?.add(listener);
    }
  }

  /// Resets token for reuse (avoids allocation of new token)
  void reset() {
    _isCancelled = false;
    _listeners = [];
  }
}
