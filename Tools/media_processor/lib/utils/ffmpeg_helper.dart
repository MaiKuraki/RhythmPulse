import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:path/path.dart' as path;

/// Enhanced media type detection with proper MIME type checking
enum MediaType { video, audio, unknown }

/// Represents the result of an FFmpeg operation
class FfmpegResult {
  final bool success; // Indicates whether the operation succeeded
  final String log; // Contains execution logs or error messages

  FfmpegResult(this.success, this.log);
}

/// Contains metadata information about a media file
class MediaInfo {
  final int videoBitrate; // Video bitrate in bits per second (bps)
  final int width; // Video width in pixels
  final int height; // Video height in pixels
  final int audioBitrate; // Audio bitrate in bits per second (bps)

  MediaInfo({
    required this.videoBitrate,
    required this.width,
    required this.height,
    required this.audioBitrate,
  });
}

/// Provides utility methods for FFmpeg operations
class FFmpegHelper {
  /// Retrieves media metadata using the bundled ffprobe
  static Future<MediaInfo?> _getMediaInfo(String inputPath) async {
    try {
      final hasFfprobe = await verifyBundledFfprobe();
      if (!hasFfprobe) {
        throw Exception('Bundled ffprobe not found or not accessible');
      }

      final ffprobePath = getBundledFfprobePath();
      if (kDebugMode) {
        print('Using bundled ffprobe at: $ffprobePath');
      }

      // First detect if the file has video streams
      final hasVideo = await _hasVideoStream(inputPath);
      final hasAudio = await _hasAudioStream(inputPath);

      if (!hasVideo && !hasAudio) {
        if (kDebugMode) {
          print('No video or audio streams detected in the input file');
        }
        return null;
      }

      // Initialize default values
      int videoBitrate = 0;
      int width = 0;
      int height = 0;
      int audioBitrate = 0;

      // Only fetch video info if video streams exist
      if (hasVideo) {
        final videoArgs = [
          '-v',
          'error',
          '-select_streams',
          'v:0',
          '-show_entries',
          'stream=bit_rate,width,height',
          '-of',
          'json',
          inputPath,
        ];

        final videoProcess = await Process.run(ffprobePath, videoArgs);
        if (videoProcess.exitCode == 0) {
          final videoJson = json.decode(videoProcess.stdout as String);
          final videoStreams = videoJson['streams'] as List<dynamic>?;
          if (videoStreams != null && videoStreams.isNotEmpty) {
            final videoStream = videoStreams.first;
            videoBitrate =
                int.tryParse(videoStream['bit_rate']?.toString() ?? '') ?? 0;
            width = videoStream['width'] ?? 0;
            height = videoStream['height'] ?? 0;
          }
        } else if (kDebugMode) {
          print('ffprobe video stream analysis failed: ${videoProcess.stderr}');
        }
      }

      // Only fetch audio info if audio streams exist
      if (hasAudio) {
        final audioArgs = [
          '-v',
          'error',
          '-select_streams',
          'a:0',
          '-show_entries',
          'stream=bit_rate',
          '-of',
          'json',
          inputPath,
        ];

        final audioProcess = await Process.run(ffprobePath, audioArgs);
        if (audioProcess.exitCode == 0) {
          final audioJson = json.decode(audioProcess.stdout as String);
          final audioStreams = audioJson['streams'] as List<dynamic>?;
          if (audioStreams != null && audioStreams.isNotEmpty) {
            final audioStream = audioStreams.first;
            audioBitrate =
                int.tryParse(audioStream['bit_rate']?.toString() ?? '') ?? 0;
          }
        } else if (kDebugMode) {
          print('ffprobe audio stream analysis failed: ${audioProcess.stderr}');
        }
      }

      return MediaInfo(
        videoBitrate: videoBitrate,
        width: width,
        height: height,
        audioBitrate: audioBitrate,
      );
    } catch (e) {
      if (kDebugMode) {
        print('Exception occurred while retrieving media info: $e');
      }
      return null;
    }
  }

  /// Helper method to check if file has video streams
  static Future<bool> _hasVideoStream(String inputPath) async {
    try {
      final ffprobePath = getBundledFfprobePath();
      final args = [
        '-v',
        'error',
        '-select_streams',
        'v',
        '-show_entries',
        'stream=codec_type',
        '-of',
        'csv=p=0',
        inputPath,
      ];

      final result = await Process.run(ffprobePath, args);
      return result.exitCode == 0 && result.stdout.toString().trim().isNotEmpty;
    } catch (e) {
      if (kDebugMode) {
        print('Error checking for video streams: $e');
      }
      return false;
    }
  }

  /// Helper method to check if file has audio streams
  static Future<bool> _hasAudioStream(String inputPath) async {
    try {
      final ffprobePath = getBundledFfprobePath();
      final args = [
        '-v',
        'error',
        '-select_streams',
        'a',
        '-show_entries',
        'stream=codec_type',
        '-of',
        'csv=p=0',
        inputPath,
      ];

      final result = await Process.run(ffprobePath, args);
      return result.exitCode == 0 && result.stdout.toString().trim().isNotEmpty;
    } catch (e) {
      if (kDebugMode) {
        print('Error checking for audio streams: $e');
      }
      return false;
    }
  }

  /// Gets the absolute path to the bundled ffprobe executable
  static String getBundledFfprobePath() {
    final binDir = path.join(
      Directory.current.path,
      'data',
      'ffmpeg-release-full-shared',
      'bin',
    );

    if (Platform.isWindows) {
      return path.join(binDir, 'ffprobe.exe');
    } else if (Platform.isLinux || Platform.isMacOS) {
      return path.join(binDir, 'ffprobe');
    }
    throw UnsupportedError('Unsupported platform');
  }

  /// Gets the absolute path to the bundled ffmpeg executable
  static String getBundledFfmpegPath() {
    final binDir = path.join(
      Directory.current.path,
      'data',
      'ffmpeg-release-full-shared',
      'bin',
    );

    if (Platform.isWindows) {
      return path.join(binDir, 'ffmpeg.exe');
    } else if (Platform.isLinux || Platform.isMacOS) {
      return path.join(binDir, 'ffmpeg');
    }
    throw UnsupportedError('Unsupported platform');
  }

  /// Verifies the bundled ffprobe exists and is accessible
  static Future<bool> verifyBundledFfprobe() async {
    try {
      final ffprobePath = getBundledFfprobePath();
      final file = File(ffprobePath);

      if (await file.exists()) {
        // On Unix-like systems, check execute permission
        if (!Platform.isWindows) {
          final stat = await file.stat();
          if (stat.mode & 0x1 == 0) {
            // No execute permission
            if (kDebugMode) {
              print('ffprobe exists but is not executable: $ffprobePath');
            }
            return false;
          }
        }
        return true;
      }
      return false;
    } catch (e) {
      if (kDebugMode) {
        print('Error verifying bundled ffprobe: $e');
      }
      return false;
    }
  }

  /// Verifies the bundled ffmpeg exists and is accessible
  static Future<bool> verifyBundledFfmpeg() async {
    try {
      final ffmpegPath = getBundledFfmpegPath();
      final file = File(ffmpegPath);

      if (await file.exists()) {
        // On Unix-like systems, check execute permission
        if (!Platform.isWindows) {
          final stat = await file.stat();
          if (stat.mode & 0x1 == 0) {
            // No execute permission
            if (kDebugMode) {
              print('ffmpeg exists but is not executable: $ffmpegPath');
            }
            return false;
          }
        }
        return true;
      }
      return false;
    } catch (e) {
      if (kDebugMode) {
        print('Error verifying bundled ffmpeg: $e');
      }
      return false;
    }
  }

  static Future<MediaType> detectMediaType(String filePath) async {
    try {
      final hasFfprobe = await verifyBundledFfprobe();
      if (!hasFfprobe) {
        throw Exception('FFprobe not available for media type detection');
      }

      final ffprobePath = getBundledFfprobePath();
      final args = [
        '-v',
        'error',
        '-show_entries',
        'stream=codec_type',
        '-of',
        'default=noprint_wrappers=1',
        filePath,
      ];

      final result = await Process.run(ffprobePath, args);
      if (result.exitCode != 0) {
        throw Exception('FFprobe failed: ${result.stderr}');
      }

      final output = result.stdout.toString();
      if (output.contains('codec_type=video')) {
        return MediaType.video;
      } else if (output.contains('codec_type=audio')) {
        return MediaType.audio;
      }
      return MediaType.unknown;
    } catch (e) {
      if (kDebugMode) {
        print('Media type detection error: $e');
      }
      // Fallback to extension check if ffprobe fails
      return _fallbackMediaTypeDetection(filePath);
    }
  }

  /// Fallback media type detection using file extensions
  static MediaType _fallbackMediaTypeDetection(String filePath) {
    const videoExtensions = ['mp4', 'avi', 'mkv', 'mov', 'flv', 'wmv', 'webm'];
    const audioExtensions = ['mp3', 'wav', 'ogg', 'aac', 'm4a', 'flac'];

    final extension = path
        .extension(filePath)
        .toLowerCase()
        .replaceAll('.', '');

    if (videoExtensions.contains(extension)) {
      return MediaType.video;
    } else if (audioExtensions.contains(extension)) {
      return MediaType.audio;
    }
    return MediaType.unknown;
  }

  /// Executes an FFmpeg command with process management
  /// [args] - Command line arguments for FFmpeg
  /// [cancelToken] - Optional cancellation token
  /// Returns an [FfmpegResult] containing execution status and logs
  static Future<FfmpegResult> _executeFfmpegCommand(
    List<String> args, {
    Future<void>? cancelToken,
  }) async {
    final logBuffer = StringBuffer();
    int? processPid;
    Process? process;

    try {
      // Verify bundled ffmpeg is available
      final hasBundledFfmpeg = await verifyBundledFfmpeg();
      if (!hasBundledFfmpeg) {
        throw Exception('Bundled ffmpeg not found or not accessible');
      }

      final ffmpegPath = getBundledFfmpegPath();
      if (kDebugMode) {
        print('Using bundled ffmpeg at: $ffmpegPath');
      }

      process = await Process.start(ffmpegPath, args, runInShell: true);
      processPid = process.pid;
      FfmpegProcessManager().addProcess(processPid);
      if (kDebugMode) {
        print('FFmpeg process initiated with PID: $processPid');
      }

      final completer = Completer<void>();

      // Capture standard output
      process.stdout.transform(utf8.decoder).listen((data) {
        logBuffer.write(data);
      });

      // Capture standard error
      process.stderr.transform(utf8.decoder).listen((data) {
        logBuffer.write(data);
      }, onDone: () => completer.complete());

      // Handle cancellation request
      final cancelSub = cancelToken?.asStream().listen((_) {
        if (kDebugMode) {
          print('Cancellation initiated for process PID: $processPid');
        }
        process?.kill();
        FfmpegProcessManager().killProcess(processPid!);
      });

      final exitCode = await process.exitCode;
      await completer.future;
      cancelSub?.cancel();

      return FfmpegResult(
        exitCode == 0,
        'Exit Code: $exitCode\n${logBuffer.toString()}',
      );
    } catch (e) {
      return FfmpegResult(
        false,
        'Execution error: $e\n${logBuffer.toString()}',
      );
    } finally {
      if (processPid != null) {
        FfmpegProcessManager().removeProcess(processPid);
      }
    }
  }

  /// Helper to format milliseconds into HH:MM:SS.mmm  format
  static String _formatTime(int milliseconds) {
    final duration = Duration(milliseconds: milliseconds);
    final hours = duration.inHours.toString().padLeft(2, '0');
    final minutes = (duration.inMinutes % 60).toString().padLeft(2, '0');
    final seconds = (duration.inSeconds % 60).toString().padLeft(2, '0');
    final ms = (duration.inMilliseconds % 1000).toString().padLeft(3, '0');
    return '$hours:$minutes:$seconds.$ms';
  }

  /// Generates full media output with quality optimizations
  static Future<String> generateFullMedia({
    required String inputPath,
    String? outputVideoPath,
    required String outputAudioPath,
    required Map<String, String> localizedStrings,
    bool apply4K = false,
    void Function(String log)? onLog,
    Future<void>? cancelToken,
  }) async {
    final buffer = StringBuffer();
    final timestamp = DateTime.now().toIso8601String();

    try {
      // Verify required binaries
      final hasFfprobe = await verifyBundledFfprobe();
      final hasFfmpeg = await verifyBundledFfmpeg();

      if (!hasFfprobe || !hasFfmpeg) {
        final err = 'Required FFmpeg binaries not found or not accessible';
        buffer.writeln('[$timestamp]      ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      final mediaInfo = await _getMediaInfo(inputPath);
      final mediaType = await detectMediaType(inputPath);
      final isHDR = await _hasHdr(inputPath); // Check for HDR

      // ✨ MODIFIED: Video quality presets are now always for SDR output.
      // HDR-specific logic is removed from here and handled by the tonemap filter.
      Map<int, Map<String, dynamic>> videoQualityRecommendations = {
        2160: {
          // 4K UHD
          'crf': 18, 'bitrate': 28000, 'profile': 'high',
          'pixelFormat': 'yuv420p',
          'hdrParams': 'colorprim=bt709:transfer=bt709:colormatrix=bt709',
        },
        1440: {
          // QHD
          'crf': 20, 'bitrate': 22000, 'profile': 'high',
          'pixelFormat': 'yuv420p',
          'hdrParams': 'colorprim=bt709:transfer=bt709:colormatrix=bt709',
        },
        1080: {
          // Full HD
          'crf': 22, 'bitrate': 18000, 'profile': 'high',
          'pixelFormat': 'yuv420p',
          'hdrParams': 'colorprim=bt709:transfer=bt709:colormatrix=bt709',
        },
        720: {
          // HD
          'crf': 23, 'bitrate': 10000, 'profile': 'high',
          'pixelFormat': 'yuv420p',
          'hdrParams': 'colorprim=bt709:transfer=bt709:colormatrix=bt709',
        },
        480: {
          // SD
          'crf': 24, 'bitrate': 6000, 'profile': 'main',
          'pixelFormat': 'yuv420p',
          'hdrParams': 'colorprim=bt709:transfer=bt709:colormatrix=bt709',
        },
        360: {
          'crf': 25,
          'bitrate': 3000,
          'profile': 'main',
          'pixelFormat': 'yuv420p',
          'hdrParams': 'colorprim=bt709:transfer=bt709:colormatrix=bt709',
        },
      };

      const Map<String, int> audioBitrateRecommendations = {
        'aac': 320,
        'mp3': 320,
        'vorbis': 320,
        'flac': 0,
      };

      if (mediaType == MediaType.video) {
        if (outputVideoPath == null) {
          throw ArgumentError('outputVideoPath is required for video input');
        }

        final inputWidth = mediaInfo?.width ?? 1920;
        final inputHeight = mediaInfo?.height ?? 1080;
        final inputVideoBitrate = mediaInfo?.videoBitrate ?? 0;
        final inputAudioBitrate = mediaInfo?.audioBitrate ?? 0;

        int targetHeight = inputHeight;
        int targetWidth = inputWidth;

        if (!apply4K && inputHeight > 1080) {
          final aspectRatio = inputWidth / inputHeight;
          targetHeight = 1080;
          targetWidth = (1080 * aspectRatio).round();
        }

        final resolutions = videoQualityRecommendations.keys.toList();
        resolutions.sort((a, b) => b.compareTo(a));
        int? recommendedHeight;
        for (final res in resolutions) {
          if (targetHeight >= res) {
            recommendedHeight = res;
            break;
          }
        }
        recommendedHeight ??= resolutions.last;

        final recommendedSettings =
            videoQualityRecommendations[recommendedHeight]!;
        final recommendedCrf = recommendedSettings['crf'] as int;
        final recommendedProfile = recommendedSettings['profile'] as String;
        final pixelFormat = recommendedSettings['pixelFormat'] as String;
        final hdrParams = recommendedSettings['hdrParams'] as String?;

        buffer.writeln(
          '[$timestamp]      Input video resolution: ${inputWidth}x$inputHeight',
        );
        buffer.writeln(
          '[$timestamp]      Input video bitrate: ${inputVideoBitrate ~/ 1000} kbps',
        );
        buffer.writeln('[$timestamp]      HDR detected: $isHDR');
        buffer.writeln(
          '[$timestamp]      Output video resolution: ${targetWidth}x$targetHeight',
        );
        buffer.writeln(
          '[$timestamp]      Encoding strategy: CRF $recommendedCrf',
        );
        buffer.writeln(
          '[$timestamp]      Color format: $pixelFormat, Profile: $recommendedProfile',
        );
        onLog?.call(buffer.toString());

        final hwAccelMethod = await _getBestHardwareAccelerationMethod();
        final useHwAccel = hwAccelMethod != null;

        // ✨ NEW: Dynamically build the video filter chain
        final List<String> vfFilters = [];
        if (targetWidth != inputWidth || targetHeight != inputHeight) {
          vfFilters.add('scale=$targetWidth:$targetHeight');
        }
        if (isHDR) {
          // Add tonemapping to convert HDR to SDR. 'hable' is a good general-purpose algorithm.
          // 'format=yuv420p' ensures the output is in the standard SDR pixel format.
          vfFilters.add('tonemap=tonemap=hable,format=yuv420p');
        }

        // Build FFmpeg command with quality optimizations
        final videoCmd = [
          if (useHwAccel) ...['-hwaccel', hwAccelMethod],
          '-i', inputPath,
          '-c:v', 'libx264',
          '-crf', recommendedCrf.toString(),
          '-preset', 'slower',
          '-tune', 'film',
          '-x264opts',
          'ref=5:deblock=-1,-1:me=umh:subme=8:trellis=1:no-scenecut:rc-lookahead=30:keyint=60:min-keyint=30:aq-mode=2:b-adapt=2:direct=auto',
          '-profile:v', recommendedProfile,
          '-pix_fmt', pixelFormat,
          '-movflags', '+faststart',
          // ✨ MODIFIED: Add the combined video filters
          if (vfFilters.isNotEmpty) ...['-vf', vfFilters.join(',')],
          '-x264-params',
          hdrParams ??
              'colorprim=bt709:transfer=bt709:colormatrix=bt709:aq-mode=3',
          '-an', '-y', outputVideoPath,
        ];

        final targetAudioBitrate =
            inputAudioBitrate > 0
                ? (inputAudioBitrate > 320 * 1000
                    ? 320 * 1000
                    : inputAudioBitrate)
                : audioBitrateRecommendations['vorbis']! * 1000;

        final audioCmd = [
          '-i',
          inputPath,
          '-map',
          '0:a:0',
          '-vn',
          '-ar',
          '48000',
          '-c:a',
          'libvorbis',
          '-b:a',
          targetAudioBitrate.toString(),
          '-vbr',
          'on',
          '-compression_level',
          '10',
          '-frame_duration',
          '20',
          '-application',
          'lowdelay',
          '-y',
          outputAudioPath,
        ];

        final videoCmdString = videoCmd.join('  ');
        buffer.writeln(
          '[$timestamp]      ${localizedStrings["videoSplitCmd"]?.replaceFirst("%s", videoCmdString)}',
        );
        final videoResult = await _executeFfmpegCommand(
          videoCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(videoResult.log);
        onLog?.call(buffer.toString());

        if (!videoResult.success) {
          buffer.writeln(
            '[$timestamp]      ❌ ${localizedStrings["videoSplitFailed"]?.replaceFirst("%s", inputPath)}',
          );
          onLog?.call(buffer.toString());
          return buffer.toString();
        }

        final audioCmdString = audioCmd.join(' ');
        buffer.writeln(
          '[$timestamp]      ${localizedStrings["audioSplitCmd"]?.replaceFirst("%s", audioCmdString)}',
        );
        final audioResult = await _executeFfmpegCommand(
          audioCmd,
          cancelToken: cancelToken,
        );

        buffer.writeln(audioResult.log);
        onLog?.call(buffer.toString());

        if (!audioResult.success) {
          buffer.writeln(
            '[$timestamp]      ❌ ${localizedStrings["audioSplitFailed"]?.replaceFirst("%s", inputPath)}',
          );
          onLog?.call(buffer.toString());
        }

        if (videoResult.success) {
          buffer.writeln(
            '[$timestamp]      ✅ ${localizedStrings["videoSplitSuccess"]?.replaceFirst("%s", outputVideoPath)}',
          );
          // ✨ NEW: Add a warning if HDR to SDR conversion was performed.
          if (isHDR) {
            buffer.writeln(
              '[$timestamp]      ⚠️ ${localizedStrings["hdrToSdrWarning"] ?? "Warning: The original HDR video was converted to SDR for compatibility. The visual result may differ from the source."}',
            );
          }
        }
        if (audioResult.success) {
          buffer.writeln(
            '[$timestamp]      ✅ ${localizedStrings["audioSplitSuccess"]?.replaceFirst("%s", outputAudioPath)}',
          );
        }
      } else {
        // Audio-only processing (unchanged)
        final inputAudioBitrate = mediaInfo?.audioBitrate ?? 0;
        final targetAudioBitrate =
            inputAudioBitrate > 0
                ? (inputAudioBitrate > 320 * 1000
                    ? 320 * 1000
                    : inputAudioBitrate)
                : audioBitrateRecommendations['vorbis']! * 1000;

        buffer.writeln(
          '[$timestamp]      Input audio bitrate: ${inputAudioBitrate ~/ 1000} kbps',
        );
        buffer.writeln(
          '[$timestamp]      Selected audio bitrate: ${targetAudioBitrate ~/ 1000} kbps',
        );
        onLog?.call(buffer.toString());

        final audioCmd = [
          '-i',
          inputPath,
          '-map',
          '0:a:0',
          '-vn',
          '-ar',
          '48000',
          '-c:a',
          'libvorbis',
          '-b:a',
          targetAudioBitrate.toString(),
          '-vbr',
          'on',
          '-compression_level',
          '10',
          '-frame_duration',
          '20',
          '-application',
          'lowdelay',
          '-y',
          outputAudioPath,
        ];

        var result = await _executeFfmpegCommand(
          audioCmd,
          cancelToken: cancelToken,
        );

        if (!result.success) {
          buffer.writeln(
            '[$timestamp]      ⚠️ Original codec not supported, converting to Vorbis',
          );
          onLog?.call(buffer.toString());
          // Fallback command is the same as the initial command for Vorbis
          result = await _executeFfmpegCommand(
            audioCmd,
            cancelToken: cancelToken,
          );
        }

        buffer.writeln(result.log);
        onLog?.call(buffer.toString());

        if (result.success) {
          buffer.writeln(
            '[$timestamp]      ✅ ${localizedStrings["audioSplitSuccess"]?.replaceFirst("%s", outputAudioPath)}',
          );
        } else {
          buffer.writeln(
            '[$timestamp]      ❌ ${localizedStrings["audioSplitFailed"]?.replaceFirst("%s", inputPath)}',
          );
        }
      }
    } catch (e) {
      buffer.writeln('[$timestamp]      ❌ Error: $e');
      onLog?.call(buffer.toString());
    }

    return buffer.toString();
  }

  /// Helper method to check for HDR metadata
  static Future<bool> _hasHdr(String inputPath) async {
    try {
      final ffprobePath = getBundledFfprobePath();
      final args = [
        '-v',
        'error',
        '-select_streams',
        'v:0',
        '-show_entries',
        'stream=color_primaries,color_transfer,color_space,side_data_list',
        '-of',
        'json',
        inputPath,
      ];

      final result = await Process.run(ffprobePath, args);
      if (result.exitCode != 0) return false;

      final jsonData = json.decode(result.stdout as String);
      final streams = jsonData['streams'] as List<dynamic>?;
      if (streams == null || streams.isEmpty) return false;

      final stream = streams.first;

      // Check standard HDR metadata
      final isHdr =
          stream['color_primaries'] == 'bt2020' ||
          stream['color_transfer'] == 'smpte2084' ||
          stream['color_space'] == 'bt2020nc';

      // Check for Dolby Vision or HDR10+ side data
      final sideData = stream['side_data_list'] as List<dynamic>?;
      final hasHdrSideData =
          sideData?.any(
            (data) =>
                data['side_data_type'] == 'Dolby Vision Metadata' ||
                data['side_data_type'] == 'HDR Dynamic Metadata SMPTE2094-40',
          ) ??
          false;

      return isHdr || hasHdrSideData;
    } catch (e) {
      if (kDebugMode) {
        print('HDR detection error: $e');
      }
      return false;
    }
  }

  /// Gets the most appropriate hardware acceleration method for the current platform
  static Future<String?> _getBestHardwareAccelerationMethod() async {
    try {
      final result = await Process.run(getBundledFfmpegPath(), [
        '-hide_banner',
        '-hwaccels',
      ]);

      if (result.exitCode != 0) return null;

      final output = result.stdout.toString().toLowerCase();

      if (Platform.isWindows) {
        if (output.contains('d3d11va')) return 'd3d11va';
        if (output.contains('dxva2')) return 'dxva2';
        if (output.contains('cuda')) return 'cuda';
        if (output.contains('nvdec')) return 'nvdec';
      } else if (Platform.isMacOS) {
        if (output.contains('videotoolbox')) return 'videotoolbox';
      } else if (Platform.isLinux) {
        if (output.contains('vaapi')) return 'vaapi';
        if (output.contains('vdpau')) return 'vdpau';
        if (output.contains('nvdec')) return 'nvdec';
      }

      return null;
    } catch (e) {
      if (kDebugMode) {
        print('Hardware acceleration method detection failed: $e');
      }
      return null;
    }
  }

  static Future<Map<String, dynamic>> _analyzeAudio(String inputPath) async {
    try {
      final ffprobePath = getBundledFfprobePath();
      final args = [
        '-v',
        'error',
        '-select_streams',
        'a:0',
        '-show_entries',
        'stream=bit_rate,sample_rate,channels',
        '-show_entries',
        'format_tags=encoder',
        '-of',
        'json',
        inputPath,
      ];

      final result = await Process.run(ffprobePath, args);
      if (result.exitCode != 0) return {};

      final jsonData = json.decode(result.stdout as String);
      return jsonData;
    } catch (e) {
      if (kDebugMode) {
        print('Audio analysis error: $e');
      }
      return {};
    }
  }

  /// Generates a media preview based on input file type
  static Future<String> generatePreview({
    required String inputPath,
    String? outputVideoPath,
    required String outputAudioPath,
    required int startMs,
    required int endMs,
    required Map<String, String> localizedStrings,
    void Function(String log)? onLog,
    Future<void>? cancelToken,
  }) async {
    final buffer = StringBuffer();
    final timestamp = DateTime.now().toIso8601String();

    try {
      final hasFfmpeg = await verifyBundledFfmpeg();
      if (!hasFfmpeg) {
        final err = 'Bundled ffmpeg not found or not accessible';
        buffer.writeln('[$timestamp]      ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      final durationSec = (endMs - startMs) / 1000;
      if (durationSec <= 0) {
        final err = 'Invalid duration (end time must be after start time)';
        buffer.writeln('[$timestamp]      ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      final mediaType = await detectMediaType(inputPath);
      final isVideo = mediaType == MediaType.video;
      // ✨ NEW: Detect HDR for previews as well
      final isHDR = isVideo ? await _hasHdr(inputPath) : false;

      final mediaInfo = await _getMediaInfo(inputPath);

      const int maxVideoBitrate = 10 * 1000 * 1000;
      const int maxAudioBitrate = 128 * 1000;
      const int maxHeight = 1080;

      int targetAudioBitrate = maxAudioBitrate;
      int targetWidth = 1920;
      int targetHeight = 1080;

      if (mediaInfo != null) {
        if (isVideo) {
          if (mediaInfo.height > maxHeight) {
            final aspectRatio = mediaInfo.width / mediaInfo.height;
            targetHeight = maxHeight;
            targetWidth = (maxHeight * aspectRatio).round();
          } else {
            targetHeight = mediaInfo.height;
            targetWidth = mediaInfo.width;
          }
        }
        targetAudioBitrate =
            mediaInfo.audioBitrate < maxAudioBitrate
                ? mediaInfo.audioBitrate
                : maxAudioBitrate;
      }

      buffer.writeln(
        '[$timestamp]      Input file type: ${isVideo ? 'Video' : 'Audio'}',
      );
      if (isVideo) {
        buffer.writeln(
          '[$timestamp]      Target resolution: ${targetWidth}x$targetHeight',
        );
        // ✨ NEW: Log HDR detection for preview
        buffer.writeln('[$timestamp]      HDR Detected: $isHDR');
      }
      buffer.writeln(
        '[$timestamp]      Target audio bitrate: ${targetAudioBitrate ~/ 1000} kbps',
      );
      onLog?.call(buffer.toString());

      if (isVideo && outputVideoPath != null) {
        // ✨ NEW: Dynamically build filter chain for preview
        final List<String> vfFilters = [];
        vfFilters.add(
          'scale=$targetWidth:$targetHeight:force_original_aspect_ratio=decrease',
        );
        if (isHDR) {
          vfFilters.add('tonemap=tonemap=hable,format=yuv420p');
        }

        final videoCmd = [
          '-ss',
          _formatTime(startMs),
          '-i',
          inputPath,
          '-t',
          durationSec.toString(),
          '-c:v', 'libx264', '-preset', 'fast', '-crf', '23',
          '-maxrate',
          '${maxVideoBitrate ~/ 1000}k',
          '-bufsize',
          '${maxVideoBitrate ~/ 500}k',
          '-pix_fmt',
          'yuv420p',
          '-profile:v',
          'main',
          '-movflags',
          '+faststart',
          // ✨ MODIFIED: Add combined filter string
          if (vfFilters.isNotEmpty) ...['-vf', vfFilters.join(',')],
          '-an', '-y', outputVideoPath,
        ];

        final audioCmd = [
          '-ss',
          _formatTime(startMs),
          '-i',
          inputPath,
          '-t',
          durationSec.toString(),
          '-map',
          '0:a:0',
          '-vn',
          '-ar',
          '48000',
          '-c:a',
          'libvorbis',
          '-b:a',
          '${targetAudioBitrate ~/ 1000}k',
          '-vbr',
          'on',
          '-frame_duration',
          '20',
          '-application',
          'lowdelay',
          '-compression_level',
          '10',
          '-ac',
          '2',
          '-y',
          outputAudioPath,
        ];

        final videoResult = await _executeFfmpegCommand(
          videoCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(videoResult.log);
        onLog?.call(buffer.toString());

        if (!videoResult.success) {
          buffer.writeln(
            '[$timestamp]      ❌ ${localizedStrings["previewVideoSplitFailed"]?.replaceFirst("%s", inputPath)}',
          );
          onLog?.call(buffer.toString());
          return buffer.toString();
        }

        final audioResult = await _executeFfmpegCommand(
          audioCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(audioResult.log);
        onLog?.call(buffer.toString());

        if (!audioResult.success) {
          buffer.writeln(
            '[$timestamp]      ❌ ${localizedStrings["previewAudioSplitFailed"]?.replaceFirst("%s", inputPath)}',
          );
          onLog?.call(buffer.toString());
        }

        if (videoResult.success) {
          buffer.writeln(
            '[$timestamp]      ✅ ${localizedStrings["previewVideoSplitSuccess"]?.replaceFirst("%s", outputVideoPath)}',
          );
          // ✨ NEW: Add warning for preview if HDR was converted
          if (isHDR) {
            buffer.writeln(
              '[$timestamp]      ⚠️ ${localizedStrings["hdrToSdrWarning"] ?? "Warning: The original HDR video was converted to SDR for the preview. The visual result may differ from the source."}',
            );
          }
        }
        if (audioResult.success) {
          buffer.writeln(
            '[$timestamp]      ✅ ${localizedStrings["previewAudioSplitSuccess"]?.replaceFirst("%s", outputAudioPath)}',
          );
        }
      } else {
        // Audio-only preview (unchanged)
        final audioCmd = [
          '-ss',
          _formatTime(startMs),
          '-i',
          inputPath,
          '-t',
          durationSec.toString(),
          '-map',
          '0:a:0',
          '-vn',
          '-ar',
          '48000',
          '-c:a',
          'libvorbis',
          '-b:a',
          '${targetAudioBitrate ~/ 1000}k',
          '-vbr',
          'on',
          '-compression_level',
          '10',
          '-frame_duration',
          '20',
          '-application',
          'lowdelay',
          '-ac',
          '2',
          '-y',
          outputAudioPath,
        ];

        final result = await _executeFfmpegCommand(
          audioCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(result.log);
        onLog?.call(buffer.toString());

        if (result.success) {
          buffer.writeln(
            '[$timestamp]      ✅ ${localizedStrings["previewAudioSplitSuccess"]?.replaceFirst("%s", inputPath)}',
          );
        } else {
          buffer.writeln(
            '[$timestamp]      ❌ ${localizedStrings["previewAudioSplitFailed"]?.replaceFirst("%s", inputPath)}',
          );
        }
      }
    } catch (e) {
      final errLog = 'Preview generation error: $e';
      if (kDebugMode) {
        print(errLog);
      }
      buffer.writeln('[$timestamp]      ❌ $errLog');
      onLog?.call(buffer.toString());
    }

    return buffer.toString();
  }
}

/// Manages FFmpeg process lifecycle and provides cleanup capabilities
class FfmpegProcessManager {
  static final FfmpegProcessManager _instance =
      FfmpegProcessManager._internal();
  factory FfmpegProcessManager() => _instance;
  FfmpegProcessManager._internal();

  final Set<int> _runningPids = {};

  /// Registers a new process PID for management
  void addProcess(int pid) {
    _runningPids.add(pid);
    if (kDebugMode) {
      print('Process registered - PID: $pid, Active PIDs: $_runningPids');
    }
  }

  /// Deregisters a process PID
  void removeProcess(int pid) {
    _runningPids.remove(pid);
    if (kDebugMode) {
      print('Process deregistered - PID: $pid, Remaining PIDs: $_runningPids');
    }
  }

  /// Terminates a specific process
  void killProcess(int pid) {
    if (_runningPids.contains(pid)) {
      try {
        if (Platform.isWindows) {
          Process.run('taskkill', ['/F', '/T', '/PID', '$pid']);
        } else {
          Process.run('kill', ['-9', '$pid']);
        }
        if (kDebugMode) {
          print('Termination signal sent to process PID: $pid');
        }
      } catch (e) {
        if (kDebugMode) {
          print('Process termination failed for PID $pid: $e');
        }
      }
      _runningPids.remove(pid);
    }
  }

  /// Terminates all managed processes
  Future<void> killAll() async {
    final pidsToKill = Set<int>.from(_runningPids);
    _runningPids.clear(); // Clear PID registry immediately

    if (kDebugMode) {
      print('Initiating termination for all processes: $pidsToKill');
    }

    for (final pid in pidsToKill) {
      try {
        if (Platform.isWindows) {
          await Process.run('taskkill', ['/F', '/T', '/PID', '$pid']);
        } else {
          await Process.run('kill', ['-9', '$pid']);
        }
        if (kDebugMode) {
          print('Successfully terminated process PID: $pid');
        }
      } catch (e) {
        if (kDebugMode) {
          print('Error terminating process $pid: $e');
        }
      }
    }
  }
}
