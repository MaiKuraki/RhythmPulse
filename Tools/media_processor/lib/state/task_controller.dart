import 'dart:async';

import '../core/cancellation.dart';
import '../core/log.dart';
import '../domain/task.dart';
import '../services/media_processor.dart';

/// Explicit state machine with task-level mutual exclusion.
///
/// The controller is pure Dart: it exposes a state stream, so the UI never
/// imports the entry point and no `setState` runs after an unguarded await.
final class TaskController {
  TaskController({required MediaProcessor processor, required LogStore logs})
      : _processor = processor,
        _logs = logs;

  final MediaProcessor _processor;
  final LogStore _logs;
  final StreamController<TaskState> _states =
      StreamController<TaskState>.broadcast(sync: true);

  TaskState _state = const TaskState();
  CancellationToken? _token;
  bool _disposed = false;

  static const Map<TaskStatus, Set<TaskStatus>> _allowed = {
    TaskStatus.idle: {TaskStatus.running},
    TaskStatus.running: {
      TaskStatus.completed,
      TaskStatus.failed,
      TaskStatus.cancelled,
    },
    TaskStatus.completed: {TaskStatus.running},
    TaskStatus.failed: {TaskStatus.running},
    TaskStatus.cancelled: {TaskStatus.running},
  };

  TaskState get state => _state;
  Stream<TaskState> get states => _states.stream;
  bool get isRunning => _state.isRunning;

  Future<TaskResult> run(TaskRequest request) async {
    if (_disposed) {
      return const TaskResult(
        status: TaskStatus.failed,
        outputs: [],
        stages: [],
        elapsed: Duration.zero,
        failure: null,
      );
    }
    if (_state.isRunning) {
      return const TaskResult(
        status: TaskStatus.failed,
        outputs: [],
        stages: [],
        elapsed: Duration.zero,
        failure: null,
      );
    }

    final token = CancellationToken();
    _token = token;
    _transition(TaskStatus.running);
    _state = TaskState(
      status: TaskStatus.running,
      progress: const Progress(0, stage: StageId.probe),
    );
    _emit();

    try {
      final result = await _processor.execute(
        request,
        cancellation: token,
        onProgress: (progress) {
          if (_disposed || !_state.isRunning) return;
          _state = _state.copyWith(progress: progress);
          _emit();
        },
      );
      _transition(result.status);
      _state = TaskState(
        status: result.status,
        progress: _state.progress,
        result: result,
      );
      _emit();
      _logs.info('task',
          'Task ${result.status.name} in ${result.elapsedMilliseconds} ms');
      return result;
    } finally {
      token.dispose();
      _token = null;
    }
  }

  void cancel() {
    final token = _token;
    if (token == null) return;
    token.cancel();
    _logs.info('task', 'Cancellation requested');
  }

  void _transition(TaskStatus next) {
    final current = _state.status;
    if (current == next) return;
    if (!(_allowed[current]?.contains(next) ?? false)) {
      _logs.error('state', 'Illegal transition ${current.name} -> ${next.name}');
      throw StateError('Illegal task transition ${current.name} -> $next');
    }
  }

  void _emit() {
    if (_disposed || _states.isClosed) return;
    _states.add(_state);
  }

  Future<void> dispose() async {
    if (_disposed) return;
    cancel();
    _disposed = true;
    await _states.close();
  }
}

extension on TaskResult {
  int get elapsedMilliseconds => elapsed.inMilliseconds;
}
