/// Lightweight cancellation token for async operation control.
/// Listeners are cleared after cancel to prevent leaks.
class CancellationToken {
  bool _isCancelled = false;
  List<void Function()>? _listeners = [];

  bool get isCancelled => _isCancelled;

  void cancel() {
    if (_isCancelled) return;
    _isCancelled = true;
    final listeners = _listeners;
    _listeners = null;
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

  void removeListener(void Function() listener) {
    _listeners?.remove(listener);
  }

  void reset() {
    _isCancelled = false;
    _listeners = [];
  }
}
