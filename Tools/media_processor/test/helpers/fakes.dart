import 'dart:io';

import '../../lib/core/log.dart';
import '../../lib/core/outcome.dart';
import '../../lib/domain/media.dart';
import '../../lib/services/binaries.dart';
import '../../lib/services/process_runner.dart';

/// Deterministic clock/timer seam for log batching tests.
final class ManualScheduler implements FlushScheduler {
  final List<void Function()> pending = [];
  DateTime _now = DateTime.utc(2026, 1, 1);
  int schedules = 0;

  @override
  void scheduleAfter(Duration delay, void Function() task) {
    schedules++;
    pending.add(task);
  }

  @override
  DateTime now() => _now;

  void advance([Duration delta = const Duration(milliseconds: 40)]) {
    _now = _now.add(delta);
    final due = List<void Function()>.of(pending);
    pending.clear();
    for (final task in due) {
      task();
    }
  }
}

final class ScriptedOutcome {
  const ScriptedOutcome({
    this.exitCode = 0,
    this.timedOut = false,
    this.cancelled = false,
    this.error,
    this.stdoutLines = const [],
    this.stderrLines = const [],
    this.writeOutputBytes = 0,
    this.delay = Duration.zero,
  });

  final int? exitCode;
  final bool timedOut;
  final bool cancelled;
  final String? error;
  final List<String> stdoutLines;
  final List<String> stderrLines;
  final int writeOutputBytes;
  final Duration delay;
}

/// Fully scriptable process runner. Writes real `.part` files when requested
/// so atomic-commit and rollback paths are exercised end to end.
final class FakeProcessRunner implements ProcessRunner {
  FakeProcessRunner({
    Map<String, ScriptedOutcome>? byLabel,
    this.responder,
    this.onRequest,
  }) : byLabel = byLabel ?? const {};

  final Map<String, ScriptedOutcome> byLabel;
  final ScriptedOutcome Function(ProcessRequest request, int callIndex)? responder;
  final void Function(ProcessRequest request)? onRequest;
  final List<ProcessRequest> requests = [];

  int get callCount => requests.length;
  int countFor(String label) => requests.where((r) => r.label == label).length;

  @override
  Future<ProcessOutcome> run(ProcessRequest request) async {
    final index = requests.length;
    requests.add(request);
    onRequest?.call(request);
    final script = responder?.call(request, index) ??
        byLabel[request.label] ??
        const ScriptedOutcome(writeOutputBytes: 64);
    if (script.delay > Duration.zero) {
      await Future<void>.delayed(script.delay);
    }
    for (final line in script.stdoutLines) {
      request.onLine?.call(line, OutputChannel.stdout);
    }
    for (final line in script.stderrLines) {
      request.onLine?.call(line, OutputChannel.stderr);
    }
    if (script.writeOutputBytes > 0) {
      final output = File(request.arguments.last);
      await output.parent.create(recursive: true);
      await output.writeAsBytes(
          List<int>.filled(script.writeOutputBytes, 0x41));
    }
    return ProcessOutcome(
      exitCode: script.exitCode,
      elapsed: script.delay,
      timedOut: script.timedOut,
      cancelled: script.cancelled,
      stdoutLines: script.stdoutLines,
      stderrTail: script.stderrLines,
      error: script.error,
    );
  }
}

final class FakeBinaryLocator implements BinaryLocator {
  FakeBinaryLocator([FfmpegBinaries? binaries])
      : _binaries = binaries ??
            const FfmpegBinaries(ffmpeg: 'ffmpeg.exe', ffprobe: 'ffprobe.exe');

  final FfmpegBinaries _binaries;
  int verifyCalls = 0;
  bool failVerification = false;

  @override
  FfmpegBinaries get binaries => _binaries;

  @override
  void invalidate() {}

  @override
  Future<Outcome<FfmpegBinaries>> verify(ProcessRunner runner) async {
    verifyCalls++;
    if (failVerification) {
      return const Err<FfmpegBinaries>(
          Failure(FailureKind.binaryUnavailable, 'injected failure'));
    }
    return Ok(_binaries);
  }
}

final class TestFs {
  TestFs() : dir = Directory.systemTemp.createTempSync('media_processor_test_');

  final Directory dir;

  String file(String name, {int bytes = 128}) {
    final target = File('${dir.path}/$name');
    target.parent.createSync(recursive: true);
    target.writeAsBytesSync(List<int>.filled(bytes, 0x42));
    return target.path;
  }

  String path(String name) => '${dir.path}/$name';

  void dispose() {
    if (dir.existsSync()) dir.deleteSync(recursive: true);
  }
}

MediaInfo sampleMedia({
  String path = 'input.mp4',
  bool video = true,
  bool audio = true,
  int width = 1920,
  int height = 1080,
  bool hdr = false,
  Duration duration = const Duration(seconds: 10),
  String? videoCodec = 'h264',
  String? audioCodec = 'aac',
  String? pixelFormat = 'yuv420p',
  int videoBitrate = 4000000,
  int audioBitrate = 192000,
}) =>
    MediaInfo(
      path: path,
      type: video ? MediaType.video : MediaType.audio,
      duration: duration,
      resolution: Resolution(width, height),
      videoBitrate: videoBitrate,
      audioBitrate: audioBitrate,
      hasVideo: video,
      hasAudio: audio,
      videoCodec: videoCodec,
      audioCodec: audioCodec,
      pixelFormat: pixelFormat,
      hdr: hdr,
      sizeBytes: 1024,
      modifiedMs: 1700000000000,
    );

String sampleProbeJson({
  bool video = true,
  bool audio = true,
  int width = 1920,
  int height = 1080,
  double duration = 10,
  String videoCodec = 'h264',
  String audioCodec = 'aac',
  String pixFmt = 'yuv420p',
  bool hdr = false,
}) {
  final streams = <String>[
    if (video)
      '{"codec_type":"video","codec_name":"$videoCodec","width":$width,'
          '"height":$height,"bit_rate":"4000000","pix_fmt":"$pixFmt",'
          '"color_primaries":"${hdr ? 'bt2020' : 'bt709'}",'
          '"color_transfer":"${hdr ? 'smpte2084' : 'bt709'}"}',
    if (audio)
      '{"codec_type":"audio","codec_name":"$audioCodec",'
          '"bit_rate":"192000"}',
  ];
  return '{"streams":[${streams.join(',')}],'
      '"format":{"duration":"$duration","size":"1024","bit_rate":"4192000"}}';
}
