import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:path/path.dart' as path;
import '../models/processing_state.dart';
import '../utils/cancellation_token.dart';

/// Hardware encoder types in priority order
enum HardwareEncoder { nvenc, qsv, amf, software }

/// Thread-safe process manager for FFmpeg process lifecycles
class FfmpegProcessManager {
  static final FfmpegProcessManager _instance = FfmpegProcessManager._internal();
  final Set<int> _activePids = {};
  final _lock = Lock();

  factory FfmpegProcessManager() => _instance;
  FfmpegProcessManager._internal();

  Future<void> addProcess(int pid) async {
    await _lock.synchronized(() => _activePids.add(pid));
  }

  Future<void> removeProcess(int pid) async {
    await _lock.synchronized(() => _activePids.remove(pid));
  }

  Future<bool> killProcess(int pid) async {
    return await _lock.synchronized(() {
      try {
        if (_activePids.contains(pid)) {
          Process.killPid(pid);
          _activePids.remove(pid);
          return true;
        }
      } catch (e) {
        if (kDebugMode) print('Error killing process $pid: $e');
      }
      return false;
    });
  }

  Future<void> killAll() async {
    final pids = await _lock.synchronized(() => _activePids.toList());
    for (final pid in pids) {
      await killProcess(pid);
    }
  }
}

// Dart single-isolate mutex using Completer chain to guarantee FIFO ordering
class Lock {
  Future<void> _last = Future.value();

  Future<T> synchronized<T>(T Function() action) {
    final prev = _last;
    final completer = Completer<void>();
    _last = completer.future;
    return prev.then((_) {
      try {
        return action();
      } finally {
        completer.complete();
      }
    });
  }
}

/// Service providing FFmpeg operations with optimized logging and error handling
class FfmpegService {
  static String? _cachedBinDir;
  static HardwareEncoder? _cachedEncoder;
  static int? _cachedThreadCount;

  static final RegExp _progressRegex = RegExp(r'time=(\d+):(\d+):(\d+\.\d+)');

  static void init(String appRoot) {
     _cachedBinDir = path.join(appRoot, 'data', 'ffmpeg-release-full-shared', 'bin');
  }

  static String _getBinDir() {
    if (_cachedBinDir != null) return _cachedBinDir!;
    return path.join(Directory.current.path, 'data', 'ffmpeg-release-full-shared', 'bin');
  }

  /// Retrieves all media metadata in a single ffprobe invocation
  static Future<MediaInfo?> getMediaInfo(String inputPath) async {
    try {
      final ffprobePath = getBundledFfprobePath();
      if (!await _verifyBinary(ffprobePath)) {
        throw Exception('Bundled ffprobe not found or not accessible');
      }

      final args = [
        '-v', 'error',
        '-show_entries', 'stream=codec_type,bit_rate,width,height',
        '-show_entries', 'format=duration',
        '-of', 'json',
        inputPath,
      ];

      final result = await Process.run(ffprobePath, args);
      if (result.exitCode != 0) return null;

      final jsonMap = json.decode(result.stdout as String);
      final streams = jsonMap['streams'] as List<dynamic>? ?? [];
      final format = jsonMap['format'] as Map<String, dynamic>?;

      int videoBitrate = 0, width = 0, height = 0, audioBitrate = 0;

      for (final stream in streams) {
        final codecType = stream['codec_type']?.toString();
        if (codecType == 'video' && width == 0) {
          videoBitrate = int.tryParse(stream['bit_rate']?.toString() ?? '') ?? 0;
          width = stream['width'] ?? 0;
          height = stream['height'] ?? 0;
        } else if (codecType == 'audio' && audioBitrate == 0) {
          audioBitrate = int.tryParse(stream['bit_rate']?.toString() ?? '') ?? 0;
        }
      }

      if (width == 0 && height == 0 && audioBitrate == 0) return null;

      final durationSec = double.tryParse(format?['duration']?.toString() ?? '') ?? 0.0;

      return MediaInfo(
        videoBitrate: videoBitrate,
        width: width,
        height: height,
        audioBitrate: audioBitrate,
        durationSec: durationSec,
      );
    } catch (e) {
      return null;
    }
  }

  static Future<MediaType> detectMediaType(String filePath) async {
    try {
      final ffprobePath = getBundledFfprobePath();
      if (!await _verifyBinary(ffprobePath)) {
        return _fallbackMediaTypeDetection(filePath);
      }

      final args = [
        '-v', 'error',
        '-show_entries', 'stream=codec_type',
        '-of', 'default=noprint_wrappers=1',
        filePath,
      ];

      final result = await Process.run(ffprobePath, args);
      if (result.exitCode != 0) {
        return _fallbackMediaTypeDetection(filePath);
      }

      final output = result.stdout.toString();
      if (output.contains('codec_type=video')) return MediaType.video;
      if (output.contains('codec_type=audio')) return MediaType.audio;

      return MediaType.unknown;
    } catch (e) {
      return _fallbackMediaTypeDetection(filePath);
    }
  }

  static MediaType _fallbackMediaTypeDetection(String filePath) {
    const videoExts = {'mp4', 'avi', 'mkv', 'mov', 'flv', 'wmv', 'webm'};
    const audioExts = {'mp3', 'wav', 'ogg', 'aac', 'm4a', 'flac'};

    final ext = path.extension(filePath).toLowerCase().replaceAll('.', '');
    if (videoExts.contains(ext)) return MediaType.video;
    if (audioExts.contains(ext)) return MediaType.audio;
    return MediaType.unknown;
  }

  /// Executes FFmpeg command and streams logs line-by-line via [onLog].
  static Future<FfmpegResult> executeCommand(
    List<String> args, {
    required void Function(String line) onLog,
    CancellationToken? cancelToken,
  }) async {
    int? pid;
    Process? process;

    try {
      final ffmpegPath = getBundledFfmpegPath();
      if (!await _verifyBinary(ffmpegPath)) {
        throw Exception('Bundled ffmpeg not found');
      }

      // Direct process spawn without shell overhead
      process = await Process.start(ffmpegPath, args);
      pid = process.pid;
      await FfmpegProcessManager().addProcess(pid);

      // Drain streams fully before checking exitCode
      final stdoutDone = process.stdout
          .transform(utf8.decoder)
          .transform(const LineSplitter())
          .listen(onLog)
          .asFuture<void>();

      final stderrDone = process.stderr
          .transform(utf8.decoder)
          .transform(const LineSplitter())
          .listen(onLog)
          .asFuture<void>();

      if (cancelToken?.isCancelled == true) {
        process.kill();
        await FfmpegProcessManager().killProcess(pid);
        return const FfmpegResult(false, 'Cancelled by user');
      }

      final capturedPid = pid;
      cancelToken?.addListener(() {
         process?.kill();
         unawaited(FfmpegProcessManager().killProcess(capturedPid));
      });

      final exitCode = await process.exitCode;

      // Wait for all stream output to be delivered
      await Future.wait([stdoutDone, stderrDone]).catchError((_) {});

      if (cancelToken?.isCancelled == true) {
         return const FfmpegResult(false, 'Task Cancelled');
      }

      return FfmpegResult(exitCode == 0, 'Process exited with code $exitCode');
    } catch (e) {
      return FfmpegResult(false, 'Execution failed: $e');
    } finally {
      if (pid != null) {
        await FfmpegProcessManager().removeProcess(pid);
      }
    }
  }

  /// Parses FFmpeg time= output into seconds, returns null if no match
  static double? _parseProgressTime(String msg) {
    final timeMatch = _progressRegex.firstMatch(msg);
    if (timeMatch == null) return null;
    final h = int.parse(timeMatch.group(1)!);
    final m = int.parse(timeMatch.group(2)!);
    final s = double.parse(timeMatch.group(3)!);
    return h * 3600 + m * 60 + s;
  }

  /// Generates full media (Audio + Video splitting/processing)
  static Future<void> generateFullMedia({
    required String inputPath,
    String? outputVideoPath,
    required String outputAudioPath,
    required Map<String, String> localizedStrings,
    bool apply4K = false,
    VideoFormat videoFormat = VideoFormat.mp4,
    required void Function(String line) onLog,
    void Function(double progress)? onProgress,
    CancellationToken? cancelToken,
  }) async {
    final List<String> summary = [];
    
    final mediaInfo = await getMediaInfo(inputPath);
    final totalDuration = mediaInfo?.durationSec ?? 0.0;

    void log(String msg) {
       onLog(msg);
       if (totalDuration > 0 && onProgress != null) {
         final current = _parseProgressTime(msg);
         if (current != null) {
           onProgress((current / totalDuration).clamp(0.0, 1.0));
         }
       }
    }
    
    bool isCancelled() {
      if (cancelToken?.isCancelled == true) {
        log('🛑 Task Cancelled by user.');
        summary.add('Status: CANCELLED');
        return true;
      }
      return false;
    }

    try {
      if (!await _verifyBinary(getBundledFfmpegPath())) {
        log('❌ Required binaries not found.');
        summary.add('Status: FAILED (Missing Binaries)');
        return;
      }

      if (isCancelled()) return;

      final mediaType = await detectMediaType(inputPath);
      final isHDR = await _hasHdr(inputPath);

      if (mediaType == MediaType.video) {
        if (outputVideoPath == null) throw ArgumentError('No video output path');

        final inputWidth = mediaInfo?.width ?? 1920;
        final inputHeight = mediaInfo?.height ?? 1080;
        
        int targetWidth = inputWidth;
        int targetHeight = inputHeight;
        
        if (!apply4K && inputHeight > 1080) {
           final ratio = inputWidth / inputHeight;
           targetHeight = 1080;
           targetWidth = (1080 * ratio).round();
           if (targetWidth % 2 != 0) targetWidth++;
        }

        log('Video Input: ${inputWidth}x$inputHeight, HDR: $isHDR');
        
        if (isCancelled()) return;

        // 1. Video Command - Choose codec based on output format
        List<String> videoCmd;
        if (videoFormat == VideoFormat.webm) {
          log('🎬 Using VP8 encoder (WebM)');
          videoCmd = _buildVP8Command(
            inputPath, outputVideoPath,
            inputWidth, inputHeight, targetWidth, targetHeight,
            isHDR
          );
        } else {
          final encoder = await _detectBestEncoder();
          log('🎮 Using encoder: ${encoder.name.toUpperCase()} (MP4)');
          videoCmd = _buildVideoCommand(
            inputPath, outputVideoPath, 
            inputWidth, inputHeight, targetWidth, targetHeight, 
            isHDR, encoder
          );
        }
        
        log(localizedStrings['videoSplitCmd']?.replaceAll('%s', '...') ?? 'Executing Video Split...');
        final vResult = await executeCommand(videoCmd, onLog: log, cancelToken: cancelToken);
        
        if (cancelToken?.isCancelled == true) {
           summary.add('Video: CANCELLED');
           return; 
        }

        if (!vResult.success) {
           log('❌ ${localizedStrings['videoSplitFailed']?.replaceAll('%s', vResult.log) ?? 'Video failed: ${vResult.log}'}');
           summary.add('Video: FAILED');
           return;
        }
        log('✅ ${localizedStrings['videoSplitSuccess']?.replaceAll('%s', outputVideoPath) ?? 'Video Ready: $outputVideoPath'}');
        summary.add('Video: SUCCESS ($outputVideoPath)');

        // 2. Audio Command
        if (isCancelled()) return;

        final audioCmd = _buildAudioCommand(inputPath, outputAudioPath, mediaInfo?.audioBitrate ?? 0);
        log(localizedStrings['audioSplitCmd']?.replaceAll('%s', '...') ?? 'Executing Audio Split...');
        final aResult = await executeCommand(audioCmd, onLog: log, cancelToken: cancelToken);
        
        if (cancelToken?.isCancelled == true) {
           summary.add('Audio: CANCELLED');
           return;
        }

        if (aResult.success) {
           log('✅ ${localizedStrings['audioSplitSuccess']?.replaceAll('%s', outputAudioPath) ?? 'Audio Ready: $outputAudioPath'}');
           summary.add('Audio: SUCCESS ($outputAudioPath)');
        } else {
           log('❌ ${localizedStrings['audioSplitFailed']?.replaceAll('%s', aResult.log) ?? 'Audio failed: ${aResult.log}'}');
           summary.add('Audio: FAILED');
        }

      } else {
        // Audio Only logic
        if (isCancelled()) return;
        
        final audioCmd = _buildAudioCommand(inputPath, outputAudioPath, mediaInfo?.audioBitrate ?? 0);
        final result = await executeCommand(audioCmd, onLog: onLog, cancelToken: cancelToken);
        
         if (result.success) {
           summary.add('Audio: SUCCESS ($outputAudioPath)');
        } else {
           summary.add('Audio: FAILED');
        }
      }

    } catch (e) {
      log('❌ Error: $e');
      summary.add('Error: $e');
    } finally {
       final separator = '=' * 30;
       onLog('\n$separator');
       onLog('      PROCESSING SUMMARY');
       onLog(separator);
       if (summary.isEmpty) {
         onLog('No actions completed.');
       } else {
         for (final line in summary) {
           onLog(line);
         }
       }
       onLog('$separator\n');
    }
  }

  // --- Helpers ---

  static int _getOptimalThreadCount() {
    return _cachedThreadCount ??= (Platform.numberOfProcessors * 0.75).ceil().clamp(2, 16);
  }

  /// VP8 encoder for WebM - constrained quality mode (CRF + bitrate cap)
  static List<String> _buildVP8Command(
    String input, String output,
    int inW, int inH, int targetW, int targetH,
    bool isHDR,
  ) {
    final filters = <String>[];
    if (targetW != inW || targetH != inH) filters.add('scale=$targetW:$targetH');
    if (isHDR) filters.add('tonemap=tonemap=hable,format=yuv420p');

    final threadCount = _getOptimalThreadCount();
    
    // VP8 CRF: higher = smaller file (4-63 range)
    // Adjusted to ~2/3 size of high quality settings
    int crf = 33;
    String bitrateCap = '4M';
    if (targetH >= 2160) {
      crf = 23;           // 4K: ~26Mbps
      bitrateCap = '26M';
    } else if (targetH >= 1440) {
      crf = 27;           // 1440p: ~13Mbps
      bitrateCap = '13M';
    } else if (targetH >= 1080) {
      crf = 27;           // 1080p: ~7Mbps
      bitrateCap = '7M';
    } else if (targetH >= 720) {
      crf = 33;           // 720p: ~4Mbps
      bitrateCap = '4M';
    }

    return [
      '-i', input,
      if (filters.isNotEmpty) ...['-vf', filters.join(',')],
      '-c:v', 'libvpx',
      '-crf', '$crf',            // Quality target
      '-b:v', bitrateCap,        // Bitrate cap (not target!)
      '-quality', 'good',
      '-cpu-used', '2',
      '-threads', '$threadCount',
      '-auto-alt-ref', '1',
      '-lag-in-frames', '16',
      '-an', '-y', output
    ];
  }

  static List<String> _buildVideoCommand(
    String input, String output, 
    int inW, int inH, int targetW, int targetH, 
    bool isHDR, HardwareEncoder encoder,
  ) {
    final filters = <String>[];
    if (targetW != inW || targetH != inH) filters.add('scale=$targetW:$targetH');
    if (isHDR) filters.add('tonemap=tonemap=hable,format=yuv420p');

    final threadCount = _getOptimalThreadCount();

    // Hardware encoder config optimized for maximum compression (WebGL H.264)
    switch (encoder) {
      case HardwareEncoder.nvenc:
        // NVENC: Balanced High Quality (approx 2/3 size of max)
        int cq = 29;
        if (targetH >= 2160) {
          cq = 26;   // 4K: ~30-40 Mbps
        } else if (targetH >= 1440) {
          cq = 28;   // 1440p: ~15-20 Mbps
        } else if (targetH >= 1080) {
          cq = 29;   // 1080p: ~8-12 Mbps
        }
        return [
          '-hwaccel', 'd3d11va',
          '-i', input,
          if (filters.isNotEmpty) ...['-vf', filters.join(',')],
          '-c:v', 'h264_nvenc',
          '-preset', 'p7',           // Slowest = best compression
          '-tune', 'hq',             // High quality tuning
          '-rc', 'vbr', '-cq', '$cq',
          '-b:v', '0',
          '-bf', '3',                // 3 B-frames for better compression
          '-b_ref_mode', 'middle',   // Use middle B-frame as reference
          '-spatial-aq', '1',        // Spatial adaptive quantization
          '-temporal-aq', '1',       // Temporal adaptive quantization
          '-rc-lookahead', '32',     // Lookahead for better rate control
          '-profile:v', 'high',
          '-movflags', '+faststart',
          '-an', '-y', output
        ];

      case HardwareEncoder.qsv:
        int qsvQuality = 29;
        if (targetH >= 2160) {
          qsvQuality = 26;
        } else if (targetH >= 1440) {
          qsvQuality = 28;
        } else if (targetH >= 1080) {
          qsvQuality = 29;
        }
        return [
          '-hwaccel', 'd3d11va',
          '-i', input,
          if (filters.isNotEmpty) ...['-vf', filters.join(',')],
          '-c:v', 'h264_qsv',
          '-preset', 'veryslow',     // Best compression
          '-global_quality', '$qsvQuality',
          '-look_ahead', '1',        // Enable lookahead
          '-look_ahead_depth', '40',
          '-profile:v', 'high',
          '-movflags', '+faststart',
          '-an', '-y', output
        ];

      case HardwareEncoder.amf:
        int amfQp = 29;
        if (targetH >= 2160) {
          amfQp = 26;
        } else if (targetH >= 1440) {
          amfQp = 28;
        } else if (targetH >= 1080) {
          amfQp = 29;
        }
        return [
          '-hwaccel', 'd3d11va',
          '-i', input,
          if (filters.isNotEmpty) ...['-vf', filters.join(',')],
          '-c:v', 'h264_amf',
          '-quality', 'quality',     // Quality mode for best compression
          '-rc', 'cqp',
          '-qp_i', '$amfQp', '-qp_p', '$amfQp', '-qp_b', '${amfQp + 2}',
          '-bf', '3',                // B-frames
          '-profile:v', 'high',
          '-movflags', '+faststart',
          '-an', '-y', output
        ];

      case HardwareEncoder.software:
        // libx264: Balanced High Quality
        int crf = 24;
        if (targetH >= 2160) {
          crf = 21;   // 4K
        } else if (targetH >= 1440) {
          crf = 23;   // 1440p
        } else if (targetH >= 1080) {
          crf = 24;   // 1080p
        }
        return [
          '-i', input,
          if (filters.isNotEmpty) ...['-vf', filters.join(',')],
          '-c:v', 'libx264',
          '-crf', '$crf',
          '-preset', 'slower',       // Better compression than medium
          '-tune', 'film',
          '-threads', '$threadCount',
          '-profile:v', 'high',
          '-level', '4.1',           // WebGL/browser compatibility
          '-pix_fmt', 'yuv420p',
          '-movflags', '+faststart',
          '-an', '-y', output
        ];
    }
  }

  static List<String> _buildAudioCommand(String input, String output, int inputBitrate) {
    final targetBitrate = (inputBitrate > 320000) ? 320000 : (inputBitrate > 0 ? inputBitrate : 192000);
    return [
      '-i', input,
      '-map', '0:a:0',
      '-vn',
      '-c:a', 'libvorbis',
      '-b:a', '$targetBitrate',
      '-y', output
    ];
  }

  static Future<String> generatePreview({
      required String inputPath,
      String? outputVideoPath,
      required String outputAudioPath,
      required int startMs,
      required int endMs,
      required void Function(String line) onLog,
      void Function(double progress)? onProgress,
      CancellationToken? cancelToken,
      VideoFormat videoFormat = VideoFormat.mp4,
      }) async {
        
      final startSec = (startMs / 1000).toStringAsFixed(3);
      final durationVal = (endMs - startMs) / 1000.0;
      final durationSec = durationVal.toStringAsFixed(3);
      
       void log(String msg) {
          onLog(msg);
          if (onProgress != null && durationVal > 0) {
            final current = _parseProgressTime(msg);
            if (current != null) {
              onProgress((current / durationVal).clamp(0.0, 1.0));
            }
          }
       }

       log('Generating Preview from ${startSec}s for ${durationSec}s');

       if (outputVideoPath != null) {
         if (cancelToken?.isCancelled == true) return "Cancelled";
         
         final isWebM = videoFormat == VideoFormat.webm;
         final encoder = isWebM ? 'libvpx' : 'libx264'; // VP8 for WebM preview (faster than VP9)
         
         final cmd = [
           '-ss', startSec, '-t', durationSec,
           '-i', inputPath,
           '-c:v', encoder, 
           // Use Ultrafast/low quality for preview speed
           if (isWebM) ...['-qmin', '10', '-qmax', '42', '-cpu-used', '5', '-deadline', 'realtime']
           else ...['-crf', '28', '-preset', 'ultrafast'],
           '-an', '-y', outputVideoPath
         ];
          await executeCommand(cmd, onLog: log, cancelToken: cancelToken);
       }

       if (cancelToken?.isCancelled == true) return "Cancelled";
       final audioCmd = [
           '-ss', startSec, '-t', durationSec,
           '-i', inputPath,
            '-map', '0:a:0', '-vn',
           '-c:a', 'libvorbis', '-aq', '4', 
           '-y', outputAudioPath
       ];
       await executeCommand(audioCmd, onLog: log, cancelToken: cancelToken);
       
       return "Preview Generation Completed";
  }

  static Future<bool> _hasHdr(String inputPath) async {
     final res = await Process.run(getBundledFfprobePath(), [
      '-v', 'error', '-select_streams', 'v:0',
      '-show_entries', 'stream=color_primaries,color_transfer', 
      '-of', 'json', inputPath
    ]);
    if (res.exitCode != 0) return false;
    final body = res.stdout.toString();
    return body.contains('bt2020') || body.contains('smpte2084');
  }

  /// Detects best hardware encoder with caching (hardware doesn't change at runtime)
  static Future<HardwareEncoder> _detectBestEncoder() async {
    if (_cachedEncoder != null) return _cachedEncoder!;

    if (!Platform.isWindows) {
      _cachedEncoder = HardwareEncoder.software;
      return _cachedEncoder!;
    }

    final ffmpegPath = getBundledFfmpegPath();
    
    if (await _testEncoder(ffmpegPath, 'h264_nvenc')) {
      _cachedEncoder = HardwareEncoder.nvenc;
    } else if (await _testEncoder(ffmpegPath, 'h264_qsv')) {
      _cachedEncoder = HardwareEncoder.qsv;
    } else if (await _testEncoder(ffmpegPath, 'h264_amf')) {
      _cachedEncoder = HardwareEncoder.amf;
    } else {
      _cachedEncoder = HardwareEncoder.software;
    }

    return _cachedEncoder!;
  }

  /// Tests if a specific encoder is available and functional
  static Future<bool> _testEncoder(String ffmpegPath, String encoder) async {
    try {
      final result = await Process.run(
        ffmpegPath,
        ['-hide_banner', '-f', 'lavfi', '-i', 'nullsrc=s=256x256:d=1', 
         '-c:v', encoder, '-f', 'null', '-'],
        stdoutEncoding: utf8,
        stderrEncoding: utf8,
      );
      return result.exitCode == 0;
    } catch (_) {
      return false;
    }
  }

  static Future<bool> _verifyBinary(String binaryPath) async {
    return File(binaryPath).exists();
  }

  static String getBundledFfmpegPath() {
    final binDir = _getBinDir();
    return Platform.isWindows ? path.join(binDir, 'ffmpeg.exe') : path.join(binDir, 'ffmpeg');
  }

  static String getBundledFfprobePath() {
    final binDir = _getBinDir();
    return Platform.isWindows ? path.join(binDir, 'ffprobe.exe') : path.join(binDir, 'ffprobe');
  }
}
