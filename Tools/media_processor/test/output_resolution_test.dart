import 'package:flutter_test/flutter_test.dart';

import '../lib/domain/output_plan.dart';
import '../lib/domain/task.dart';
import 'helpers/fakes.dart';
import 'helpers/rig.dart';

void main() {
  test('4K source with 1080p policy emits an exact scale filter', () async {
    final rig = ProcessorRig(byLabel: {
      'ffprobe': ScriptedOutcome(
          stdoutLines: [sampleProbeJson(width: 3840, height: 2160)]),
    });
    final result = await rig.processor.execute(
        rig.request(outputResolution: OutputResolution.p1080));
    expect(result.status, TaskStatus.completed);
    final video =
        rig.runner.requests.firstWhere((request) => request.label == 'video');
    expect(video.arguments[video.arguments.indexOf('-vf') + 1],
        contains('scale=1920:1080'));
    expect(result.stages.first.streamCopy, isFalse);
    await rig.dispose();
  });

  test('follow-source policy never scales and allows stream copy', () async {
    final rig = ProcessorRig(byLabel: {
      'ffprobe': ScriptedOutcome(
          stdoutLines: [sampleProbeJson(width: 3840, height: 2160)]),
    });
    final result = await rig.processor.execute(rig.request());
    expect(result.status, TaskStatus.completed);
    for (final request
        in rig.runner.requests.where((r) => r.label == 'video')) {
      expect(request.arguments.any((a) => a.startsWith('scale=')), isFalse);
    }
    await rig.dispose();
  });

  test('audio-only input never starts a video stage', () async {
    final rig = ProcessorRig(byLabel: {
      'ffprobe': ScriptedOutcome(
          stdoutLines: [sampleProbeJson(video: false, audio: true)]),
    });
    final result = await rig.processor.execute(
        rig.request(outputResolution: OutputResolution.p2160));
    expect(result.status, TaskStatus.completed);
    expect(rig.runner.countFor('video'), 0);
    expect(result.outputs.length, 1);
    expect(result.outputs.single.kind, OutputKind.audio);
    await rig.dispose();
  });
}
