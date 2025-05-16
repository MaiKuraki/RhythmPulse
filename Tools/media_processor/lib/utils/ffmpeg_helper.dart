import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart' as path;

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
      // Verify bundled ffprobe is available
      final hasBundledFfprobe = await verifyBundledFfprobe();
      if (!hasBundledFfprobe) {
        throw Exception('Bundled ffprobe not found or not accessible');
      }

      final ffprobePath = getBundledFfprobePath();
      if (kDebugMode) {
        print('Using bundled ffprobe at: $ffprobePath');
      }

      // Configure arguments for video stream analysis
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

      if (videoProcess.exitCode != 0) {
        if (kDebugMode) {
          print('ffprobe video stream analysis failed: ${videoProcess.stderr}');
        }
        return null;
      }

      final videoJson = json.decode(videoProcess.stdout as String);
      final videoStreams = videoJson['streams'] as List<dynamic>?;
      if (videoStreams == null || videoStreams.isEmpty) {
        if (kDebugMode) {
          print('No video stream detected in the input file');
        }
        return null;
      }

      final videoStream = videoStreams.first;
      final int videoBitrate =
          int.tryParse(videoStream['bit_rate']?.toString() ?? '') ?? 0;
      final int width = videoStream['width'] ?? 0;
      final int height = videoStream['height'] ?? 0;

      // Configure arguments for audio stream analysis
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

      if (audioProcess.exitCode != 0) {
        if (kDebugMode) {
          print('ffprobe audio stream analysis failed: ${audioProcess.stderr}');
        }
        return null;
      }

      final audioJson = json.decode(audioProcess.stdout as String);
      final audioStreams = audioJson['streams'] as List<dynamic>?;
      if (audioStreams == null || audioStreams.isEmpty) {
        if (kDebugMode) {
          print('No audio stream detected in the input file');
        }
        return null;
      }

      final audioStream = audioStreams.first;
      final int audioBitrate =
          int.tryParse(audioStream['bit_rate']?.toString() ?? '') ?? 0;

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
      rethrow; // Rethrow to let caller handle the error
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

  /// Splits a media file into separate video and audio streams
  /// [inputPath] - Source media file path
  /// [outputVideoPath] - Destination path for video output
  /// [outputAudioPath] - Destination path for audio output
  /// [localizedStrings] - Localization strings for logging
  /// [onLog] - Optional callback for real-time logging
  /// [cancelToken] - Optional cancellation token for aborting the operation
  /// Returns a formatted log of the operation
  static Future<String> splitAudioVideo({
    required String inputPath,
    required String outputVideoPath,
    required String outputAudioPath,
    required Map<String, String> localizedStrings,
    bool apply4K = false,
    void Function(String log)? onLog,
    Future<void>? cancelToken,
  }) async {
    final buffer = StringBuffer();
    final timestamp = DateTime.now().toIso8601String();

    /// Formats template strings with provided parameters
    String format(String template, Map<String, dynamic> params) {
      return params.entries.fold(
        template,
        (result, entry) =>
            result.replaceAll('{{${entry.key}}}', entry.value.toString()),
      );
    }

    try {
      // Verify both ffprobe and ffmpeg are available
      final hasFfprobe = await verifyBundledFfprobe();
      final hasFfmpeg = await verifyBundledFfmpeg();

      if (!hasFfprobe || !hasFfmpeg) {
        final err = 'Required FFmpeg binaries not found or not accessible';
        buffer.writeln('[$timestamp]    ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }
      final mediaInfo = await _getMediaInfo(inputPath);

      if (mediaInfo == null) {
        final err = 'Unable to retrieve media metadata from input file';
        buffer.writeln('[$timestamp]   ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      // Calculate target resolution
      int targetHeight = mediaInfo.height;
      int targetWidth = mediaInfo.width;

      // If 4K is not selected and original height exceeds 1080, scale down
      if (!apply4K && mediaInfo.height > 1080) {
        final aspectRatio = mediaInfo.width / mediaInfo.height;
        targetHeight = 1080;
        targetWidth = (1080 * aspectRatio).round();
      }

      // Calculate bitrates with 5% margin
      final videoBitrate =
          mediaInfo.videoBitrate > 0
              ? (mediaInfo.videoBitrate * 1.05).toInt()
              : 1000 * 1000; // Default to 1Mbps if bitrate unavailable

      // Adjust bitrate proportionally if resolution was scaled down
      final adjustedBitrate =
          !apply4K && mediaInfo.height > 1080
              ? (videoBitrate * (1080 / mediaInfo.height)).toInt()
              : videoBitrate;

      final audioBitrate =
          mediaInfo.audioBitrate > 0
              ? (mediaInfo.audioBitrate * 1.05).toInt()
              : 128 * 1000; // Default to 128kbps if bitrate unavailable

      // Video extraction command configuration
      final videoCmd = [
        '-i', inputPath,
        '-c:v', 'libx264',
        '-b:v', adjustedBitrate.toString(),
        '-vf', 'scale=$targetWidth:$targetHeight',
        '-an', // Disable audio
        '-y', // Overwrite output without confirmation
        outputVideoPath,
      ];

      // Audio extraction command configuration
      final audioCmd = [
        '-i', inputPath,
        '-vn', // Disable video
        '-c:a', 'libvorbis',
        '-b:a', audioBitrate.toString(),
        '-y', // Overwrite output without confirmation
        outputAudioPath,
      ];

      // Prepare localized log messages
      final videoSplitCmdLog = format(
        localizedStrings['videoSplitCmd'] ??
            'Video extraction command: {{cmd}}',
        {'cmd': videoCmd.join('   ')},
      );
      final videoSplitSuccessLog = format(
        localizedStrings['videoSplitSuccess'] ??
            'Video successfully extracted to: {{path}}',
        {'path': outputVideoPath},
      );
      final videoSplitFailedLog =
          localizedStrings['videoSplitFailed'] ?? 'Video extraction failed';

      final audioSplitCmdLog = format(
        localizedStrings['audioSplitCmd'] ??
            'Audio extraction command: {{cmd}}',
        {'cmd': audioCmd.join('   ')},
      );
      final audioSplitSuccessLog = format(
        localizedStrings['audioSplitSuccess'] ??
            'Audio successfully extracted to: {{path}}',
        {'path': outputAudioPath},
      );
      final audioSplitFailedLog =
          localizedStrings['audioSplitFailed'] ?? 'Audio extraction failed';

      // Execute video extraction
      buffer.writeln('[$timestamp]   $videoSplitCmdLog');
      onLog?.call(buffer.toString());

      final videoResult = await _executeFfmpegCommand(
        videoCmd,
        cancelToken: cancelToken,
      );

      buffer.writeln(videoResult.log);
      onLog?.call(buffer.toString());

      if (videoResult.success) {
        buffer.writeln('[$timestamp]   ✅ $videoSplitSuccessLog');
        onLog?.call(buffer.toString());
      } else {
        buffer.writeln('[$timestamp]   ❌ $videoSplitFailedLog');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      // Execute audio extraction
      buffer.writeln('[$timestamp]   $audioSplitCmdLog');
      onLog?.call(buffer.toString());

      final audioResult = await _executeFfmpegCommand(
        audioCmd,
        cancelToken: cancelToken,
      );

      buffer.writeln(audioResult.log);
      onLog?.call(buffer.toString());

      if (audioResult.success) {
        buffer.writeln('[$timestamp]   ✅ $audioSplitSuccessLog');
        onLog?.call(buffer.toString());
      } else {
        buffer.writeln('[$timestamp]   ❌ $audioSplitFailedLog');
        onLog?.call(buffer.toString());
      }
    } catch (e) {
      final errLog = 'FFmpeg operation encountered an error: $e';
      if (kDebugMode) {
        print(errLog);
      }
      buffer.writeln('[$timestamp]   ❌ $errLog');
      onLog?.call(buffer.toString());
    }

    return buffer.toString();
  }

  /// Generates a media preview based on input file type
  /// [inputPath] - Source media file path
  /// [outputPath] - Destination path for preview output
  /// [startMs] - Start time in milliseconds
  /// [endMs] - End time in milliseconds
  /// [isVideo] - Whether input is a video file
  /// [apply4K] - Whether to apply 4K resolution
  /// [localizedStrings] - Localization strings for logging
  /// [onLog] - Optional callback for real-time logging
  /// [cancelToken] - Optional cancellation token
  /// Returns a formatted log of the operation
  static Future<String> generatePreview({
    required String inputPath,
    required String outputPath,
    required int startMs,
    required int endMs,
    required bool isVideo,
    required bool apply4K,
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
        buffer.writeln('[$timestamp]     ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      // Calculate duration in seconds
      final durationSec = (endMs - startMs) / 1000;
      if (durationSec <= 0) {
        final err = 'Invalid duration (end time must be after start time)';
        buffer.writeln('[$timestamp]    ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      // Get media info for video files to calculate resolution
      MediaInfo? mediaInfo;
      if (isVideo) {
        mediaInfo = await _getMediaInfo(inputPath);
        if (mediaInfo == null) {
          final err = 'Unable to retrieve video metadata';
          buffer.writeln('[$timestamp]    ❌ $err');
          onLog?.call(buffer.toString());
          return buffer.toString();
        }
      }

      // Prepare FFmpeg command
      final List<String> cmd = [
        '-ss', _formatTime(startMs), // Start position
        '-i', inputPath, // Input file
        '-t', durationSec.toString(), // Duration
      ];

      // Video specific options
      if (isVideo) {
        // Calculate target resolution
        int targetHeight = mediaInfo!.height;
        int targetWidth = mediaInfo.width;

        if (!apply4K && mediaInfo.height > 1080) {
          final aspectRatio = mediaInfo.width / mediaInfo.height;
          targetHeight = 1080;
          targetWidth = (1080 * aspectRatio).round();
        }

        cmd.addAll([
          '-c:v', 'libx264', // Video codec
          '-vf', 'scale=$targetWidth:$targetHeight', // Scaling
          '-c:a', 'libvorbis', // Audio codec
        ]);
      } else {
        // Audio only options
        cmd.addAll([
          '-vn', // No video
          '-c:a', 'libvorbis', // Audio codec
        ]);
      }

      cmd.addAll([
        '-y', // Overwrite output
        outputPath, // Output file
      ]);

      // Prepare localized log messages
      final previewCmdLog = format(
        localizedStrings['previewCmd'] ?? 'Preview command: {{cmd}}',
        {'cmd': cmd.join('    ')},
      );
      final previewSuccessLog = format(
        localizedStrings['previewSuccess'] ??
            'Preview successfully generated: {{path}}',
        {'path': outputPath},
      );
      final previewFailedLog =
          localizedStrings['previewFailed'] ?? 'Preview generation failed';

      // Execute command
      buffer.writeln('[$timestamp]    $previewCmdLog');
      onLog?.call(buffer.toString());

      final result = await _executeFfmpegCommand(cmd, cancelToken: cancelToken);

      buffer.writeln(result.log);
      onLog?.call(buffer.toString());

      if (result.success) {
        buffer.writeln('[$timestamp]    ✅ $previewSuccessLog');
        onLog?.call(buffer.toString());
      } else {
        buffer.writeln('[$timestamp]    ❌ $previewFailedLog');
        onLog?.call(buffer.toString());
      }
    } catch (e) {
      final errLog = 'Preview generation error: $e';
      if (kDebugMode) {
        print(errLog);
      }
      buffer.writeln('[$timestamp]    ❌ $errLog');
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
