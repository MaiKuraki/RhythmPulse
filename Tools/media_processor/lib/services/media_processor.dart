import 'dart:async';

import 'package:path/path.dart' as p;

import '../core/cancellation.dart';
import '../core/log.dart';
import '../core/outcome.dart';
import '../core/ring_buffer.dart';
import '../domain/encoding.dart';
import '../domain/media.dart';
import '../domain/output_plan.dart';
import '../domain/task.dart';
import '../platform/encoders.dart';
import '../platform/host.dart';
import 'atomic_output.dart';
import 'binaries.dart';
import 'command_builder.dart';
import 'encoder_selector.dart';
import 'probe.dart';
import 'process_runner.dart';

final class MediaProcessorConfig {
  const MediaProcessorConfig({
    this.processTimeout = const Duration(minutes: 30),
    this.probeTimeout = const Duration(seconds: 30),
    this.probeCacheCapacity = 64,
    this.maxEncoderAttempts = 3,
    this.enableStreamCopy = true,
    this.enableHardware = true,
    this.enableFastStart = true,
    this.progressEmitDelta = 0.005,
  });

  final Duration processTimeout;
  final Duration probeTimeout;
  final int probeCacheCapacity;
  final int maxEncoderAttempts;
  final bool enableStreamCopy;
  final bool enableHardware;
  final bool enableFastStart;
  final double progressEmitDelta;
}

final class MediaProcessorMetrics {
  int tasks = 0;
  int stages = 0;
  int encoderFallbacks = 0;
  int streamCopies = 0;
  int cancellations = 0;
  int timeouts = 0;
  int bytesWritten = 0;

  Map<String, int> snapshot() => {
        'tasks': tasks,
        'stages': stages,
        'encoderFallbacks': encoderFallbacks,
        'streamCopies': streamCopies,
        'cancellations': cancellations,
        'timeouts': timeouts,
        'bytesWritten': bytesWritten,
      };
}

final class _StageOutcome {
  const _StageOutcome({
    required this.report,
    required this.target,
    this.failure,
  });

  final StageReport report;
  final OutputTarget target;
  final Failure? failure;

  bool get succeeded => failure == null;
}

final class _VideoAttempt {
  const _VideoAttempt({required this.copy, required this.encoder});

  final bool copy;
  final EncoderDescriptor? encoder;
}

/// Application-level media processing facade.
///
/// One call performs: validation -> binary verification -> cached probe ->
/// output planning -> encoder planning -> parallel/serial stage execution with
/// runtime fallback -> atomic all-or-nothing commit -> structured result.
final class MediaProcessor {
  MediaProcessor({
    required ProcessRunner runner,
    required BinaryLocator binaries,
    required HostCapabilities host,
    required LogStore logs,
    MediaProcessorConfig config = const MediaProcessorConfig(),
    MediaProbeService? probe,
    EncoderSelector? encoders,
    FfmpegCommandBuilder builder = const FfmpegCommandBuilder(),
    ProcessGroup? group,
    OutputPlanner planner = const OutputPlanner(),
  })  : _runner = runner,
        _binaries = binaries,
        _host = host,
        _logs = logs,
        _config = config,
        _builder = builder,
        _group = group,
        _planner = planner {
    _probe = probe ??
        MediaProbeService(
          runner: runner,
          binaries: binaries,
          cacheCapacity: config.probeCacheCapacity,
          timeout: config.probeTimeout,
        );
    _encoders = encoders ??
        EncoderSelector(runner: runner, binaries: binaries, logs: logs);
  }

  final ProcessRunner _runner;
  final BinaryLocator _binaries;
  final HostCapabilities _host;
  final LogStore _logs;
  final MediaProcessorConfig _config;
  final FfmpegCommandBuilder _builder;
  final ProcessGroup? _group;
  final OutputPlanner _planner;
  late final MediaProbeService _probe;
  late final EncoderSelector _encoders;
  final MediaProcessorMetrics metrics = MediaProcessorMetrics();

  bool _busy = false;
  bool _disposed = false;
  final List<OutputTarget> _activeTargets = [];
  static const String _taskGroupKey = 'media-task';

  bool get isBusy => _busy;
  CacheStats get probeCacheStats => _probe.cacheStats;
  int get cachedEncoderCount => _encoders.cachedEncoderCount;

  Future<TaskResult> execute(
    TaskRequest request, {
    CancellationToken? cancellation,
    void Function(Progress progress)? onProgress,
  }) async {
    if (_disposed) {
      return _finished(TaskStatus.failed, const [], const [], Duration.zero,
          const Failure(FailureKind.internal, 'MediaProcessor is disposed'));
    }
    if (_busy) {
      return _finished(TaskStatus.failed, const [], const [], Duration.zero,
          const Failure(FailureKind.busy, 'A media task is already running'));
    }
    _busy = true;
    final stopwatch = Stopwatch()..start();
    metrics.tasks++;
    try {
      return await _execute(request, cancellation, onProgress, stopwatch);
    } finally {
      _busy = false;
      _activeTargets.clear();
      stopwatch.stop();
    }
  }

  Future<TaskResult> _execute(
    TaskRequest request,
    CancellationToken? cancellation,
    void Function(Progress progress)? onProgress,
    Stopwatch stopwatch,
  ) async {
    if (!request.validate()) {
      return _fail(FailureKind.invalidInput, 'Invalid task request', stopwatch);
    }

    final binaryCheck = await _binaries.verify(_runner);
    if (binaryCheck.isErr) {
      return _failWith(binaryCheck.failureOrNull!, stopwatch);
    }

    final probeResult = await _probe.inspect(request.inputPath);
    if (probeResult.isErr) return _failWith(probeResult.failureOrNull!, stopwatch);
    final media = probeResult.valueOrNull!;

    if (media.hasVideo && media.resolution.isEmpty) {
      return _fail(FailureKind.probeFailed,
          'Video stream has no dimensions', stopwatch);
    }

    ClipRange? range;
    if (request.kind == TaskKind.preview) {
      range = request.range!.clampedTo(media.duration.inMilliseconds);
      if (!range.isValid || media.duration == Duration.zero) {
        return _fail(FailureKind.invalidInput,
            'Preview range is outside the media duration', stopwatch);
      }
    }

    final profile = ProfileRegistry.of(request.container);
    final plan = OutputPlan.build(media, request.outputResolution);
    final targetResolution = plan.target;
    final needsTransform = plan.transform || media.hdr || range != null;

    final kinds = <OutputKind>[
      if (media.hasVideo) OutputKind.video,
      if (media.hasAudio) OutputKind.audio,
    ];
    if (kinds.isEmpty) {
      return _fail(FailureKind.unsupported, 'No processable stream', stopwatch);
    }

    final outputDirectory = p.dirname(request.inputPath);
    final writable =
        await AtomicOutput.validateWritable(outputDirectory);
    if (writable.isErr) return _failWith(writable.failureOrNull!, stopwatch);

    final targets = await _planner.planAsync(
      inputPath: request.inputPath,
      isPreview: request.kind == TaskKind.preview,
      profile: profile,
      overwrite: request.overwrite,
      kinds: kinds,
    );
    _activeTargets
      ..clear()
      ..addAll(targets);
    final targetByKind = <OutputKind, OutputTarget>{
      for (final t in targets) t.kind: t,
    };

    final copyVideo = _config.enableStreamCopy &&
        media.canCopyVideoFor(profile, needsTransform: needsTransform);
    final copyAudio = _config.enableStreamCopy &&
        range == null &&
        media.canCopyAudioFor(profile);

    final videoWeight = media.hasVideo && media.hasAudio ? 0.75 : 1.0;
    final weights = <String, double>{
      if (media.hasVideo) 'video': videoWeight,
      if (media.hasAudio) 'audio': 1.0 - (media.hasVideo ? videoWeight : 0.0),
    };
    final aggregator = ProgressAggregator(weights);
    var lastEmitted = 0.0;
    void emit(double fraction, StageId stage) {
      if (fraction < 1.0 &&
          fraction - lastEmitted < _config.progressEmitDelta) {
        return;
      }
      if (fraction <= lastEmitted) return;
      lastEmitted = fraction;
      onProgress?.call(Progress(fraction, stage: stage));
    }

    var videoCandidates = const <EncoderDescriptor>[];
    if (media.hasVideo) {
      final plan = await _encoders.plan(profile.video, _host);
      if (plan.isErr) return _failWith(plan.failureOrNull!, stopwatch);
      videoCandidates = plan.valueOrNull!
          .where((e) => _config.enableHardware || !e.isHardware)
          // A transform that produces system-memory frames cannot feed an
          // encoder that requires device frames throughout the pipeline.
          .where((e) => !(needsTransform && e.requiresDeviceFrames))
          .toList(growable: false);
      if (videoCandidates.isEmpty) {
        return _fail(FailureKind.encoderUnavailable,
            'No encoder available for ${profile.video.name}', stopwatch);
      }
    }

    if (cancellation?.isCancelled == true) {
      await AtomicOutput.abortAll(targets);
      return _cancelled(stopwatch);
    }

    Future<_StageOutcome> runVideo() => _runVideoStage(
          request: request,
          media: media,
          profile: profile,
          range: range,
          targetResolution: targetResolution,
          candidates: videoCandidates,
          streamCopy: copyVideo,
          target: targetByKind[OutputKind.video]!,
          aggregator: aggregator,
          emit: emit,
          cancellation: cancellation,
        );

    Future<_StageOutcome> runAudio() => _runAudioStage(
          request: request,
          media: media,
          profile: profile,
          range: range,
          targetResolution: targetResolution,
          streamCopy: copyAudio,
          target: targetByKind[OutputKind.audio]!,
          aggregator: aggregator,
          emit: emit,
          cancellation: cancellation,
        );

    final parallel = request.maxConcurrentEncodes > 1 && kinds.length > 1;
    final reports = <StageReport>[];
    _StageOutcome? videoResult;
    _StageOutcome? audioResult;
    if (parallel) {
      final results = await Future.wait([
        if (media.hasVideo) runVideo(),
        if (media.hasAudio) runAudio(),
      ]);
      var index = 0;
      if (media.hasVideo) videoResult = results[index++];
      if (media.hasAudio) audioResult = results[index];
    } else {
      if (media.hasVideo) videoResult = await runVideo();
      if (media.hasAudio && (videoResult?.succeeded ?? true)) {
        audioResult = await runAudio();
      }
    }
    if (videoResult != null) reports.add(videoResult.report);
    if (audioResult != null) reports.add(audioResult.report);
    metrics.stages += reports.length;

    final failure = videoResult?.failure ?? audioResult?.failure;
    if (cancellation?.isCancelled == true) {
      await AtomicOutput.abortAll(targets);
      metrics.cancellations++;
      return _finished(
          TaskStatus.cancelled, const [], reports, stopwatch.elapsed, null);
    }
    if (failure != null) {
      await AtomicOutput.abortAll(targets);
      _logs.error('task', 'Failed: ${failure.message}');
      return _finished(
          TaskStatus.failed, const [], reports, stopwatch.elapsed, failure);
    }

    final commit = await AtomicOutput.commitAll(targets,
        overwrite: request.overwrite);
    if (commit.isErr) {
      return _finished(TaskStatus.failed, const [], reports,
          stopwatch.elapsed, commit.failureOrNull);
    }
    final outputs = commit.valueOrNull!;
    final totalBytes = outputs.fold<int>(0, (sum, o) => sum + o.bytes);
    metrics.bytesWritten += totalBytes;
    _logs.info('task',
        '${request.kind.name} completed in ${stopwatch.elapsedMilliseconds} ms, '
        '${outputs.length} outputs, $totalBytes bytes');
    for (final output in outputs) {
      _logs.info('output', '${output.path} (${output.bytes} bytes)');
    }
    emit(1.0, StageId.video);
    return _finished(TaskStatus.completed, outputs, reports,
        stopwatch.elapsed, null);
  }

  Future<_StageOutcome> _runVideoStage({
    required TaskRequest request,
    required MediaInfo media,
    required EncodingProfile profile,
    required ClipRange? range,
    required Resolution targetResolution,
    required List<EncoderDescriptor> candidates,
    required bool streamCopy,
    required OutputTarget target,
    required ProgressAggregator aggregator,
    required void Function(double, StageId) emit,
    CancellationToken? cancellation,
  }) async {
    final attemptPlan = <_VideoAttempt>[
      if (streamCopy) const _VideoAttempt(copy: true, encoder: null),
      for (final candidate in candidates)
        _VideoAttempt(copy: false, encoder: candidate),
    ];
    var attemptCount = 0;
    Failure? lastFailure;
    var lastEncoder = 'copy';
    List<String> lastCommand = const [];
    List<String> lastStderr = const [];
    var lastDrainTimedOut = false;
    var lastKillSucceeded = true;
    int? lastExitCode;
    var lastElapsed = Duration.zero;

    for (final attempt in attemptPlan) {
      if (attemptCount >= _config.maxEncoderAttempts) break;
      attemptCount++;
      if (attemptCount > 1) {
        metrics.encoderFallbacks++;
        _logs.warning('video',
            'Falling back to ${attempt.encoder?.id ?? "re-encode"}');
      }
      final encoder = attempt.encoder;
      final spec = CommandSpec(
        input: request.inputPath,
        output: target.partPath,
        media: media,
        profile: profile,
        quality: request.quality,
        targetResolution: targetResolution,
        kind: OutputKind.video,
        threads: _host.planFor(encoder?.vendor ?? GpuVendor.software,
            maxConcurrencyOverride: request.maxConcurrentEncodes),
        range: range,
        encoder: encoder,
        streamCopy: attempt.copy,
        fastStart: _config.enableFastStart,
      );
      final command = _builder.build(spec);
      final stopwatch = Stopwatch()..start();
      _logs.info('video',
          'Start (${attempt.copy ? "stream copy" : encoder!.id}, '
          'quality ${request.quality.name})');
      final outcome = await _runner.run(ProcessRequest(
        label: 'video',
        executable: _binaries.binaries.ffmpeg,
        arguments: command,
        timeout: _config.processTimeout,
        cancellation: cancellation,
        groupKey: _taskGroupKey,
        onLine: (line, channel) =>
            _handleLine(line, channel, 'video', media, range, aggregator, emit),
      ));
      stopwatch.stop();
      lastEncoder = attempt.copy ? 'copy' : encoder!.id;
      lastCommand = command;
      lastStderr = outcome.stderrTail;
      lastDrainTimedOut = outcome.drainTimedOut;
      lastKillSucceeded = outcome.killSucceeded;
      lastExitCode = outcome.exitCode;
      lastElapsed = stopwatch.elapsed;
      final report = StageReport(
        stage: StageId.video,
        encoder: lastEncoder,
        elapsed: lastElapsed,
        exitCode: lastExitCode,
        streamCopy: attempt.copy,
        attempts: attemptCount,
        command: command,
        stderrTail: outcome.stderrTail,
        drainTimedOut: outcome.drainTimedOut,
        killSucceeded: outcome.killSucceeded,
      );
      if (outcome.succeeded) {
        aggregator.complete('video');
        emit(aggregator.fraction, StageId.video);
        _logs.info('video',
            'Completed in ${stopwatch.elapsedMilliseconds} ms ($attemptCount attempt(s))');
        if (attempt.copy) metrics.streamCopies++;
        return _StageOutcome(report: report, target: target);
      }
      lastFailure = _failureFor(outcome);
      if (lastFailure.kind == FailureKind.cancelled ||
          lastFailure.kind == FailureKind.timeout) {
        if (lastFailure.kind == FailureKind.timeout) metrics.timeouts++;
        await AtomicOutput.abort(target);
        return _StageOutcome(
            report: report, target: target, failure: lastFailure);
      }
      _logs.warning('video',
          'Attempt $attemptCount/${attemptPlan.length} failed (${outcome.describe()})');
      await AtomicOutput.abort(target);
    }
    return _StageOutcome(
      report: StageReport(
        stage: StageId.video,
        encoder: lastEncoder,
        elapsed: lastElapsed,
        exitCode: lastExitCode,
        streamCopy: false,
        attempts: attemptCount,
        command: lastCommand,
        stderrTail: lastStderr,
        drainTimedOut: lastDrainTimedOut,
        killSucceeded: lastKillSucceeded,
      ),
      target: target,
      failure: lastFailure ??
          const Failure(FailureKind.processFailed, 'Video encoding failed'),
    );
  }

  Future<_StageOutcome> _runAudioStage({
    required TaskRequest request,
    required MediaInfo media,
    required EncodingProfile profile,
    required ClipRange? range,
    required Resolution targetResolution,
    required bool streamCopy,
    required OutputTarget target,
    required ProgressAggregator aggregator,
    required void Function(double, StageId) emit,
    CancellationToken? cancellation,
  }) async {
    // First attempt uses stream copy when codecs match; a failure retries
    // with a real encode instead of declaring the task failed.
    final attempts = streamCopy ? 2 : 1;
    Failure? lastFailure;
    for (var i = 0; i < attempts; i++) {
      final useCopy = streamCopy && i == 0;
      final spec = CommandSpec(
        input: request.inputPath,
        output: target.partPath,
        media: media,
        profile: profile,
        quality: request.quality,
        targetResolution: targetResolution,
        kind: OutputKind.audio,
        threads: _host.planFor(GpuVendor.software,
            maxConcurrencyOverride: 1),
        range: range,
        streamCopy: useCopy,
      );
      final command = _builder.build(spec);
      final stopwatch = Stopwatch()..start();
      _logs.info('audio', 'Start (${useCopy ? "stream copy" : "encode"})');
      final outcome = await _runner.run(ProcessRequest(
        label: 'audio',
        executable: _binaries.binaries.ffmpeg,
        arguments: command,
        timeout: _config.processTimeout,
        cancellation: cancellation,
        groupKey: _taskGroupKey,
        onLine: (line, channel) =>
            _handleLine(line, channel, 'audio', media, range, aggregator, emit),
      ));
      stopwatch.stop();
      final report = StageReport(
        stage: StageId.audio,
        encoder: useCopy ? 'copy' : profile.audio.name,
        elapsed: stopwatch.elapsed,
        exitCode: outcome.exitCode,
        streamCopy: useCopy,
        attempts: i + 1,
        command: command,
        stderrTail: outcome.stderrTail,
        drainTimedOut: outcome.drainTimedOut,
        killSucceeded: outcome.killSucceeded,
      );
      if (outcome.succeeded) {
        aggregator.complete('audio');
        emit(aggregator.fraction, StageId.audio);
        if (useCopy) metrics.streamCopies++;
        _logs.info('audio', 'Completed in ${stopwatch.elapsedMilliseconds} ms');
        return _StageOutcome(report: report, target: target);
      }
      lastFailure = _failureFor(outcome);
      if (lastFailure.kind == FailureKind.cancelled ||
          lastFailure.kind == FailureKind.timeout) {
        if (lastFailure.kind == FailureKind.timeout) metrics.timeouts++;
        await AtomicOutput.abort(target);
        return _StageOutcome(
            report: report, target: target, failure: lastFailure);
      }
      await AtomicOutput.abort(target);
    }
    return _StageOutcome(
      report: StageReport(
        stage: StageId.audio,
        encoder: profile.audio.name,
        elapsed: Duration.zero,
        exitCode: null,
        streamCopy: false,
        attempts: attempts,
      ),
      target: target,
      failure: lastFailure ??
          const Failure(FailureKind.processFailed, 'Audio encoding failed'),
    );
  }

  void _handleLine(
    String line,
    OutputChannel channel,
    String stageKey,
    MediaInfo media,
    ClipRange? range,
    ProgressAggregator aggregator,
    void Function(double, StageId) emit,
  ) {
    if (channel == OutputChannel.stdout) {
      final total = range?.durationSeconds ?? media.durationSeconds;
      final fraction = FfmpegProgress.parse(line, total);
      if (fraction != null) {
        final value = aggregator.update(stageKey, fraction);
        emit(value, stageKey == 'video' ? StageId.video : StageId.audio);
      }
    } else if (line.trim().isNotEmpty) {
      _logs.warning(stageKey, line);
    }
  }

  Failure _failureFor(ProcessOutcome outcome) {
    if (outcome.timedOut) {
      return const Failure(FailureKind.timeout, 'Encoder exceeded its timeout');
    }
    if (outcome.cancelled) {
      return const Failure(FailureKind.cancelled, 'Task cancelled');
    }
    return Failure(FailureKind.processFailed,
        'Encoder failed (${outcome.describe()})');
  }

  TaskResult _fail(FailureKind kind, String message, Stopwatch sw) =>
      _failWith(Failure(kind, message), sw);

  TaskResult _failWith(Failure failure, Stopwatch sw) {
    _logs.error('task', failure.message);
    return _finished(
        TaskStatus.failed, const [], const [], sw.elapsed, failure);
  }

  TaskResult _cancelled(Stopwatch sw) => _finished(
      TaskStatus.cancelled, const [], const [], sw.elapsed,
      const Failure(FailureKind.cancelled, 'Task cancelled'));

  TaskResult _finished(
    TaskStatus status,
    List<OutputArtifact> outputs,
    List<StageReport> stages,
    Duration elapsed,
    Failure? failure,
  ) =>
      TaskResult(
        status: status,
        outputs: outputs,
        stages: stages,
        elapsed: elapsed,
        failure: failure,
      );

  void invalidateCaches() {
    _probe.invalidateAll();
    _encoders.invalidate();
    _binaries.invalidate();
  }

  /// Kills every process owned by this processor's task group, then clears
  /// caches. Used on window close / application exit.
  Future<void> dispose() async {
    if (_disposed) return;
    _disposed = true;
    // Only this task's encoders are killed; waveform/probe processes belong
    // to the shared group and are left alone.
    _group?.killGroup(_taskGroupKey);
    await AtomicOutput.abortAll(List<OutputTarget>.of(_activeTargets));
    invalidateCaches();
  }
}
