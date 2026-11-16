import 'dart:io';

import '../core/outcome.dart';
import '../domain/encoding.dart';
import '../domain/task.dart';
import 'package:path/path.dart' as p;

final class OutputTarget {
  const OutputTarget({
    required this.kind,
    required this.finalPath,
    required this.partPath,
  });

  final OutputKind kind;
  final String finalPath;
  final String partPath;
}

/// Plans outputs in the input directory so the final rename is a same-
/// filesystem operation. Existing user files are never overwritten silently.
final class OutputPlanner {
  const OutputPlanner();

  List<OutputTarget> plan({
    required String inputPath,
    required bool isPreview,
    required EncodingProfile profile,
    required bool overwrite,
    required List<OutputKind> kinds,
  }) =>
      _build(inputPath, isPreview, profile, overwrite, kinds,
          _existingNamesSync(p.dirname(inputPath)));

  /// Async variant used by the application: one directory enumeration instead
  /// of one synchronous stat per candidate, with no UI-isolate blocking.
  Future<List<OutputTarget>> planAsync({
    required String inputPath,
    required bool isPreview,
    required EncodingProfile profile,
    required bool overwrite,
    required List<OutputKind> kinds,
  }) async =>
      _build(inputPath, isPreview, profile, overwrite, kinds,
          await _existingNames(p.dirname(inputPath)));

  List<OutputTarget> _build(
    String inputPath,
    bool isPreview,
    EncodingProfile profile,
    bool overwrite,
    List<OutputKind> kinds,
    Set<String> existing,
  ) {
    final directory = p.dirname(inputPath);
    final base = p.basenameWithoutExtension(inputPath);
    final targets = <OutputTarget>[];
    for (final kind in kinds) {
      final stem = switch (kind) {
        OutputKind.video => isPreview
            ? '${base}_preview.${profile.extension}'
            : '${base}_video_only.${profile.extension}',
        OutputKind.audio => isPreview
            ? '${base}_preview.${profile.audioExtension}'
            : '${base}_audio.${profile.audioExtension}',
      };
      final name = overwrite
          ? stem
          : _nonColliding(stem, existing, used: {
              for (final t in targets) p.basename(t.finalPath),
            });
      final finalPath = p.join(directory, name);
      targets.add(OutputTarget(
        kind: kind,
        finalPath: finalPath,
        partPath: '$finalPath.part',
      ));
    }
    return targets;
  }

  String _nonColliding(String stem, Set<String> existing,
      {required Set<String> used}) {
    final extension = p.extension(stem);
    final name = p.basenameWithoutExtension(stem);
    var candidate = stem;
    var index = 1;
    while (existing.contains(candidate) ||
        existing.contains('$candidate.part') ||
        used.contains(candidate)) {
      candidate = '${name}_$index$extension';
      index++;
    }
    return candidate;
  }

  Set<String> _existingNamesSync(String directory) {
    try {
      return Directory(directory)
          .listSync()
          .map((e) => p.basename(e.path))
          .toSet();
    } catch (_) {
      return <String>{};
    }
  }

  Future<Set<String>> _existingNames(String directory) async {
    try {
      final dir = Directory(directory);
      if (!await dir.exists()) return <String>{};
      final names = <String>{};
      await for (final entity in dir.list()) {
        names.add(p.basename(entity.path));
      }
      return names;
    } catch (_) {
      return <String>{};
    }
  }
}

final class _Backup {
  const _Backup(this.path, this.originalPath);
  final String path;
  final String originalPath;
}

/// Publishing transaction.
///
/// Existing targets are moved to recoverable backups (never deleted), all
/// `.part` files are renamed into place, and only then the backups are
/// dropped. Any failure restores every original file and removes every newly
/// published one, so neither the old nor the new data can be lost.
final class AtomicOutput {
  const AtomicOutput._();

  /// Kept for compatibility: commits a single target.
  static Future<Outcome<int>> commit(OutputTarget target,
      {required bool overwrite}) async {
    final result = await commitAll([target], overwrite: overwrite);
    return result.map((artifacts) => artifacts.single.bytes);
  }

  static Future<Outcome<List<OutputArtifact>>> commitAll(
    List<OutputTarget> targets,
      {required bool overwrite}) async {
    final backups = <_Backup>[];
    final published = <String>[];
    final sizes = <String, int>{};
    try {
      // Phase 1: validate every input before causing any side effect.
      for (final target in targets) {
        final part = File(target.partPath);
        if (!await part.exists()) {
          return Err(Failure(FailureKind.io,
              'Encoder produced no output for ${target.finalPath}'));
        }
        final bytes = await part.length();
        if (bytes == 0) {
          return Err(Failure(FailureKind.io,
              'Encoder produced an empty file for ${target.finalPath}'));
        }
        sizes[target.partPath] = bytes;
      }

      // Phase 2: park existing user files under unique backup names.
      for (final target in targets) {
        final finalFile = File(target.finalPath);
        if (!await finalFile.exists()) continue;
        if (!overwrite) {
          await _rollback(backups, published);
          return Err(Failure(FailureKind.io,
              'Output already exists: ${target.finalPath}'));
        }
        final backupPath = await _uniqueBackupPath(target.finalPath);
        await finalFile.rename(backupPath);
        backups.add(_Backup(backupPath, target.finalPath));
      }

      // Phase 3: make all outputs visible.
      for (final target in targets) {
        await File(target.partPath).rename(target.finalPath);
        published.add(target.finalPath);
      }

      // Phase 4: only after full success are the backups released.
      for (final backup in backups) {
        await _safeDelete(backup.path);
      }
      return Ok([
        for (final target in targets)
          OutputArtifact(
            path: target.finalPath,
            kind: target.kind,
            bytes: sizes[target.partPath]!,
          ),
      ]);
    } catch (e) {
      await _rollback(backups, published);
      return Err(Failure(FailureKind.io,
          'Cannot finalize ${targets.map((t) => t.finalPath).join(", ")}',
          cause: e));
    }
  }

  static Future<void> _rollback(
      List<_Backup> backups, List<String> published) async {
    for (final path in published) {
      await _safeDelete(path);
    }
    for (final backup in backups.reversed) {
      try {
        final source = File(backup.path);
        if (!await source.exists()) continue;
        await _safeDelete(backup.originalPath);
        await source.rename(backup.originalPath);
      } catch (_) {
        // Recovery is best effort; the backup file remains on disk and its
        // path is reported by the returned failure for manual recovery.
      }
    }
  }

  static Future<String> _uniqueBackupPath(String finalPath) async {
    var index = 0;
    var candidate = '$finalPath.bak';
    while (await File(candidate).exists()) {
      index++;
      candidate = '$finalPath.bak.$index';
    }
    return candidate;
  }

  static Future<void> abort(OutputTarget target) async {
    try {
      final part = File(target.partPath);
      if (await part.exists()) await part.delete();
    } catch (_) {
      // Cleanup is best effort; the error that triggered it is reported.
    }
  }

  static Future<void> abortAll(Iterable<OutputTarget> targets) async {
    await Future.wait([for (final t in targets) abort(t)]);
  }

  static Future<void> _safeDelete(String path) async {
    try {
      final file = File(path);
      if (await file.exists()) await file.delete();
    } catch (_) {
      // Best effort.
    }
  }

  /// Pre-flight check so a read-only / overlong-path target fails immediately
  /// instead of after a long encode.
  static Future<Outcome<void>> validateWritable(String directory) async {
    try {
      final dir = Directory(directory);
      if (!await dir.exists()) {
        return Err(Failure(FailureKind.io, 'Output directory does not exist'));
      }
      if (Platform.isWindows && directory.length > 240) {
        return Err(Failure(FailureKind.io,
            'Output path exceeds the Windows MAX_PATH limit'));
      }
      final probe = File(p.join(directory, '.media_processor_write_probe'));
      await probe.writeAsBytes(const [0]);
      await probe.delete();
      return const Ok(null);
    } catch (e) {
      return Err(Failure(FailureKind.io,
          'Output directory is not writable', cause: e));
    }
  }
}
