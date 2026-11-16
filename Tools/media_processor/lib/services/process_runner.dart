import 'dart:async';
import 'dart:collection';
import 'dart:convert';
import 'dart:io';

import '../core/cancellation.dart';
import '../core/ring_buffer.dart';

enum OutputChannel { stdout, stderr }

final class ProcessRequest {
  const ProcessRequest({
    required this.label,
    required this.executable,
    required this.arguments,
    this.timeout = const Duration(minutes: 30),
    this.captureStdout = false,
    this.maxCapturedLines = 256,
    this.onLine,
    this.cancellation,
    this.workingDirectory,
    this.environment,
    this.groupKey = 'shared',
  });

  final String label;
  final String executable;
  final List<String> arguments;
  final Duration timeout;
  final bool captureStdout;
  final int maxCapturedLines;
  final void Function(String line, OutputChannel channel)? onLine;
  final CancellationToken? cancellation;
  final String? workingDirectory;
  final Map<String, String>? environment;

  /// Processes sharing a key form one cancellation scope.
  final String groupKey;
}

final class ProcessOutcome {
  const ProcessOutcome({
    required this.exitCode,
    required this.elapsed,
    required this.timedOut,
    required this.cancelled,
    required this.stdoutLines,
    required this.stderrTail,
    this.error,
    this.drainTimedOut = false,
    this.killSucceeded = true,
    this.pid,
    this.groupKey = 'shared',
    this.exitCodeKnown = true,
  });

  final int? exitCode;
  final Duration elapsed;
  final bool timedOut;
  final bool cancelled;
  final List<String> stdoutLines;
  final List<String> stderrTail;
  final String? error;

  /// True when the pipe drain watchdog fired: the stderr tail may be partial.
  final bool drainTimedOut;

  /// False when a requested kill could not be delivered.
  final bool killSucceeded;
  final int? pid;
  final String groupKey;

  /// False only when the watchdog had to give up waiting for the OS.
  final bool exitCodeKnown;

  bool get succeeded =>
      !timedOut && !cancelled && error == null && exitCode == 0;

  String describe() {
    if (timedOut) return 'timeout (exit ${exitCode ?? "unknown"})';
    if (cancelled) return 'cancelled';
    if (error != null) return 'error: $error';
    final tail = stderrTail.isEmpty ? '' : ': ${stderrTail.last}';
    return 'exit ${exitCode ?? "?"}$tail'
        '${drainTimedOut ? " [partial stderr]" : ""}';
  }
}

abstract interface class ProcessRunner {
  Future<ProcessOutcome> run(ProcessRequest request);
}

/// Cancellation scopes. A task disposes only its own group, so waveform / probe
/// processes are never killed by an unrelated media task shutdown.
final class ProcessGroup {
  final Map<String, Set<Process>> _groups = {};

  int get activeCount =>
      _groups.values.fold(0, (sum, set) => sum + set.length);
  Iterable<String> get keys => _groups.keys;

  void add(String key, Process process) =>
      _groups.putIfAbsent(key, () => <Process>{}).add(process);

  void remove(String key, Process process) {
    final group = _groups[key];
    if (group == null) return;
    group.remove(process);
    if (group.isEmpty) _groups.remove(key);
  }

  int killGroup(String key) => _kill(_groups[key]);

  int killAll() {
    var killed = 0;
    for (final group in List<Set<Process>>.of(_groups.values)) {
      killed += _kill(group);
    }
    return killed;
  }

  static int _kill(Set<Process>? group) {
    if (group == null) return 0;
    var killed = 0;
    for (final process in List<Process>.of(group)) {
      if (_SystemKill.force(process)) killed++;
    }
    return killed;
  }
}

final class _SystemKill {
  static bool force(Process process) {
    try {
      return process.kill(ProcessSignal.sigkill);
    } catch (_) {
      return false;
    }
  }

  /// Windows console builds can react to SIGINT; GUI builds cannot, so this is
  /// a best-effort graceful step before the forced kill.
  static bool graceful(Process process) {
    try {
      return process.kill(ProcessSignal.sigint);
    } catch (_) {
      return false;
    }
  }
}

/// FIFO semaphore bounding the number of simultaneous child processes.
final class _Semaphore {
  _Semaphore(this.max);

  final int max;
  final Queue<Completer<void>> _waiting = Queue<Completer<void>>();
  int _active = 0;
  int peak = 0;
  int waits = 0;

  Future<void> acquire() async {
    if (_active < max) {
      _allocate();
      return;
    }
    waits++;
    final completer = Completer<void>();
    _waiting.add(completer);
    await completer.future;
    _allocate();
  }

  void _allocate() {
    _active++;
    if (_active > peak) peak = _active;
  }

  void release() {
    _active--;
    if (_waiting.isNotEmpty) {
      _waiting.removeFirst().complete();
    }
  }
}

/// Production process runner.
///
/// Every child process gets: bounded concurrency, closed stdin with an error
/// handler, a watchdog that kills and then waits for the *real* exit code,
/// cancellation bound to its own group, malformed-tolerant decoding, fully
/// drained pipes with an observable watchdog result, and bounded retention.
final class SystemProcessRunner implements ProcessRunner {
  SystemProcessRunner({
    ProcessGroup? group,
    int maxConcurrentProcesses = 4,
  })  : assert(maxConcurrentProcesses > 0),
        _group = group,
        _semaphore = _Semaphore(maxConcurrentProcesses);

  final ProcessGroup? _group;
  final _Semaphore _semaphore;

  int get peakConcurrent => _semaphore.peak;
  int get queuedProcesses => _semaphore.waits;
  int get totalProcesses => _totalProcesses;
  int _totalProcesses = 0;

  @override
  Future<ProcessOutcome> run(ProcessRequest request) async {
    await _semaphore.acquire();
    try {
      return await _runIsolated(request);
    } finally {
      _semaphore.release();
    }
  }

  Future<ProcessOutcome> _runIsolated(ProcessRequest request) async {
    final stopwatch = Stopwatch()..start();
    final stdoutLines = <String>[];
    final stderrTail = RingBuffer<String>(request.maxCapturedLines);
    Process? process;
    CancelSubscription? cancelSubscription;
    StreamSubscription<String>? outSub;
    StreamSubscription<String>? errSub;
    Future<void>? stdoutDone;
    Future<void>? stderrDone;
    Timer? watchdog;
    bool timedOut = false;
    bool cancelled = false;
    bool drainTimedOut = false;
    bool killSucceeded = true;
    bool exitCodeKnown = false;
    String? error;
    int? exitCode;

    try {
      process = await Process.start(
        request.executable,
        request.arguments,
        workingDirectory: request.workingDirectory,
        environment: request.environment,
      );
      _totalProcesses++;
      _group?.add(request.groupKey, process);

      // Closing stdin can fail with EPIPE when a child exits immediately.
      // unawaited() only silences the lint, so the error must be handled.
      unawaited(process.stdin.close().catchError((Object _) {}));

      const decoder = Utf8Decoder(allowMalformed: true);
      const splitter = LineSplitter();

      final stdoutSub = process.stdout
          .transform(decoder)
          .transform(splitter)
          .listen((line) {
        if (request.captureStdout &&
            stdoutLines.length < request.maxCapturedLines) {
          stdoutLines.add(line);
        }
        request.onLine?.call(line, OutputChannel.stdout);
      });
      // asFuture must be attached before the stream can complete.
      stdoutDone = stdoutSub.asFuture<void>();
      outSub = stdoutSub;

      final stderrSub = process.stderr
          .transform(decoder)
          .transform(splitter)
          .listen((line) {
        stderrTail.add(line);
        request.onLine?.call(line, OutputChannel.stderr);
      });
      stderrDone = stderrSub.asFuture<void>();
      errSub = stderrSub;

      final activeProcess = process;
      cancelSubscription = request.cancellation?.onCancel(() {
        cancelled = true;
        if (!_SystemKill.graceful(activeProcess)) {
          killSucceeded = _SystemKill.force(activeProcess) && killSucceeded;
        }
      });
      if (request.cancellation?.isCancelled == true) {
        cancelled = true;
        killSucceeded = _SystemKill.force(activeProcess);
      }

      final exitCompleter = Completer<int?>();
      activeProcess.exitCode.then((code) {
        exitCodeKnown = true;
        if (!exitCompleter.isCompleted) exitCompleter.complete(code);
      }, onError: (Object e) {
        if (!exitCompleter.isCompleted) exitCompleter.completeError(e);
      });

      watchdog = Timer(request.timeout, () {
        timedOut = true;
        if (!_SystemKill.graceful(activeProcess)) {
          killSucceeded = _SystemKill.force(activeProcess) && killSucceeded;
        }
        // Give the OS a short window to report the real exit code rather than
        // fabricating -1; if it never arrives, the outcome records null.
        Timer(const Duration(seconds: 2), () {
          if (!exitCompleter.isCompleted) exitCompleter.complete(null);
        });
      });

      try {
        exitCode = await exitCompleter.future;
      } catch (e) {
        error = e.toString();
      } finally {
        watchdog.cancel();
      }

      try {
        await Future.wait<void>([stdoutDone, stderrDone])
            .timeout(const Duration(seconds: 5));
      } catch (_) {
        drainTimedOut = true;
      }
    } catch (e) {
      error = e.toString();
    } finally {
      cancelSubscription?.cancel();
      await outSub?.cancel();
      await errSub?.cancel();
      if (process != null) _group?.remove(request.groupKey, process);
      stopwatch.stop();
    }

    return ProcessOutcome(
      exitCode: exitCode,
      elapsed: stopwatch.elapsed,
      timedOut: timedOut,
      cancelled: cancelled,
      stdoutLines: List<String>.unmodifiable(stdoutLines),
      stderrTail: stderrTail.values().toList(growable: false),
      error: error,
      drainTimedOut: drainTimedOut,
      killSucceeded: killSucceeded,
      pid: process?.pid,
      groupKey: request.groupKey,
      exitCodeKnown: exitCodeKnown,
    );
  }
}
