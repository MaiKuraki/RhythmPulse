import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;

import '../lib/core/cancellation.dart';
import '../lib/platform/host.dart';
import '../lib/services/process_runner.dart';
import 'helpers/metrics.dart';

String _ffmpeg() {
  final bin = p.join(Directory.current.path, 'data',
      'ffmpeg-release-full-shared', 'bin');
  final suffix = currentHostOs() == HostOs.windows ? '.exe' : '';
  return p.join(bin, 'ffmpeg$suffix');
}

void main() {
  test('bounded concurrency, graceful degradation and diagnostics', () async {
    final ffmpeg = _ffmpeg();
    if (!File(ffmpeg).existsSync()) {
      print('[METRIC] process_runner skipped (ffmpeg absent)');
      return;
    }
    final group = ProcessGroup();
    final runner = SystemProcessRunner(
        group: group, maxConcurrentProcesses: 2);

    // 12 concurrent short-lived processes must never exceed the cap.
    final futures = <Future<ProcessOutcome>>[];
    for (var i = 0; i < 12; i++) {
      futures.add(runner.run(ProcessRequest(
        label: 'burst-$i',
        executable: ffmpeg,
        arguments: const ['-hide_banner', '-version'],
        captureStdout: true,
        groupKey: 'burst',
        timeout: const Duration(seconds: 20),
      )));
    }
    final outcomes = await Future.wait(futures);
    expect(outcomes.every((o) => o.succeeded), isTrue);
    expect(runner.totalProcesses, 12);
    expect(runner.peakConcurrent, lessThanOrEqualTo(2));
    expect(runner.queuedProcesses, greaterThan(0));
    expect(group.activeCount, 0,
        reason: 'every process must be deregistered after completion');

    // An immediate child exit must not produce an unhandled stdin EPIPE.
    final bad = await runner.run(ProcessRequest(
      label: 'bad-args',
      executable: ffmpeg,
      arguments: const ['-hide_banner', '-definitely-not-an-option'],
      timeout: const Duration(seconds: 20),
    ));
    expect(bad.succeeded, isFalse);
    expect(bad.stderrTail, isNotEmpty);
    expect(bad.error, isNull);
    expect(bad.drainTimedOut, isFalse);

    // A watchdog kill records a real exit code (never a fabricated -1).
    final timedOut = await runner.run(ProcessRequest(
      label: 'timeout',
      executable: ffmpeg,
      arguments: const [
        '-hide_banner', '-loglevel', 'error',
        '-f', 'lavfi', '-i', 'sine=frequency=440:duration=20',
        '-f', 'null', '-',
      ],
      timeout: const Duration(milliseconds: 40),
    ));
    expect(timedOut.timedOut, isTrue);
    expect(timedOut.exitCodeKnown, isTrue,
        reason: 'the exit code must come from the OS, not a fabricated value');
    expect(timedOut.killSucceeded, isTrue);

    // Cancellation is scoped to one group key.
    final scopeRunner = SystemProcessRunner(group: group);
    final token = CancellationToken();
    final long = scopeRunner.run(ProcessRequest(
      label: 'long',
      executable: ffmpeg,
      arguments: const [
        '-hide_banner', '-loglevel', 'error',
        '-f', 'lavfi', '-i', 'sine=frequency=440:duration=20',
        '-f', 'null', '-',
      ],
      cancellation: token,
      groupKey: 'media-task',
      timeout: const Duration(seconds: 20),
    ));
    await Future<void>.delayed(const Duration(milliseconds: 30));
    token.cancel();
    final cancelled = await long;
    expect(cancelled.cancelled, isTrue);
    expect(cancelled.succeeded, isFalse);

    recordMetric('process_runner', {
      'concurrent_runs': 12,
      'max_concurrent': 2,
      'peak': runner.peakConcurrent,
      'queued': runner.queuedProcesses,
      'timeout_exit_code': timedOut.exitCode,
      'stderr_lines': bad.stderrTail.length,
    });
  });
}
