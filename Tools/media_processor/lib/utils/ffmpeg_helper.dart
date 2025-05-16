import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:flutter/foundation.dart';
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

  /// Generates full media output based on input type
  /// For video: outputs video-only and audio-only files
  /// For audio: outputs a single ogg file with same quality
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

    String format(String template, Map<String, dynamic> params) {
      return params.entries.fold(
        template,
        (result, entry) =>
            result.replaceAll('{{${entry.key}}}', entry.value.toString()),
      );
    }

    try {
      // Verify required binaries
      final hasFfprobe = await verifyBundledFfprobe();
      final hasFfmpeg = await verifyBundledFfmpeg();

      if (!hasFfprobe || !hasFfmpeg) {
        final err = 'Required FFmpeg binaries not found or not accessible';
        buffer.writeln('[$timestamp]   ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      final mediaInfo = await _getMediaInfo(inputPath);
      final mediaType = await detectMediaType(inputPath);

      // Default bitrates
      const int defaultVideoBitrate = 30 * 1000 * 1000; // 30 Mbps
      const int defaultAudioBitrate = 320 * 1000; // 320 kbps

      // Function to calculate recommended video bitrate based on resolution
      int calculateRecommendedVideoBitrate(int width, int height) {
        final resolution = width * height;

        // Bitrate recommendations based on resolution (in bits per pixel per frame)
        // These are general guidelines and can be adjusted
        if (resolution >= 3840 * 2160) {
          // 4K
          return 50 * 1000 * 1000; // 50 Mbps
        } else if (resolution >= 2560 * 1440) {
          // 1440p
          return 30 * 1000 * 1000; // 30 Mbps
        } else if (resolution >= 1920 * 1080) {
          // 1080p
          return 20 * 1000 * 1000; // 20 Mbps
        } else if (resolution >= 1280 * 720) {
          // 720p
          return 10 * 1000 * 1000; // 10 Mbps
        } else {
          // SD
          return 6 * 1000 * 1000; // 6 Mbps
        }
      }

      if (mediaType == MediaType.video) {
        // Video processing logic
        if (outputVideoPath == null) {
          throw ArgumentError('outputVideoPath is required for video input');
        }

        // Get input video properties
        final inputWidth = mediaInfo?.width ?? 1920;
        final inputHeight = mediaInfo?.height ?? 1080;
        final inputVideoBitrate =
            mediaInfo?.videoBitrate ?? defaultVideoBitrate;
        final inputAudioBitrate =
            mediaInfo?.audioBitrate ?? defaultAudioBitrate;

        // Calculate target resolution
        int targetHeight = inputHeight;
        int targetWidth = inputWidth;

        if (!apply4K && inputHeight > 1080) {
          final aspectRatio = inputWidth / inputHeight;
          targetHeight = 1080;
          targetWidth = (1080 * aspectRatio).round();
        }

        // Calculate target video bitrate
        int targetVideoBitrate;
        if (inputVideoBitrate < defaultVideoBitrate) {
          // If input bitrate is lower than default, use input bitrate
          targetVideoBitrate = inputVideoBitrate;
        } else {
          // Calculate recommended bitrate based on output resolution
          final recommendedBitrate = calculateRecommendedVideoBitrate(
            targetWidth,
            targetHeight,
          );

          // If input is higher than default, use the higher of recommended or default
          targetVideoBitrate =
              recommendedBitrate > defaultVideoBitrate
                  ? recommendedBitrate
                  : defaultVideoBitrate;
        }

        // For 4K output, ensure minimum bitrate
        if (apply4K &&
            targetHeight >= 2160 &&
            targetVideoBitrate < 50 * 1000 * 1000) {
          targetVideoBitrate = 50 * 1000 * 1000; // 50 Mbps for 4K
        }

        // Calculate target audio bitrate
        int targetAudioBitrate;
        if (inputAudioBitrate < defaultAudioBitrate) {
          // If input audio bitrate is lower than default, use input bitrate
          targetAudioBitrate = inputAudioBitrate;
        } else {
          // For high quality audio, use 320kbps or higher if input is higher
          targetAudioBitrate =
              inputAudioBitrate > defaultAudioBitrate
                  ? inputAudioBitrate
                  : defaultAudioBitrate;
        }

        // Log the bitrate decisions
        buffer.writeln(
          '[$timestamp]   Input video resolution: ${inputWidth}x$inputHeight',
        );
        buffer.writeln(
          '[$timestamp]   Input video bitrate: ${inputVideoBitrate ~/ 1000} kbps',
        );
        buffer.writeln(
          '[$timestamp]   Output video resolution: ${targetWidth}x$targetHeight',
        );
        buffer.writeln(
          '[$timestamp]   Selected video bitrate: ${targetVideoBitrate ~/ 1000} kbps',
        );
        buffer.writeln(
          '[$timestamp]   Selected audio bitrate: ${targetAudioBitrate ~/ 1000} kbps',
        );
        onLog?.call(buffer.toString());

        // Video extraction command
        final videoCmd = [
          '-i', inputPath,
          '-c:v', 'libx264',
          '-b:v', targetVideoBitrate.toString(),
          '-vf', 'scale=$targetWidth:$targetHeight',
          '-an', // No audio
          '-y', // Overwrite
          outputVideoPath,
        ];

        // Audio extraction command
        final audioCmd = [
          '-i', inputPath,
          '-vn', // No video
          '-c:a', 'libvorbis',
          '-b:a', targetAudioBitrate.toString(),
          '-y', // Overwrite
          outputAudioPath,
        ];

        // Execute both commands
        final videoResult = await _executeFfmpegCommand(
          videoCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(videoResult.log);
        onLog?.call(buffer.toString());

        if (!videoResult.success) {
          buffer.writeln('[$timestamp]   ❌ Video extraction failed');
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
          buffer.writeln('[$timestamp]   ❌ Audio extraction failed');
          onLog?.call(buffer.toString());
        }

        buffer.writeln(
          '[$timestamp]  ✅ Video and audio extracted successfully',
        );
      } else {
        // Audio-only processing
        final inputAudioBitrate =
            mediaInfo?.audioBitrate ?? defaultAudioBitrate;

        // Calculate target audio bitrate
        int targetAudioBitrate;
        if (inputAudioBitrate < defaultAudioBitrate) {
          // If input audio bitrate is lower than default, use input bitrate
          targetAudioBitrate = inputAudioBitrate;
        } else {
          // For high quality audio, use 320kbps or higher if input is higher
          targetAudioBitrate =
              inputAudioBitrate > defaultAudioBitrate
                  ? inputAudioBitrate
                  : defaultAudioBitrate;
        }

        buffer.writeln(
          '[$timestamp]   Input audio bitrate: ${inputAudioBitrate ~/ 1000} kbps',
        );
        buffer.writeln(
          '[$timestamp]   Selected audio bitrate: ${targetAudioBitrate ~/ 1000} kbps',
        );
        onLog?.call(buffer.toString());

        final audioCmd = [
          '-i', inputPath,
          '-c:a', 'copy', // Keep original codec
          '-b:a', targetAudioBitrate.toString(),
          '-y', // Overwrite
          outputAudioPath,
        ];

        final result = await _executeFfmpegCommand(
          audioCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(result.log);
        onLog?.call(buffer.toString());

        if (result.success) {
          buffer.writeln('[$timestamp]   ✅ Audio extracted successfully');
        } else {
          buffer.writeln('[$timestamp]   ❌ Audio extraction failed');
        }
      }
    } catch (e) {
      buffer.writeln('[$timestamp]   ❌ Error: $e');
      onLog?.call(buffer.toString());
    }

    return buffer.toString();
  }

  /// Generates a media preview based on input file type
  /// [inputPath] - Source media file path
  /// [outputPath] - Destination path for preview output
  /// [startMs] - Start time in milliseconds
  /// [endMs] - End time in milliseconds
  /// [localizedStrings] - Localization strings for logging
  /// [onLog] - Optional callback for real-time logging
  /// [cancelToken] - Optional cancellation token
  /// Returns a formatted log of the operation
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

    String format(String template, Map<String, dynamic> params) {
      return params.entries.fold(
        template,
        (result, entry) =>
            result.replaceAll('{{${entry.key}}}', entry.value.toString()),
      );
    }

    try {
      // Verify ffmpeg is available
      final hasFfmpeg = await verifyBundledFfmpeg();
      if (!hasFfmpeg) {
        final err = 'Bundled ffmpeg not found or not accessible';
        buffer.writeln('[$timestamp]      ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      // Calculate duration in seconds
      final durationSec = (endMs - startMs) / 1000;
      if (durationSec <= 0) {
        final err = 'Invalid duration (end time must be after start time)';
        buffer.writeln('[$timestamp]     ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      // Detect media type
      final mediaType = await detectMediaType(inputPath);
      final isVideo = mediaType == MediaType.video;

      // Track output files for cleanup
      final outputFiles = <String>[outputAudioPath];
      if (isVideo && outputVideoPath != null) {
        outputFiles.add(outputVideoPath);
      }

      // Get media info to determine input parameters
      final mediaInfo = await _getMediaInfo(inputPath);

      // Set maximum limits
      const int maxVideoBitrate = 20 * 1000 * 1000; // 20 Mbps
      const int maxAudioBitrate = 128 * 1000; // 128 kbps
      const int maxHeight = 1080; // 1080p

      // Calculate target parameters
      int targetVideoBitrate = maxVideoBitrate;
      int targetAudioBitrate = maxAudioBitrate;
      int targetWidth = 1920;
      int targetHeight = 1080;

      if (mediaInfo != null) {
        // Video parameters
        if (isVideo) {
          // Use input bitrate if it's lower than our max
          targetVideoBitrate =
              mediaInfo.videoBitrate < maxVideoBitrate
                  ? mediaInfo.videoBitrate
                  : maxVideoBitrate;

          // Calculate target resolution while maintaining aspect ratio
          if (mediaInfo.height > maxHeight) {
            final aspectRatio = mediaInfo.width / mediaInfo.height;
            targetHeight = maxHeight;
            targetWidth = (maxHeight * aspectRatio).round();
          } else {
            targetHeight = mediaInfo.height;
            targetWidth = mediaInfo.width;
          }
        }

        // Audio parameters
        targetAudioBitrate =
            mediaInfo.audioBitrate < maxAudioBitrate
                ? mediaInfo.audioBitrate
                : maxAudioBitrate;
      }

      buffer.writeln(
        '[$timestamp]    Input file type: ${isVideo ? 'Video' : 'Audio'}',
      );
      if (isVideo) {
        buffer.writeln(
          '[$timestamp]    Target resolution: ${targetWidth}x$targetHeight',
        );
        buffer.writeln(
          '[$timestamp]    Target video bitrate: ${targetVideoBitrate ~/ 1000} kbps',
        );
      }
      buffer.writeln(
        '[$timestamp]    Target audio bitrate: ${targetAudioBitrate ~/ 1000} kbps',
      );
      onLog?.call(buffer.toString());

      // Execute commands based on media type
      if (isVideo && outputVideoPath != null) {
        // Video preview command - extract video portion
        final videoCmd = [
          '-ss', _formatTime(startMs), // Start position
          '-i', inputPath, // Input file
          '-t', durationSec.toString(), // Duration
          '-c:v', 'libx264', // Video codec
          '-b:v', targetVideoBitrate.toString(), // Video bitrate
          '-vf', 'scale=$targetWidth:$targetHeight', // Scaling
          '-an', // No audio
          '-y', // Overwrite
          outputVideoPath,
        ];

        // Audio preview command - extract audio portion
        final audioCmd = [
          '-ss', _formatTime(startMs), // Start position
          '-i', inputPath, // Input file
          '-t', durationSec.toString(), // Duration
          '-vn', // No video
          '-c:a', 'libvorbis', // Audio codec
          '-b:a', targetAudioBitrate.toString(), // Audio bitrate
          '-y', // Overwrite
          outputAudioPath,
        ];

        // Execute video extraction
        final videoResult = await _executeFfmpegCommand(
          videoCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(videoResult.log);
        onLog?.call(buffer.toString());

        if (!videoResult.success) {
          buffer.writeln('[$timestamp]    ❌ Video preview extraction failed');
          onLog?.call(buffer.toString());
          return buffer.toString();
        }

        // Execute audio extraction
        final audioResult = await _executeFfmpegCommand(
          audioCmd,
          cancelToken: cancelToken,
        );
        buffer.writeln(audioResult.log);
        onLog?.call(buffer.toString());

        if (!audioResult.success) {
          buffer.writeln('[$timestamp]    ❌ Audio preview extraction failed');
          onLog?.call(buffer.toString());
        }

        buffer.writeln(
          '[$timestamp]   ✅ Video and audio preview extracted successfully',
        );
      } else {
        // Audio-only preview command
        final audioCmd = [
          '-ss', _formatTime(startMs), // Start position
          '-i', inputPath, // Input file
          '-t', durationSec.toString(), // Duration
          '-vn', // No video
          '-c:a', 'libvorbis', // Audio codec
          '-b:a', targetAudioBitrate.toString(), // Audio bitrate
          '-y', // Overwrite
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
            '[$timestamp]    ✅ Audio preview extracted successfully',
          );
        } else {
          buffer.writeln('[$timestamp]    ❌ Audio preview extraction failed');
        }
      }
    } catch (e) {
      final errLog = 'Preview generation error: $e';
      if (kDebugMode) {
        print(errLog);
      }
      buffer.writeln('[$timestamp]     ❌ $errLog');
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
