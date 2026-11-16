import 'package:flutter_test/flutter_test.dart';

import '../lib/domain/task.dart';
import '../lib/services/media_processor.dart';
import 'helpers/fakes.dart';
import 'helpers/rig.dart';

void main() {
  test('state stream publishes only legal transitions', () async {
    final rig = ProcessorRig(
        config: const MediaProcessorConfig(enableHardware: false));
    final statuses = <TaskStatus>[];
    final subscription = rig.tasks.states.listen((s) => statuses.add(s.status));
    final result = await rig.tasks.run(rig.request());
    expect(result.status, TaskStatus.completed);
    expect(statuses.first, TaskStatus.running);
    expect(statuses.last, TaskStatus.completed);
    expect(statuses.toSet(), {TaskStatus.running, TaskStatus.completed},
        reason: 'progress updates stay inside the running state');
    expect(rig.tasks.state.status, TaskStatus.completed);
    expect(rig.tasks.state.result?.outputs.length, 2);
    await subscription.cancel();
    await rig.dispose();
  });

  test('controller enforces mutual exclusion', () async {
    final rig = ProcessorRig(
      config: const MediaProcessorConfig(enableHardware: false),
      byLabel: {
        'video': const ScriptedOutcome(
            writeOutputBytes: 128, delay: Duration(milliseconds: 40)),
      },
    );
    final first = rig.tasks.run(rig.request());
    final second = rig.tasks.run(rig.request());
    final results = await Future.wait([first, second]);
    expect(results.where((r) => r.status == TaskStatus.completed).length, 1);
    expect(results.where((r) => r.status == TaskStatus.failed).length, 1);
    await rig.dispose();
  });

  test('cancelling an active task reaches the cancelled state', () async {
    final rig = ProcessorRig(
      config: const MediaProcessorConfig(enableHardware: false),
      byLabel: {
        'video': const ScriptedOutcome(
            writeOutputBytes: 128, delay: Duration(milliseconds: 40)),
        'audio': const ScriptedOutcome(
            writeOutputBytes: 64, delay: Duration(milliseconds: 40)),
      },
    );
    final future = rig.tasks.run(rig.request());
    await Future<void>.delayed(const Duration(milliseconds: 5));
    rig.tasks.cancel();
    final result = await future;
    expect(result.status, TaskStatus.cancelled);
    expect(rig.tasks.state.status, TaskStatus.cancelled);
    await rig.dispose();
  });

  test('dispose is idempotent and stops further work', () async {
    final rig = ProcessorRig();
    await rig.tasks.dispose();
    await rig.tasks.dispose();
    final result = await rig.tasks.run(rig.request());
    expect(result.status, TaskStatus.failed);
    await rig.dispose();
  });
}
