import 'encoding.dart';

enum MediaType { video, audio }

final class Resolution {
  const Resolution(this.width, this.height);

  final int width;
  final int height;

  bool get isEmpty => width <= 0 || height <= 0;
  int get pixels => width * height;

  /// Human-facing tier used in the source information panel and warnings.
  String get tier => height >= 2160
      ? '4K'
      : height >= 1440
          ? '2K'
          : height >= 1080
              ? '1080p'
              : height >= 720
                  ? '720p'
                  : '<720p';

  String get display => isEmpty ? '-' : '$width×$height';

  @override
  String toString() => '${width}x$height';
}

/// A half-open millisecond range [startMs, endMs).
final class ClipRange {
  const ClipRange({required this.startMs, required this.endMs});

  final int startMs;
  final int endMs;

  int get durationMs => endMs - startMs;
  bool get isValid => startMs >= 0 && endMs > startMs;

  double get startSeconds => startMs / 1000.0;
  double get durationSeconds => durationMs / 1000.0;

  ClipRange clampedTo(int durationMs) => ClipRange(
        startMs: startMs.clamp(0, durationMs).toInt(),
        endMs: endMs.clamp(0, durationMs).toInt(),
      );

  @override
  String toString() => 'ClipRange($startMs, $endMs)';
}

/// Complete, single-probe description of an input file.
final class MediaInfo {
  const MediaInfo({
    required this.path,
    required this.type,
    required this.duration,
    required this.resolution,
    required this.videoBitrate,
    required this.audioBitrate,
    required this.hasVideo,
    required this.hasAudio,
    required this.videoCodec,
    required this.audioCodec,
    required this.pixelFormat,
    required this.hdr,
    required this.sizeBytes,
    required this.modifiedMs,
  });

  final String path;
  final MediaType type;
  final Duration duration;
  final Resolution resolution;
  final int videoBitrate;
  final int audioBitrate;
  final bool hasVideo;
  final bool hasAudio;
  final String? videoCodec;
  final String? audioCodec;
  final String? pixelFormat;
  final bool hdr;
  final int sizeBytes;
  final int modifiedMs;

  /// Content fingerprint. A new value means the file changed and every
  /// derived cache entry (probe, waveform) is invalid.
  String get fingerprint => '$path|$sizeBytes|$modifiedMs';

  double get durationSeconds => duration.inMicroseconds / 1000000.0;

  /// Stream copy is only valid when the codec, pixel format and transform
  /// requirements already match the requested output.
  bool canCopyVideoFor(EncodingProfile profile, {required bool needsTransform}) {
    if (needsTransform || !hasVideo || !profile.supportsVideoCopy) return false;
    final codec = videoCodec;
    if (codec == null) return false;
    return profile.streamCopyVideoCodecs.contains(codec) &&
        (pixelFormat == null || profile.acceptedPixelFormats.contains(pixelFormat));
  }

  bool canCopyAudioFor(EncodingProfile profile) {
    if (!hasAudio) return false;
    final codec = audioCodec;
    return codec != null && profile.streamCopyAudioCodecs.contains(codec);
  }
}
