import 'dart:convert';
import 'dart:io';

import '../core/cancellation.dart';
import '../core/log.dart';
import '../core/outcome.dart';
import '../core/ring_buffer.dart';
import 'binaries.dart';
import 'command_builder.dart';
import 'process_runner.dart';
import 'package:path/path.dart' as p;

/// Content-addressed waveform cache.
///
/// The cache key includes path, size and mtime, so editing a file invalidates
/// its waveform without any stale-path heuristics. Cached PNGs live in one
/// application-owned directory and the LRU evicts the corresponding file.
final class WaveformService {
  WaveformService({
    required ProcessRunner runner,
    required BinaryLocator binaries,
    required LogStore logs,
    required String cacheDirectory,
    int capacity = 32,
    Duration timeout = const Duration(seconds: 90),
  })  : _runner = runner,
        _binaries = binaries,
        _logs = logs,
        _cacheDirectory = cacheDirectory,
        _timeout = timeout {
    _cache = LruCache<String, String>(capacity, onEvict: (_, path) {
      try {
        final file = File(path);
        if (file.existsSync()) file.deleteSync();
      } catch (_) {
        // Cache eviction must never fail a user operation.
      }
    });
  }

  final ProcessRunner _runner;
  final BinaryLocator _binaries;
  final LogStore _logs;
  final String _cacheDirectory;
  final Duration _timeout;
  late final LruCache<String, String> _cache;

  CacheStats get cacheStats => _cache.stats;

  Future<Outcome<String>> ensure(
    String inputPath, {
    int width = 2048,
    int height = 200,
    CancellationToken? cancellation,
  }) async {
    FileStat stat;
    try {
      stat = await File(inputPath).stat();
      if (stat.type == FileSystemEntityType.notFound) {
        return Err(Failure(FailureKind.invalidInput,
            'Waveform source does not exist: $inputPath'));
      }
    } catch (e) {
      return Err(Failure(FailureKind.invalidInput,
          'Cannot stat waveform source: $inputPath', cause: e));
    }

    final key =
        '$inputPath|${stat.size}|${stat.modified.millisecondsSinceEpoch}|'
        '${width}x$height|v1';
    final cached = _cache.get(key);
    if (cached != null && File(cached).existsSync()) {
      _logs.debug('waveform', 'Cache hit for ${p.basename(inputPath)}');
      return Ok(cached);
    }

    try {
      await Directory(_cacheDirectory).create(recursive: true);
    } catch (e) {
      return Err(Failure(FailureKind.io, 'Cannot create waveform cache', cause: e));
    }

    final hash = _stableHash(key);
    final finalPath = p.join(_cacheDirectory, 'wave_${hash}_${width}x$height.png');
    final partPath = '$finalPath.part';

    final outcome = await _runner.run(ProcessRequest(
      label: 'waveform',
      executable: _binaries.binaries.ffmpeg,
      arguments: FfmpegCommandBuilder.waveform(
        input: inputPath,
        output: partPath,
        width: width,
        height: height,
      ),
      timeout: _timeout,
      cancellation: cancellation,
    ));

    if (!outcome.succeeded) {
      await _safeDelete(partPath);
      return Err(Failure(
        cancellation?.isCancelled == true
            ? FailureKind.cancelled
            : FailureKind.processFailed,
        'Waveform generation failed (${outcome.describe()})',
      ));
    }

    try {
      final part = File(partPath);
      if (!await part.exists() || await part.length() == 0) {
        await _safeDelete(partPath);
        return const Err(Failure(FailureKind.io, 'Waveform output is empty'));
      }
      if (await File(finalPath).exists()) await File(finalPath).delete();
      await part.rename(finalPath);
    } catch (e) {
      await _safeDelete(partPath);
      return Err(Failure(FailureKind.io, 'Cannot finalize waveform', cause: e));
    }

    _cache.put(key, finalPath);
    _logs.info('waveform', 'Generated ${p.basename(finalPath)}');
    return Ok(finalPath);
  }

  /// Bounded on-disk cache: only the newest [maxFiles] PNGs are retained.
  Future<int> prune({int maxFiles = 32}) async {
    try {
      final dir = Directory(_cacheDirectory);
      if (!await dir.exists()) return 0;
      final files = await dir
          .list()
          .where((e) => e is File && e.path.endsWith('.png'))
          .cast<File>()
          .toList();
      if (files.length <= maxFiles) return 0;
      final stats = await Future.wait([for (final f in files) f.stat()]);
      final order = List<int>.generate(files.length, (i) => i)
        ..sort((a, b) =>
            stats[a].modified.compareTo(stats[b].modified));
      var removed = 0;
      for (var i = 0; i < files.length - maxFiles; i++) {
        await files[order[i]].delete();
        removed++;
      }
      return removed;
    } catch (_) {
      return 0;
    }
  }

  void invalidateAll() => _cache.clear();

  Future<void> _safeDelete(String path) async {
    try {
      final file = File(path);
      if (await file.exists()) await file.delete();
    } catch (_) {
      // Best effort.
    }
  }
}

/// Deterministic FNV-1a over UTF-8 bytes: stable across processes, so cache
/// files remain valid after an application restart.
int _stableHash(String value) {
  var hash = 0xcbf29ce484222325;
  for (final byte in utf8.encode(value)) {
    hash ^= byte;
    hash = (hash * 0x100000001b3) & 0x7fffffffffffffff;
  }
  return hash;
}
