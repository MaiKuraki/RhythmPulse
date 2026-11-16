import '../domain/encoding.dart';
import 'host.dart';

enum GpuVendor { nvidia, intel, amd, apple, generic, software }
enum RateControl { crf, cq, globalQuality, qp, qv }

/// One encoder row. Hardware decode / scale / upload details travel with the
/// encoder so the command builder never guesses device compatibility.
final class EncoderDescriptor {
  const EncoderDescriptor({
    required this.id,
    required this.codec,
    required this.vendor,
    required this.rateControl,
    required this.supportedOs,
    this.architectures = const {},
    this.hwaccel,
    this.hwaccelOutputFormat,
    this.hardwareScale,
    this.hwUpload,
    this.hwDownload,
    this.extraArgs = const [],
    this.requiresDeviceFrames = false,
  });

  final String id;
  final VideoCodec codec;
  final GpuVendor vendor;
  final RateControl rateControl;
  final Set<HostOs> supportedOs;
  final Set<CpuArchitecture> architectures;
  final String? hwaccel;
  final String? hwaccelOutputFormat;
  final String? hardwareScale;
  final String? hwUpload;
  final String? hwDownload;
  final List<String> extraArgs;
  final bool requiresDeviceFrames;

  bool get isHardware => vendor != GpuVendor.software;

  bool supports(HostCapabilities host) =>
      supportedOs.contains(host.os) &&
      (architectures.isEmpty || architectures.contains(host.architecture));
}

/// Ordered catalog: hardware first, software last as the guaranteed fallback.
final class EncoderCatalog {
  const EncoderCatalog._();

  static const List<EncoderDescriptor> all = [
    EncoderDescriptor(
      id: 'h264_nvenc',
      codec: VideoCodec.h264,
      vendor: GpuVendor.nvidia,
      rateControl: RateControl.cq,
      supportedOs: {HostOs.windows, HostOs.linux},
      architectures: {CpuArchitecture.x64},
      hwaccel: 'cuda',
      hwaccelOutputFormat: 'cuda',
      hardwareScale: 'scale_cuda={w}:{h}',
      hwUpload: 'hwupload_cuda',
      hwDownload: 'hwdownload',
      extraArgs: [
        '-tune', 'hq', '-bf', '3', '-spatial-aq', '1', '-temporal-aq', '1',
        '-rc-lookahead', '32', '-profile:v', 'high',
      ],
    ),
    EncoderDescriptor(
      id: 'h264_qsv',
      codec: VideoCodec.h264,
      vendor: GpuVendor.intel,
      rateControl: RateControl.globalQuality,
      supportedOs: {HostOs.windows, HostOs.linux},
      architectures: {CpuArchitecture.x64},
      hwaccel: 'qsv',
      hwaccelOutputFormat: 'qsv',
      hardwareScale: 'scale_qsv={w}:{h}',
      hwUpload: 'hwupload=derive_device=qsv',
      hwDownload: 'hwdownload',
      extraArgs: ['-look_ahead', '1', '-look_ahead_depth', '40', '-profile:v', 'high'],
    ),
    EncoderDescriptor(
      id: 'h264_amf',
      codec: VideoCodec.h264,
      vendor: GpuVendor.amd,
      rateControl: RateControl.qp,
      supportedOs: {HostOs.windows, HostOs.linux},
      architectures: {CpuArchitecture.x64},
      hwaccel: 'd3d11va',
      extraArgs: ['-bf', '3', '-profile:v', 'high'],
    ),
    EncoderDescriptor(
      id: 'h264_videotoolbox',
      codec: VideoCodec.h264,
      vendor: GpuVendor.apple,
      rateControl: RateControl.qv,
      supportedOs: {HostOs.macos},
      hwaccel: 'videotoolbox',
      extraArgs: ['-profile:v', 'high'],
    ),
    EncoderDescriptor(
      id: 'h264_vaapi',
      codec: VideoCodec.h264,
      vendor: GpuVendor.generic,
      rateControl: RateControl.qp,
      supportedOs: {HostOs.linux},
      hwaccel: 'vaapi',
      hwaccelOutputFormat: 'vaapi',
      hardwareScale: 'scale_vaapi={w}:{h}',
      hwUpload: 'hwupload',
      hwDownload: 'hwdownload',
      extraArgs: ['-profile:v', 'high'],
      requiresDeviceFrames: true,
    ),
    EncoderDescriptor(
      id: 'libx264',
      codec: VideoCodec.h264,
      vendor: GpuVendor.software,
      rateControl: RateControl.crf,
      supportedOs: {HostOs.windows, HostOs.linux, HostOs.macos},
      extraArgs: ['-profile:v', 'high'],
    ),
    EncoderDescriptor(
      id: 'libvpx',
      codec: VideoCodec.vp8,
      vendor: GpuVendor.software,
      rateControl: RateControl.crf,
      supportedOs: {HostOs.windows, HostOs.linux, HostOs.macos},
      extraArgs: ['-auto-alt-ref', '1', '-lag-in-frames', '16'],
    ),
  ];

  static const Map<String, List<String>> presets = {
    'h264_nvenc': ['p1', 'p4', 'p7'],
    'h264_qsv': ['veryfast', 'medium', 'veryslow'],
    'h264_amf': ['speed', 'balanced', 'quality'],
    'libx264': ['veryfast', 'medium', 'slower'],
    'libvpx': ['realtime', 'good', 'best'],
  };

  static List<EncoderDescriptor> candidates(
    VideoCodec codec,
    HostCapabilities host,
  ) {
    final matches = all
        .where((e) => e.codec == codec && e.supports(host))
        .toList(growable: false);
    matches.sort((a, b) => a.vendor.index.compareTo(b.vendor.index));
    return matches;
  }

  static String presetFor(EncoderDescriptor encoder, int speedTier) =>
      presets[encoder.id]?[speedTier.clamp(0, 2)] ?? 'balanced';
}
