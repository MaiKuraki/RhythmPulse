import 'dart:io';

import '../core/outcome.dart';
import '../platform/host.dart';
import 'process_runner.dart';

final class FfmpegBinaries {
  const FfmpegBinaries({required this.ffmpeg, required this.ffprobe});

  final String ffmpeg;
  final String ffprobe;
}

abstract interface class BinaryLocator {
  FfmpegBinaries get binaries;
  Future<Outcome<FfmpegBinaries>> verify(ProcessRunner runner);
  void invalidate();
}

/// Resolves binaries relative to the running executable, never the process
/// working directory. An explicit directory (env/CLI/testing) takes priority.
final class ExeRelativeBinaryLocator implements BinaryLocator {
  ExeRelativeBinaryLocator({
    required String executableDirectory,
    required HostOs os,
    String? overrideDirectory,
  })  : _directory = overrideDirectory ??
            _join(executableDirectory, 'data', 'ffmpeg-release-full-shared', 'bin'),
        _executables = FfmpegBinaries(
          ffmpeg: _join(
            overrideDirectory ??
                _join(executableDirectory, 'data', 'ffmpeg-release-full-shared', 'bin'),
            'ffmpeg${os == HostOs.windows ? '.exe' : ''}',
          ),
          ffprobe: _join(
            overrideDirectory ??
                _join(executableDirectory, 'data', 'ffmpeg-release-full-shared', 'bin'),
            'ffprobe${os == HostOs.windows ? '.exe' : ''}',
          ),
        );

  static String _join(String a, String b, [String? c, String? d]) {
    var result = a.endsWith('/') || a.endsWith('\\') ? a : '$a/';
    result = '$result$b';
    if (c != null) result = '$result/$c';
    if (d != null) result = '$result/$d';
    return result;
  }

  final String _directory;
  final FfmpegBinaries _executables;
  bool _verified = false;

  @override
  FfmpegBinaries get binaries => _executables;

  String get directory => _directory;

  @override
  void invalidate() => _verified = false;

  @override
  Future<Outcome<FfmpegBinaries>> verify(ProcessRunner runner) async {
    if (_verified) return Ok(_executables);
    for (final path in [_executables.ffmpeg, _executables.ffprobe]) {
      if (!File(path).existsSync()) {
        return Err(Failure(FailureKind.binaryUnavailable,
            'Required binary is missing: $path'));
      }
    }
    // A present file is not enough: missing DLLs or an incompatible build
    // must be detected before a task starts, not after a long encode.
    for (final path in [_executables.ffmpeg, _executables.ffprobe]) {
      final probe = await runner.run(ProcessRequest(
        label: 'binary-check',
        executable: path,
        arguments: const ['-hide_banner', '-version'],
        timeout: const Duration(seconds: 10),
        captureStdout: true,
      ));
      if (!probe.succeeded) {
        return Err(Failure(FailureKind.binaryUnavailable,
            'Binary is not runnable: $path (${probe.describe()})'));
      }
    }
    _verified = true;
    return Ok(_executables);
  }
}
