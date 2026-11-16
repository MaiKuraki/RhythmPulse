import '../domain/encoding.dart';
import '../domain/media.dart';
import '../domain/task.dart';
import '../platform/encoders.dart';
import '../platform/host.dart';

/// Resolved hardware/filter pipeline for one video encode.
final class VideoPipeline {
  const VideoPipeline({required this.inputArgs, this.filter});

  final List<String> inputArgs;
  final String? filter;
}

final class CommandSpec {
  const CommandSpec({
    required this.input,
    required this.output,
    required this.media,
    required this.profile,
    required this.quality,
    required this.targetResolution,
    required this.kind,
    required this.threads,
    this.range,
    this.encoder,
    this.streamCopy = false,
    this.fastStart = true,
  });

  final String input;
  final String output;
  final MediaInfo media;
  final EncodingProfile profile;
  final QualityPreset quality;
  final Resolution targetResolution;
  final OutputKind kind;
  final ThreadPlan threads;
  final ClipRange? range;
  final EncoderDescriptor? encoder;
  final bool streamCopy;
  final bool fastStart;

  bool get needsScale =>
      media.resolution.width != targetResolution.width ||
      media.resolution.height != targetResolution.height;

  bool get needsCpuFilter => media.hdr;
}

/// Pure, side-effect-free ffmpeg argument construction. Every generated
/// command is deterministic and can be exhaustively verified in tests.
final class FfmpegCommandBuilder {
  const FfmpegCommandBuilder();

  static const List<String> commonPrefix = [
    '-hide_banner',
    '-nostdin',
    '-loglevel', 'warning',
    '-nostats',
    '-progress', 'pipe:1',
    '-stats_period', '250',
    '-y', // exclusively used on our own .part files
  ];

  static const List<String> encoderListArgs = [
    '-hide_banner', '-nostdin', '-encoders',
  ];

  List<String> build(CommandSpec spec) => switch (spec.kind) {
        OutputKind.video => _video(spec),
        OutputKind.audio => _audio(spec),
      };

  /// Decides whether hardware decode can stay on the GPU or must be avoided.
  ///
  /// HDR tone mapping is a CPU-only filter, so decoding straight into system
  /// memory is cheaper than a needless GPU->CPU->GPU round trip. When a
  /// hardware scaler exists and no CPU filter is needed, frames stay on the
  /// device through `-hwaccel_output_format` + the vendor scaler.
  static VideoPipeline planVideo(CommandSpec spec) {
    if (spec.kind != OutputKind.video || spec.streamCopy) {
      return const VideoPipeline(inputArgs: []);
    }
    final encoder = spec.encoder;
    if (encoder == null) return const VideoPipeline(inputArgs: []);

    final scale = spec.needsScale;
    final cpuFilter = spec.needsCpuFilter;
    final hardwareScaler = encoder.hardwareScale;
    final canScaleOnGpu = scale && !cpuFilter && hardwareScaler != null;
    final needsSystemFrames = cpuFilter || (scale && !canScaleOnGpu);
    final useHardwareDecode =
        encoder.isHardware && encoder.hwaccel != null && !needsSystemFrames;

    final filters = <String>[];
    if (scale) {
      if (useHardwareDecode && hardwareScaler != null) {
        filters.add(hardwareScaler
            .replaceAll('{w}', '${spec.targetResolution.width}')
            .replaceAll('{h}', '${spec.targetResolution.height}'));
      } else {
        filters.add(
            'scale=${spec.targetResolution.width}:${spec.targetResolution.height}');
      }
    }
    if (cpuFilter) {
      // Verified chain: tonemap requires linear floating-point samples.
      filters.add('format=gbrpf32le');
      filters.add('tonemap=tonemap=hable');
    }
    if (needsSystemFrames) {
      filters.add('format=${spec.profile.pixelFormat}');
      final upload = encoder.hwUpload;
      if (encoder.isHardware && upload != null) filters.add(upload);
    }

    final inputArgs = <String>[];
    if (useHardwareDecode) {
      inputArgs.addAll(['-hwaccel', encoder.hwaccel!]);
      final outputFormat = encoder.hwaccelOutputFormat;
      if (outputFormat != null) {
        inputArgs.addAll(['-hwaccel_output_format', outputFormat]);
      }
    }
    return VideoPipeline(
      inputArgs: List<String>.unmodifiable(inputArgs),
      filter: filters.isEmpty ? null : filters.join(','),
    );
  }

  static List<String> encoderArgs(CommandSpec spec) {
    final encoder = spec.encoder!;
    final settings =
        spec.profile.settingsFor(spec.targetResolution.height, spec.quality);
    final q = settings.constantQuality;
    final capKbps = settings.maxBitrateKbps;
    final preset = EncoderCatalog.presetFor(encoder, settings.speedTier);
    final args = <String>[];

    switch (encoder.id) {
      case 'libvpx':
        // libvpx accepts 0..5 outside realtime; wider range only for realtime.
        final cpuUsed = preset == 'realtime'
            ? settings.cpuUsed.clamp(0, 16).toInt()
            : settings.cpuUsed.clamp(0, 5).toInt();
        args.addAll(['-deadline', preset, '-cpu-used', '$cpuUsed']);
      case 'h264_amf':
        args.addAll(['-quality', preset]);
      case 'h264_videotoolbox' || 'h264_vaapi':
        break;
      default:
        args.addAll(['-preset', preset]);
    }

    switch (encoder.rateControl) {
      case RateControl.crf:
        // VP8 realtime deadline does not accept CRF; use the bitrate cap.
        if (encoder.id == 'libvpx' && preset == 'realtime') {
          args.addAll(['-b:v', '${capKbps}k']);
        } else {
          args.addAll(['-crf', '$q']);
          if (encoder.id == 'libvpx') {
            args.addAll(['-b:v', '${capKbps}k']);
          }
        }
      case RateControl.cq:
        args.addAll(['-rc', 'vbr', '-cq', '$q', '-b:v', '0']);
      case RateControl.globalQuality:
        args.addAll(['-global_quality', '$q']);
      case RateControl.qp:
        if (encoder.id == 'h264_vaapi') {
          args.addAll(['-qp', '$q']);
        } else {
          args.addAll(
              ['-rc', 'cqp', '-qp_i', '$q', '-qp_p', '$q', '-qp_b', '${q + 2}']);
        }
      case RateControl.qv:
        args.addAll(['-b:v', '${capKbps}k']);
    }

    if (encoder.id == 'libvpx' && preset == 'realtime') {
      // auto-alt-ref / lookahead are incompatible with the realtime deadline.
    } else {
      args.addAll(encoder.extraArgs);
    }
    return args;
  }

  List<String> _video(CommandSpec spec) {
    final args = <String>[...commonPrefix];
    final pipeline = planVideo(spec);
    args.addAll(_seekArgs(spec));
    args.addAll(pipeline.inputArgs);
    args.addAll(_decodeThreads(spec));
    args.addAll(['-i', spec.input, '-map', '0:v:0']);

    if (spec.streamCopy) {
      args.addAll(['-c:v', 'copy']);
    } else {
      final filter = pipeline.filter;
      if (filter != null) args.addAll(['-vf', filter]);
      args.addAll(['-c:v', spec.encoder!.id]);
      args.addAll(encoderArgs(spec));
      // Hardware zero-copy frames must not be forced back to system memory.
      if (pipeline.inputArgs.isEmpty) {
        args.addAll(['-pix_fmt', spec.profile.pixelFormat]);
      }
      if (spec.media.hdr) {
        args.addAll([
          '-color_primaries', 'bt709',
          '-color_trc', 'bt709',
          '-colorspace', 'bt709',
        ]);
      }
      if (spec.threads.filterThreads > 0) {
        args.addAll(['-filter_threads', '${spec.threads.filterThreads}']);
      }
      if (spec.threads.encodeThreads > 0) {
        args.addAll(['-threads', '${spec.threads.encodeThreads}']);
      }
    }
    if (spec.fastStart && spec.profile.movflags.isNotEmpty) {
      args.addAll(['-movflags', spec.profile.movflags]);
    }
    args.addAll(['-f', spec.profile.muxer, '-an', spec.output]);
    return List<String>.unmodifiable(args);
  }

  List<String> _audio(CommandSpec spec) {
    final args = <String>[...commonPrefix];
    args.addAll(_seekArgs(spec));
    args.addAll(_decodeThreads(spec));
    args.addAll(['-i', spec.input, '-map', '0:a:0', '-vn']);

    if (spec.streamCopy) {
      args.addAll(['-c:a', 'copy']);
    } else {
      final codec =
          spec.profile.audio == AudioCodec.vorbis ? 'libvorbis' : 'libopus';
      final sourceBitrate = spec.media.audioBitrate;
      final bitrate = sourceBitrate > 320000
          ? 320000
          : (sourceBitrate > 0 ? sourceBitrate : 192000);
      args.addAll(['-c:a', codec, '-b:a', '$bitrate']);
    }
    args.addAll(['-f', spec.profile.audioMuxer, spec.output]);
    return List<String>.unmodifiable(args);
  }

  List<String> _seekArgs(CommandSpec spec) {
    final range = spec.range;
    if (range == null) return const [];
    return [
      '-ss', range.startSeconds.toStringAsFixed(3),
      '-t', range.durationSeconds.toStringAsFixed(3),
    ];
  }

  List<String> _decodeThreads(CommandSpec spec) =>
      spec.threads.decodeThreads > 0
          ? ['-threads', '${spec.threads.decodeThreads}']
          : const [];

  static List<String> waveform({
    required String input,
    required String output,
    required int width,
    required int height,
  }) =>
      [
        '-hide_banner', '-nostdin', '-loglevel', 'warning', '-nostats',
        '-i', input,
        '-filter_complex',
        'aformat=channel_layouts=mono,'
            'showwavespic=s=${width}x$height:colors=0xFF7C4DFF:scale=sqrt:draw=full',
        '-frames:v', '1', '-c:v', 'png', '-f', 'image2', '-y', output,
      ];
}

/// Structured `-progress pipe:1` parsing. No human-readable stats scraping.
final class FfmpegProgress {
  const FfmpegProgress._();

  static double? parse(String line, double totalSeconds) {
    if (totalSeconds <= 0 || totalSeconds.isNaN || totalSeconds.isInfinite) {
      return null;
    }
    if (line == 'progress=end') return 1.0;
    final eq = line.indexOf('=');
    if (eq <= 0) return null;
    final key = line.substring(0, eq);
    final value = line.substring(eq + 1);
    double? seconds;
    switch (key) {
      case 'out_time_us':
        final us = double.tryParse(value);
        if (us != null) seconds = us / 1000000.0;
      case 'out_time_ms':
        final ms = double.tryParse(value);
        if (ms != null) {
          // Some ffmpeg builds emit out_time_ms with microsecond values.
          seconds = ms > totalSeconds * 8000 ? ms / 1000000.0 : ms / 1000.0;
        }
      case 'out_time':
        seconds = _parseClock(value);
      default:
        return null;
    }
    if (seconds == null || seconds.isNaN || seconds.isInfinite) return null;
    return (seconds / totalSeconds).clamp(0.0, 1.0).toDouble();
  }

  static double? _parseClock(String value) {
    final parts = value.split(':');
    if (parts.length != 3) return null;
    final h = double.tryParse(parts[0]);
    final m = double.tryParse(parts[1]);
    final s = double.tryParse(parts[2]);
    if (h == null || m == null || s == null) return null;
    return h * 3600 + m * 60 + s;
  }
}
