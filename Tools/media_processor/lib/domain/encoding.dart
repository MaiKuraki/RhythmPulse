/// Output containers. Adding a container requires one row in
/// [ProfileRegistry] and one row in the encoder catalog; no UI or service
/// branch needs to change.
enum ContainerFormat { mp4, webm, mkv }

enum VideoCodec { h264, vp8 }
enum AudioCodec { vorbis, opus }
enum QualityPreset { speed, balanced, quality }

/// Table-driven quality settings. Resolution tiers and presets are data,
/// not if-else chains spread across command builders.
final class QualitySettings {
  const QualitySettings({
    required this.constantQuality,
    required this.speedTier,
    required this.maxBitrateKbps,
    required this.cpuUsed,
  });

  /// CRF (software), CQ (NVENC), global_quality (QSV/AMF), q:v (VideoToolbox).
  final int constantQuality;

  /// 0 = speed, 1 = balanced, 2 = quality; mapped to concrete encoder presets.
  final int speedTier;
  final int maxBitrateKbps;
  final int cpuUsed;
}

QualitySettings _q(int cq, int kbps, int cpu, int tier) => QualitySettings(
      constantQuality: cq,
      maxBitrateKbps: kbps,
      cpuUsed: cpu,
      speedTier: tier,
    );

Map<QualityPreset, List<QualitySettings>> _tiers({
  required List<int> speed,
  required List<int> balanced,
  required List<int> quality,
  required List<int> bitrateKbps,
  required List<int> speedCpu,
  required List<int> balancedCpu,
  required List<int> qualityCpu,
}) {
  List<QualitySettings> build(List<int> values, List<int> cpu, int tier) => [
        for (var i = 0; i < 4; i++) _q(values[i], bitrateKbps[i], cpu[i], tier),
      ];
  return {
    QualityPreset.speed: build(speed, speedCpu, 0),
    QualityPreset.balanced: build(balanced, balancedCpu, 1),
    QualityPreset.quality: build(quality, qualityCpu, 2),
  };
}

final class EncodingProfile {
  const EncodingProfile({
    required this.container,
    required this.extension,
    required this.video,
    required this.audio,
    required this.muxer,
    required this.audioMuxer,
    required this.audioExtension,
    required this.movflags,
    required this.pixelFormat,
    required this.acceptedPixelFormats,
    required this.streamCopyVideoCodecs,
    required this.streamCopyAudioCodecs,
    required this.quality,
  });

  final ContainerFormat container;
  final String extension;
  final VideoCodec video;
  final AudioCodec audio;
  final String muxer;
  final String audioMuxer;
  final String audioExtension;
  final String movflags;
  final String pixelFormat;
  final Set<String> acceptedPixelFormats;
  final Set<String> streamCopyVideoCodecs;
  final Set<String> streamCopyAudioCodecs;
  final Map<QualityPreset, List<QualitySettings>> quality;

  bool get supportsVideoCopy => streamCopyVideoCodecs.isNotEmpty;
  bool get needsFastStart => movflags.isNotEmpty;

  /// O(1) tier lookup over four resolution thresholds.
  QualitySettings settingsFor(int height, QualityPreset preset) {
    final tier = height >= 2160
        ? 0
        : height >= 1440
            ? 1
            : height >= 1080
                ? 2
                : 3;
    return quality[preset]![tier];
  }
}

/// The single registry that defines output formats.
final class ProfileRegistry {
  const ProfileRegistry._();

  static final Map<ContainerFormat, EncodingProfile> profiles = {
    ContainerFormat.mp4: EncodingProfile(
      container: ContainerFormat.mp4,
      extension: 'mp4',
      video: VideoCodec.h264,
      audio: AudioCodec.vorbis,
      muxer: 'mp4',
      audioMuxer: 'ogg',
      audioExtension: 'ogg',
      movflags: '+faststart',
      pixelFormat: 'yuv420p',
      acceptedPixelFormats: {'yuv420p', 'nv12'},
      streamCopyVideoCodecs: {'h264'},
      streamCopyAudioCodecs: {'vorbis'},
      quality: _mp4Quality,
    ),
    ContainerFormat.webm: EncodingProfile(
      container: ContainerFormat.webm,
      extension: 'webm',
      video: VideoCodec.vp8,
      audio: AudioCodec.vorbis,
      muxer: 'webm',
      audioMuxer: 'ogg',
      audioExtension: 'ogg',
      movflags: '',
      pixelFormat: 'yuv420p',
      acceptedPixelFormats: {'yuv420p'},
      streamCopyVideoCodecs: {'vp8'},
      streamCopyAudioCodecs: {'vorbis'},
      quality: _vp8Quality,
    ),
    ContainerFormat.mkv: EncodingProfile(
      container: ContainerFormat.mkv,
      extension: 'mkv',
      video: VideoCodec.h264,
      audio: AudioCodec.opus,
      muxer: 'matroska',
      audioMuxer: 'ogg',
      audioExtension: 'ogg',
      movflags: '',
      pixelFormat: 'yuv420p',
      acceptedPixelFormats: {'yuv420p', 'nv12', 'yuv420p10le'},
      streamCopyVideoCodecs: {'h264', 'vp8'},
      streamCopyAudioCodecs: {'vorbis', 'opus'},
      quality: _mp4Quality,
    ),
  };

  static EncodingProfile of(ContainerFormat format) => profiles[format]!;
  static Iterable<ContainerFormat> get supported => profiles.keys;
}

const _bitrateKbps = [32000, 16000, 9000, 5000];

final _mp4Quality = _tiers(
  speed: [29, 28, 27, 26],
  balanced: [26, 27, 28, 30],
  quality: [23, 25, 26, 27],
  bitrateKbps: _bitrateKbps,
  speedCpu: [8, 8, 8, 8],
  balancedCpu: [4, 4, 4, 4],
  qualityCpu: [1, 1, 1, 1],
);

final _vp8Quality = _tiers(
  speed: [33, 32, 31, 30],
  balanced: [26, 27, 28, 30],
  quality: [22, 24, 26, 27],
  bitrateKbps: _bitrateKbps,
  speedCpu: [8, 8, 8, 8],
  balancedCpu: [4, 4, 4, 4],
  qualityCpu: [1, 1, 1, 1],
);
