import '../core/outcome.dart';
import 'encoding.dart';
import 'media.dart';
import 'output_plan.dart';

enum TaskKind { full, preview }
enum TaskStatus { idle, running, completed, failed, cancelled }
enum OutputKind { video, audio }
enum StageId { probe, video, audio }

final class TaskRequest {
  const TaskRequest({
    required this.kind,
    required this.inputPath,
    required this.container,
    required this.quality,
    this.outputResolution = OutputResolution.follow,
    this.range,
    this.overwrite = false,
    this.maxConcurrentEncodes = 2,
  });

  final TaskKind kind;
  final String inputPath;
  final ContainerFormat container;
  final QualityPreset quality;
  final OutputResolution outputResolution;
  final ClipRange? range;
  final bool overwrite;
  final int maxConcurrentEncodes;

  bool validate() {
    if (inputPath.isEmpty) return false;
    if (maxConcurrentEncodes < 1 || maxConcurrentEncodes > 4) return false;
    if (kind == TaskKind.preview) {
      final r = range;
      if (r == null || !r.isValid) return false;
    }
    return true;
  }

  TaskRequest copyWith({ClipRange? range, QualityPreset? quality}) => TaskRequest(
        kind: kind,
        inputPath: inputPath,
        container: container,
        quality: quality ?? this.quality,
        outputResolution: outputResolution,
        range: range ?? this.range,
        overwrite: overwrite,
        maxConcurrentEncodes: maxConcurrentEncodes,
      );
}

final class OutputArtifact {
  const OutputArtifact({
    required this.path,
    required this.kind,
    required this.bytes,
  });

  final String path;
  final OutputKind kind;
  final int bytes;
}

final class StageReport {
  const StageReport({
    required this.stage,
    required this.encoder,
    required this.elapsed,
    required this.exitCode,
    required this.streamCopy,
    required this.attempts,
    this.command = const [],
    this.stderrTail = const [],
    this.drainTimedOut = false,
    this.killSucceeded = true,
  });

  final StageId stage;
  final String encoder;
  final Duration elapsed;
  final int? exitCode;
  final bool streamCopy;
  final int attempts;

  /// Full diagnostics: the exact command and the retained stderr tail, so a
  /// failed task can be diagnosed without scraping a bounded in-memory log.
  final List<String> command;
  final List<String> stderrTail;
  final bool drainTimedOut;
  final bool killSucceeded;
}

final class Progress {
  const Progress(this.fraction, {required this.stage, this.eta});

  final double fraction;
  final StageId stage;
  final Duration? eta;

  double get percent => fraction * 100;
}

final class TaskResult {
  const TaskResult({
    required this.status,
    required this.outputs,
    required this.stages,
    required this.elapsed,
    this.failure,
  });

  final TaskStatus status;
  final List<OutputArtifact> outputs;
  final List<StageReport> stages;
  final Duration elapsed;
  final Failure? failure;

  bool get succeeded => status == TaskStatus.completed;
}

final class TaskState {
  const TaskState({
    this.status = TaskStatus.idle,
    this.progress = const Progress(0, stage: StageId.probe),
    this.result,
  });

  final TaskStatus status;
  final Progress progress;
  final TaskResult? result;

  bool get isRunning => status == TaskStatus.running;

  TaskState copyWith({TaskStatus? status, Progress? progress, TaskResult? result}) =>
      TaskState(
        status: status ?? this.status,
        progress: progress ?? this.progress,
        result: result ?? this.result,
      );
}

/// Aggregates per-stage progress into one monotonically non-decreasing value.
///
/// All operations are O(1); each stage keeps its last maximum so a second
/// ffmpeg invocation can never make the progress bar move backwards.
final class ProgressAggregator {
  ProgressAggregator(this._weights) : assert(_weights.isNotEmpty);

  final Map<String, double> _weights;
  final Map<String, double> _values = {};
  double _last = 0;

  double get fraction => _last;

  void reset() {
    _values.clear();
    _last = 0;
  }

  double update(String stage, double raw) {
    final weight = _weights[stage];
    if (weight == null) return _last;
    final clamped = raw.clamp(0.0, 1.0).toDouble();
    final previous = _values[stage] ?? 0.0;
    _values[stage] = clamped > previous ? clamped : previous;
    var total = 0.0;
    for (final entry in _weights.entries) {
      total += entry.value * (_values[entry.key] ?? 0.0);
    }
    final next = total.clamp(0.0, 1.0).toDouble();
    if (next > _last) _last = next;
    return _last;
  }

  double complete(String stage) => update(stage, 1.0);
}
