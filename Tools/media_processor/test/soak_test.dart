import 'dart:io' show Platform;
import 'dart:math';

import 'package:flutter_test/flutter_test.dart';

import '../lib/core/cancellation.dart';
import '../lib/core/log.dart';
import '../lib/core/ring_buffer.dart';
import '../lib/domain/task.dart';
import '../lib/services/media_processor.dart';
import 'helpers/metrics.dart';
import 'helpers/rig.dart';

int get _seed => int.tryParse(Platform.environment['TEST_SEED'] ?? '') ?? 1;
int get _soakMs => int.tryParse(Platform.environment['SOAK_MS'] ?? '') ?? 400;

void main() {
  test('sustained mixed workload keeps memory bounded and state consistent',
      () async {
    final random = Random(_seed);
    final rig = ProcessorRig(
      config: const MediaProcessorConfig(enableHardware: false),
      logCapacity: 128,
    );
    final cache = LruCache<int, int>(64);
    final aggregator =
        ProgressAggregator(const {'video': 0.7, 'audio': 0.3});
    final stopwatch = Stopwatch()..start();
    var iterations = 0;
    var tasks = 0;
    var operations = 0;
    var lastProgress = 0.0;

    while (stopwatch.elapsedMilliseconds < _soakMs) {
      for (var burst = 0; burst < 40; burst++) {
        rig.logs.log(
          LogLevel.values[random.nextInt(LogLevel.values.length)],
          'soak',
          'event $iterations-$burst',
        );
        operations++;
      }
      if (iterations % 4 == 0) rig.scheduler.advance();

      final key = random.nextInt(200);
      cache.put(key, iterations);
      cache.get(random.nextInt(200));
      operations += 2;

      final progress = aggregator.update(
        random.nextBool() ? 'video' : 'audio',
        random.nextDouble(),
      );
      expect(progress, greaterThanOrEqualTo(lastProgress));
      lastProgress = progress;
      operations++;
      if (iterations % 64 == 0) {
        aggregator.reset();
        lastProgress = 0.0;
      }

      final token = CancellationToken();
      final subscription = token.onCancel(() {});
      subscription.cancel();
      if (random.nextBool()) token.cancel();
      token.dispose();
      expect(token.listenerCount, 0);
      operations += 2;

      if (iterations % 25 == 0) {
        final result =
            await rig.processor.execute(rig.request(overwrite: true));
        expect(result.status, TaskStatus.completed);
        tasks++;
        operations++;
      }

      iterations++;
    }
    stopwatch.stop();
    rig.scheduler.advance();

    expect(rig.logs.length, lessThanOrEqualTo(rig.logs.capacity));
    expect(cache.length, lessThanOrEqualTo(64));
    expect(rig.processor.probeCacheStats.lookups, greaterThan(0));
    expect(tasks, greaterThan(0));
    expect(rig.runner.requests, isNotEmpty);
    final seconds = stopwatch.elapsedMicroseconds / 1000000.0;
    recordMetric('soak', {
      'duration_ms': stopwatch.elapsedMilliseconds,
      'iterations': iterations,
      'tasks': tasks,
      'operations': operations,
      'ops_per_sec': (operations / seconds).round(),
      'log_length': rig.logs.length,
      'log_capacity': rig.logs.capacity,
      'probe_hit_rate':
          double.parse(rig.processor.probeCacheStats.hitRate.toStringAsFixed(4)),
    });
    await rig.dispose();
  });
}
