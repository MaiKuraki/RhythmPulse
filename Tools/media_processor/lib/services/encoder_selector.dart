import '../core/log.dart';
import '../core/outcome.dart';
import '../domain/encoding.dart';
import '../platform/encoders.dart';
import '../platform/host.dart';
import 'binaries.dart';
import 'command_builder.dart';
import 'process_runner.dart';

/// Resolves the ordered encoder fallback chain.
///
/// A single `ffmpeg -encoders` call (cached with a TTL) replaces three
/// real-encoding probes. Runtime failures still fall back through the chain
/// in [MediaProcessor], so a present-but-nonfunctional driver is handled.
final class EncoderSelector {
  EncoderSelector({
    required ProcessRunner runner,
    required BinaryLocator binaries,
    required LogStore logs,
    Duration ttl = const Duration(minutes: 10),
  })  : _runner = runner,
        _binaries = binaries,
        _logs = logs,
        _ttl = ttl;

  final ProcessRunner _runner;
  final BinaryLocator _binaries;
  final LogStore _logs;
  final Duration _ttl;

  Set<String>? _available;
  DateTime? _fetchedAt;

  int get cachedEncoderCount => _available?.length ?? 0;

  void invalidate() {
    _available = null;
    _fetchedAt = null;
  }

  Future<Outcome<Set<String>>> availableEncoders(
      {bool forceRefresh = false}) async {
    if (!forceRefresh && _available != null && _fetchedAt != null) {
      if (DateTime.now().difference(_fetchedAt!) < _ttl) {
        return Ok(_available!);
      }
    }
    final result = await _runner.run(ProcessRequest(
      label: 'encoder-list',
      executable: _binaries.binaries.ffmpeg,
      arguments: FfmpegCommandBuilder.encoderListArgs,
      timeout: const Duration(seconds: 15),
      captureStdout: true,
      maxCapturedLines: 4096,
    ));
    if (!result.succeeded) {
      _logs.warning('encoders',
          'Encoder enumeration failed (${result.describe()}); using software path only');
      _available = <String>{};
      _fetchedAt = DateTime.now();
      return Ok(_available!);
    }

    final knownIds = <String>{for (final e in EncoderCatalog.all) e.id};
    final found = <String>{};
    for (final line in result.stdoutLines) {
      for (final token in line.split(RegExp(r'\s+'))) {
        if (knownIds.contains(token)) found.add(token);
      }
    }
    _available = found;
    _fetchedAt = DateTime.now();
    _logs.debug('encoders', 'Available encoders: ${found.join(", ")}');
    return Ok(found);
  }

  Future<Outcome<List<EncoderDescriptor>>> plan(
    VideoCodec codec,
    HostCapabilities host, {
    bool forceRefresh = false,
  }) async {
    final availability = await availableEncoders(forceRefresh: forceRefresh);
    if (availability.isErr) return Err(availability.failureOrNull!);
    final available = availability.valueOrNull!;
    final candidates = EncoderCatalog.candidates(codec, host)
        .where((e) => !e.isHardware || available.contains(e.id))
        .toList(growable: false);
    if (candidates.isEmpty) {
      return const Err(Failure(FailureKind.encoderUnavailable,
          'No usable encoder for the requested codec on this host'));
    }
    return Ok(candidates);
  }
}
