import 'dart:convert';
import 'dart:io';

import '../core/outcome.dart';
import '../core/ring_buffer.dart';
import '../domain/media.dart';
import 'binaries.dart';
import 'process_runner.dart';

/// One ffprobe invocation per unique (path, size, mtime) fingerprint.
/// Results are cached in an O(1) LRU so repeated operations on the same file
/// do not spawn redundant processes.
final class MediaProbeService {
  MediaProbeService({
    required ProcessRunner runner,
    required BinaryLocator binaries,
    int cacheCapacity = 64,
    Duration timeout = const Duration(seconds: 30),
  })  : _runner = runner,
        _binaries = binaries,
        _cache = LruCache<String, MediaInfo>(cacheCapacity),
        _timeout = timeout;

  final ProcessRunner _runner;
  final BinaryLocator _binaries;
  final LruCache<String, MediaInfo> _cache;
  final Duration _timeout;

  CacheStats get cacheStats => _cache.stats;
  int get cachedEntries => _cache.length;

  void invalidateAll() => _cache.clear();
  void invalidate(String path) {
    for (final key in List<String>.of(_cache.keys)) {
      if (key.startsWith('$path|')) _cache.remove(key);
    }
  }

  Future<Outcome<MediaInfo>> inspect(String path) async {
    FileStat stat;
    try {
      stat = await File(path).stat();
    } catch (e) {
      return Err(Failure(FailureKind.invalidInput, 'Cannot stat input: $path',
          cause: e));
    }
    if (stat.type == FileSystemEntityType.notFound) {
      return Err(Failure(FailureKind.invalidInput, 'Input does not exist: $path'));
    }
    if (stat.type != FileSystemEntityType.file) {
      return Err(Failure(FailureKind.invalidInput, 'Input is not a file: $path'));
    }

    final fingerprint = '$path|${stat.size}|${stat.modified.millisecondsSinceEpoch}';
    final cached = _cache.get(fingerprint);
    if (cached != null) return Ok(cached);

    final outcome = await _runner.run(ProcessRequest(
      label: 'ffprobe',
      executable: _binaries.binaries.ffprobe,
      arguments: [
        '-v', 'error',
        '-show_entries',
        'stream=index,codec_type,codec_name,width,height,bit_rate,pix_fmt,'
            'color_primaries,color_transfer'
            ':stream_disposition=attached_pic:format=duration,size,bit_rate',
        '-of', 'json',
        path,
      ],
      timeout: _timeout,
      captureStdout: true,
      maxCapturedLines: 8192,
    ));
    if (!outcome.succeeded) {
      return Err(Failure(FailureKind.probeFailed,
          'ffprobe failed for $path (${outcome.describe()})'));
    }

    final parsed = parse(
      outcome.stdoutLines.join('\n'),
      path: path,
      sizeBytes: stat.size,
      modifiedMs: stat.modified.millisecondsSinceEpoch,
    );
    final info = parsed.valueOrNull;
    if (info != null) _cache.put(fingerprint, info);
    return parsed;
  }

  /// Pure parser: arbitrary/garbage input must yield [Err], never throw.
  static Outcome<MediaInfo> parse(
    String text, {
    required String path,
    required int sizeBytes,
    required int modifiedMs,
  }) {
    try {
      final decoded = json.decode(text);
      if (decoded is! Map) {
        return const Err(Failure(FailureKind.probeFailed, 'Probe root is not an object'));
      }
      final rawStreams = decoded['streams'];
      if (rawStreams is! List) {
        return const Err(Failure(FailureKind.probeFailed, 'No stream list in probe output'));
      }
      final format = decoded['format'];
      final formatMap = format is Map ? format : const <String, dynamic>{};

      var hasVideo = false;
      var hasAudio = false;
      String? videoCodec;
      String? audioCodec;
      String? pixelFormat;
      var width = 0;
      var height = 0;
      var videoBitrate = 0;
      var audioBitrate = 0;
      var hdr = false;

      for (final raw in rawStreams) {
        if (raw is! Map) continue;
        final codecType = raw['codec_type']?.toString();
        // ID3/APIC album art is reported by ffprobe as an MJPEG/PNG "video"
        // stream. Treating it as video makes an MP3 look like a clip with a
        // 500x500 resolution and enables all the wrong controls.
        if (codecType == 'video' && _isAttachedPicture(raw)) continue;
        if (codecType == 'video' && !hasVideo) {
          hasVideo = true;
          videoCodec = raw['codec_name']?.toString();
          pixelFormat = raw['pix_fmt']?.toString();
          width = _nonNegativeInt(raw['width']);
          height = _nonNegativeInt(raw['height']);
          videoBitrate = _nonNegativeInt(raw['bit_rate']);
          final primaries = raw['color_primaries']?.toString() ?? '';
          final transfer = raw['color_transfer']?.toString() ?? '';
          hdr = primaries.contains('bt2020') || transfer.contains('smpte2084');
        } else if (codecType == 'audio' && !hasAudio) {
          hasAudio = true;
          audioCodec = raw['codec_name']?.toString();
          audioBitrate = _nonNegativeInt(raw['bit_rate']);
        }
      }

      if (!hasVideo && !hasAudio) {
        return const Err(Failure(FailureKind.unsupported,
            'Input contains neither a video nor an audio stream'));
      }

      final durationUs = _nonNegativeInt(formatMap['duration_us']);
      final durationSec = durationUs > 0
          ? durationUs / 1000000.0
          : _nonNegativeDouble(formatMap['duration']);
      if (durationSec.isNaN || durationSec.isInfinite) {
        return const Err(Failure(FailureKind.probeFailed, 'Invalid duration'));
      }

      return Ok(MediaInfo(
        path: path,
        type: hasVideo ? MediaType.video : MediaType.audio,
        duration: Duration(microseconds: (durationSec * 1000000).round()),
        resolution: Resolution(width, height),
        videoBitrate: videoBitrate,
        audioBitrate: audioBitrate,
        hasVideo: hasVideo,
        hasAudio: hasAudio,
        videoCodec: videoCodec,
        audioCodec: audioCodec,
        pixelFormat: pixelFormat,
        hdr: hdr,
        sizeBytes: sizeBytes,
        modifiedMs: modifiedMs,
      ));
    } catch (e) {
      return Err(Failure(FailureKind.probeFailed, 'Malformed ffprobe JSON', cause: e));
    }
  }

  /// `attached_pic` is the only reliable discriminator: cover-art streams also
  /// carry width/height and a duration, so codec or duration heuristics would
  /// wrongly discard legitimate MJPEG/PNG video files.
  static bool _isAttachedPicture(Map raw) {
    final disposition = raw['disposition'];
    if (disposition is! Map) return false;
    final flag = disposition['attached_pic'];
    return flag == 1 || flag == true;
  }

  static int _nonNegativeInt(Object? value) {
    final parsed = value is int ? value : int.tryParse(value?.toString() ?? '');
    return (parsed ?? 0).clamp(0, 1 << 40).toInt();
  }

  static double _nonNegativeDouble(Object? value) {
    final parsed = value is num
        ? value.toDouble()
        : double.tryParse(value?.toString() ?? '');
    return (parsed ?? 0).clamp(0, 1 << 40).toDouble();
  }
}
