import 'dart:io';
import 'dart:convert';
import 'dart:async';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart' as path;

class FfmpegResult {
  final bool success;
  final String log;

  FfmpegResult(this.success, this.log);
}

class MediaInfo {
  final int videoBitrate; // bps
  final int width;
  final int height;
  final int audioBitrate; // bps

  MediaInfo({
    required this.videoBitrate,
    required this.width,
    required this.height,
    required this.audioBitrate,
  });
}

class FFmpegHelper {
  static Future<MediaInfo?> _getMediaInfo(String inputPath) async {
    try {
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

      final videoProcess = await Process.run('ffprobe', videoArgs);

      if (videoProcess.exitCode != 0) {
        if (kDebugMode) {
          print('ffprobe video stream failed: ${videoProcess.stderr}');
        }
        return null;
      }

      final videoJson = json.decode(videoProcess.stdout as String);
      final videoStreams = videoJson['streams'] as List<dynamic>?;
      if (videoStreams == null || videoStreams.isEmpty) {
        if (kDebugMode) {
          print('No video stream found');
        }
        return null;
      }

      final videoStream = videoStreams.first;
      final int videoBitrate =
          int.tryParse(videoStream['bit_rate']?.toString() ?? '') ?? 0;
      final int width = videoStream['width'] ?? 0;
      final int height = videoStream['height'] ?? 0;

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

      final audioProcess = await Process.run('ffprobe', audioArgs);

      if (audioProcess.exitCode != 0) {
        if (kDebugMode) {
          print('ffprobe audio stream failed: ${audioProcess.stderr}');
        }
        return null;
      }

      final audioJson = json.decode(audioProcess.stdout as String);
      final audioStreams = audioJson['streams'] as List<dynamic>?;
      if (audioStreams == null || audioStreams.isEmpty) {
        if (kDebugMode) {
          print('No audio stream found');
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
        print('Error getting media info: $e');
      }
      return null;
    }
  }

  static Future<String> splitAudioVideo({
    required String inputPath,
    required String outputVideoPath,
    required String outputAudioPath,
    required Map<String, String> localizedStrings,
    void Function(String log)? onLog,
  }) async {
    final buffer = StringBuffer();
    final timestamp = DateTime.now().toIso8601String();

    String format(String template, Map<String, dynamic> params) {
      return params.entries.fold(
        template,
        (result, entry) =>
            result.replaceAll('{${entry.key}}', entry.value.toString()),
      );
    }

    try {
      final mediaInfo = await _getMediaInfo(inputPath);

      if (mediaInfo == null) {
        final err = 'Failed to get media info for input file.';
        buffer.writeln('[$timestamp]   ❌ $err');
        onLog?.call(buffer.toString());
        return buffer.toString();
      }

      final videoBitrate =
          mediaInfo.videoBitrate > 0
              ? (mediaInfo.videoBitrate * 1.05).toInt()
              : 1000 * 1000;

      final audioBitrate =
          mediaInfo.audioBitrate > 0
              ? (mediaInfo.audioBitrate * 1.05).toInt()
              : 128 * 1000;

      final videoCmd = [
        '-i',
        inputPath,
        '-c:v',
        'libx264',
        '-b:v',
        videoBitrate.toString(),
        '-vf',
        'scale=${mediaInfo.width}:${mediaInfo.height}',
        '-an',
        '-y',
        outputVideoPath,
      ];

      final audioCmd = [
        '-i',
        inputPath,
        '-vn',
        '-c:a',
        'libvorbis',
        '-b:a',
        audioBitrate.toString(),
        '-y',
        outputAudioPath,
      ];

      final videoCmdStr = videoCmd.join(' ');
      final audioCmdStr = audioCmd.join(' ');

      final videoSplitCmdLog = format(
        localizedStrings['videoSplitCmd'] ?? 'Split video command: {cmd}',
        {'cmd': videoCmdStr},
      );
      final videoSplitSuccessLog = format(
        localizedStrings['videoSplitSuccess'] ?? 'Video split success: {path}',
        {'path': outputVideoPath},
      );
      final videoSplitFailedLog =
          localizedStrings['videoSplitFailed'] ?? 'Video split failed';

      final audioSplitCmdLog = format(
        localizedStrings['audioSplitCmd'] ?? 'Split audio command: {cmd}',
        {'cmd': audioCmdStr},
      );
      final audioSplitSuccessLog = format(
        localizedStrings['audioSplitSuccess'] ?? 'Audio split success: {path}',
        {'path': outputAudioPath},
      );
      final audioSplitFailedLog =
          localizedStrings['audioSplitFailed'] ?? 'Audio split failed';

      buffer.writeln('[$timestamp]   $videoSplitCmdLog');
      onLog?.call(buffer.toString());

      final videoResult = await _executeFfmpegCommand(videoCmd);

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

      buffer.writeln('[$timestamp]   $audioSplitCmdLog');
      onLog?.call(buffer.toString());

      final audioResult = await _executeFfmpegCommand(audioCmd);

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
      final errLog = 'Error during FFmpeg operation: $e';
      if (kDebugMode) {
        print(errLog);
      }
      buffer.writeln('[$timestamp]   ❌ $errLog');
      onLog?.call(buffer.toString());
    }

    return buffer.toString();
  }

  static Future<FfmpegResult> _executeFfmpegCommand(List<String> args) async {
    final logBuffer = StringBuffer();

    try {
      late Process process;
      String ffmpegPath;

      if (Platform.isWindows) {
        ffmpegPath = path.join(
          Directory.current.path,
          'data',
          'ffmpeg-release-full-shared',
          'bin',
          'ffmpeg.exe',
        );
        final ffmpegFile = File(ffmpegPath);
        if (!ffmpegFile.existsSync()) {
          final err = 'FFmpeg executable not found at $ffmpegPath';
          if (kDebugMode) {
            print(err);
          }
          return FfmpegResult(false, err);
        }
        logBuffer.writeln(
          'Executing FFmpeg command: $ffmpegPath ${args.join(' ')}',
        );
        process = await Process.start(ffmpegPath, args, runInShell: true);
      } else {
        ffmpegPath = 'ffmpeg';
        logBuffer.writeln('Executing FFmpeg command: ffmpeg ${args.join(' ')}');
        process = await Process.start('ffmpeg', args, runInShell: true);
      }

      // 注册进程
      FfmpegProcessManager().addProcess(process);

      final completer = Completer<void>();

      process.stdout.transform(utf8.decoder).listen((data) {
        logBuffer.write(data);
        if (kDebugMode) {
          print('FFmpeg stdout: $data');
        }
      });

      process.stderr
          .transform(utf8.decoder)
          .listen(
            (data) {
              logBuffer.write(data);
              if (kDebugMode) {
                print('FFmpeg stderr: $data');
              }
            },
            onDone: () {
              completer.complete();
            },
          );

      final exitCode = await process.exitCode;
      await completer.future;

      // 进程结束，移除
      FfmpegProcessManager().removeProcess(process);

      if (exitCode == 0) {
        logBuffer.writeln('FFmpeg command executed successfully.');
        return FfmpegResult(true, logBuffer.toString());
      } else {
        logBuffer.writeln('FFmpeg command failed with exit code $exitCode.');
        return FfmpegResult(false, logBuffer.toString());
      }
    } catch (e) {
      final err = 'Error executing FFmpeg command: $e';
      if (kDebugMode) {
        print(err);
      }
      logBuffer.writeln(err);
      return FfmpegResult(false, logBuffer.toString());
    }
  }
}

class FfmpegProcessManager {
  static final FfmpegProcessManager _instance =
      FfmpegProcessManager._internal();

  factory FfmpegProcessManager() => _instance;

  FfmpegProcessManager._internal();

  final List<Process> _runningProcesses = [];

  void addProcess(Process process) {
    _runningProcesses.add(process);
  }

  void removeProcess(Process process) {
    _runningProcesses.remove(process);
  }

  /// 杀死所有正在运行的 FFmpeg 进程
  Future<void> killAll() async {
    for (final process in List<Process>.from(_runningProcesses)) {
      try {
        if (process.kill(ProcessSignal.sigterm)) {
          // 等待进程退出，超时后强制杀死
          await process.exitCode.timeout(
            const Duration(seconds: 3),
            onTimeout: () {
              process.kill(ProcessSignal.sigkill);
              return -1; // 必须返回 int
            },
          );
        }
      } catch (e) {
        if (kDebugMode) {
          print('Error killing FFmpeg process: $e');
        }
      }
      _runningProcesses.remove(process);
    }
  }
}
