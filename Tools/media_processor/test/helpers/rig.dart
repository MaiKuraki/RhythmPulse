import '../../lib/core/log.dart';
import '../../lib/domain/encoding.dart';
import '../../lib/domain/media.dart';
import '../../lib/domain/output_plan.dart';
import '../../lib/domain/task.dart';
import '../../lib/platform/host.dart';
import '../../lib/services/encoder_selector.dart';
import '../../lib/services/media_processor.dart';
import '../../lib/services/process_runner.dart';
import '../../lib/services/probe.dart';
import '../../lib/services/waveform.dart';
import '../../lib/state/task_controller.dart';
import 'fakes.dart';

/// A complete, injected application graph with scripted process behaviour.
final class ProcessorRig {
  ProcessorRig({
    Map<String, ScriptedOutcome>? byLabel,
    ScriptedOutcome Function(ProcessRequest request, int index)? responder,
    MediaProcessorConfig config = const MediaProcessorConfig(),
    HostCapabilities? host,
    int logCapacity = 256,
  }) {
    fs = TestFs();
    scheduler = ManualScheduler();
    logs = LogStore(scheduler: scheduler, capacity: logCapacity);
    final scripts = <String, ScriptedOutcome>{
      'ffprobe': ScriptedOutcome(stdoutLines: [sampleProbeJson()]),
      'encoder-list': const ScriptedOutcome(
          stdoutLines: [' V....D libx264 libx264']),
      'video': const ScriptedOutcome(
        writeOutputBytes: 128,
        stdoutLines: ['out_time_us=5000000', 'progress=end'],
      ),
      'audio': const ScriptedOutcome(
        writeOutputBytes: 64,
        stdoutLines: ['progress=end'],
      ),
      'waveform': const ScriptedOutcome(writeOutputBytes: 32),
      ...?byLabel,
    };
    runner = FakeProcessRunner(byLabel: scripts, responder: responder);
    binaries = FakeBinaryLocator();
    this.host = host ??
        const HostCapabilities(
            os: HostOs.windows,
            architecture: CpuArchitecture.x64,
            logicalCores: 8);
    input = fs.file('input.mp4', bytes: 512);
    probe = MediaProbeService(runner: runner, binaries: binaries);
    encoders = EncoderSelector(
        runner: runner, binaries: binaries, logs: logs);
    processor = MediaProcessor(
      runner: runner,
      binaries: binaries,
      host: this.host,
      logs: logs,
      config: config,
      probe: probe,
      encoders: encoders,
    );
    tasks = TaskController(processor: processor, logs: logs);
    waveforms = WaveformService(
      runner: runner,
      binaries: binaries,
      logs: logs,
      cacheDirectory: fs.path('waveforms'),
    );
  }

  late final TestFs fs;
  late final ManualScheduler scheduler;
  late final LogStore logs;
  late final FakeProcessRunner runner;
  late final FakeBinaryLocator binaries;
  late final HostCapabilities host;
  late final String input;
  late final MediaProbeService probe;
  late final EncoderSelector encoders;
  late final MediaProcessor processor;
  late final TaskController tasks;
  late final WaveformService waveforms;

  TaskRequest request({
    TaskKind kind = TaskKind.full,
    ClipRange? range,
    ContainerFormat container = ContainerFormat.mp4,
    QualityPreset quality = QualityPreset.balanced,
    int maxConcurrentEncodes = 2,
    bool overwrite = false,
    OutputResolution outputResolution = OutputResolution.follow,
  }) =>
      TaskRequest(
        kind: kind,
        inputPath: input,
        container: container,
        quality: quality,
        range: range,
        maxConcurrentEncodes: maxConcurrentEncodes,
        overwrite: overwrite,
        outputResolution: outputResolution,
      );

  void flushLogs() => scheduler.advance();

  Future<void> dispose() async {
    await tasks.dispose();
    await processor.dispose();
    await logs.dispose();
    fs.dispose();
  }
}
