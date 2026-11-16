import 'dart:async';

import 'ring_buffer.dart';

enum LogLevel { debug, info, warning, error }

/// Structured, machine-readable log record. UI rendering and error decisions
/// use [level]/[source], never emoji or substring matching.
final class LogEvent {
  const LogEvent({
    required this.timestamp,
    required this.level,
    required this.source,
    required this.message,
  });

  final DateTime timestamp;
  final LogLevel level;
  final String source;
  final String message;

  String format() =>
      '${timestamp.toIso8601String()} '
      '[${level.name.toUpperCase().padRight(7)}] '
      '[$source] $message';
}

/// Injectable clock/timer seam. Production uses real timers; verification
/// uses a manual scheduler so batching behaviour is deterministic.
abstract interface class FlushScheduler {
  void scheduleAfter(Duration delay, void Function() task);
  DateTime now();
}

final class SystemFlushScheduler implements FlushScheduler {
  const SystemFlushScheduler();

  @override
  void scheduleAfter(Duration delay, void Function() task) => Timer(delay, task);

  @override
  DateTime now() => DateTime.now();
}

/// Bounded, batched log store.
///
/// Append is O(1). Hundreds of events within one [flushInterval] produce a
/// single revision notification, so the UI rebuilds at most ~30 times per
/// second regardless of process output volume.
final class LogStore {
  LogStore({
    required FlushScheduler scheduler,
    int capacity = 2048,
    Duration flushInterval = const Duration(milliseconds: 33),
  })  : assert(capacity > 0),
        _scheduler = scheduler,
        _buffer = RingBuffer<LogEvent>(capacity),
        _flushInterval = flushInterval;

  final FlushScheduler _scheduler;
  final RingBuffer<LogEvent> _buffer;
  final Duration _flushInterval;
  final StreamController<int> _revisions =
      StreamController<int>.broadcast(sync: true);

  bool _dirty = false;
  bool _flushScheduled = false;
  bool _disposed = false;
  int _revision = 0;

  Stream<int> get revisions => _revisions.stream;
  int get revision => _revision;
  int get length => _buffer.length;
  int get capacity => _buffer.capacity;
  bool get hasPendingFlush => _dirty;

  LogEvent operator [](int index) => _buffer[index];
  Iterable<LogEvent> entries() => _buffer.values();

  void log(LogLevel level, String source, String message) {
    if (_disposed) return;
    _buffer.add(LogEvent(
      timestamp: _scheduler.now(),
      level: level,
      source: source,
      message: message,
    ));
    _dirty = true;
    _scheduleFlush();
  }

  void debug(String source, String message) =>
      log(LogLevel.debug, source, message);
  void info(String source, String message) =>
      log(LogLevel.info, source, message);
  void warning(String source, String message) =>
      log(LogLevel.warning, source, message);
  void error(String source, String message) =>
      log(LogLevel.error, source, message);

  void _scheduleFlush() {
    if (_flushScheduled || _disposed) return;
    _flushScheduled = true;
    _scheduler.scheduleAfter(_flushInterval, () {
      _flushScheduled = false;
      flush();
    });
  }

  /// Coalesces all pending appends into one notification. Safe to call from
  /// tests, shutdown paths and before exporting.
  void flush() {
    if (_disposed || !_dirty) return;
    _dirty = false;
    _revision++;
    _revisions.add(_revision);
  }

  /// Text form of the retained window, for diagnostics and export.
  /// The optional filter matches the level filter shown in the UI.
  String exportText({LogLevel? level}) {
    final buffer = StringBuffer();
    for (final event in _buffer.values()) {
      if (level == null || event.level == level) {
        buffer.writeln(event.format());
      }
    }
    return buffer.toString();
  }

  void clear() {
    _buffer.clear();
    _dirty = true;
    flush();
  }

  Future<void> dispose() async {
    if (_disposed) return;
    flush();
    _disposed = true;
    await _revisions.close();
  }
}
