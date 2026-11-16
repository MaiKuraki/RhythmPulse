import 'dart:convert';
import 'dart:io';

/// Writes one machine-readable report per suite per verification round.
/// Reports are stable enough to be diffed across rounds, seeds, depths and
/// hardware scenarios by `dart run tool/summarize.dart`.
String get verificationRound =>
    Platform.environment['VERIFICATION_ROUND'] ?? 'round-0';

String get verificationDirectory =>
    Platform.environment['VERIFICATION_DIR'] ?? 'verification/reports';

void recordMetric(String suite, Map<String, Object?> data) {
  final directory = Directory(verificationDirectory)
    ..createSync(recursive: true);
  final payload = <String, Object?>{
    'round': verificationRound,
    'suite': suite,
    'timestamp': DateTime.now().toIso8601String(),
    'seed': Platform.environment['TEST_SEED'] ?? '1',
    'depth': Platform.environment['COMBO_DEPTH'] ?? '2',
    'fuzz_cases': Platform.environment['FUZZ_CASES'] ?? '',
    'soak_ms': Platform.environment['SOAK_MS'] ?? '',
    'real_ffmpeg': Platform.environment['RUN_REAL_FFMPEG'] ?? 'auto',
    'scenario_os': Platform.environment['MEDIA_PROCESSOR_OS'] ?? 'native',
    'scenario_arch': Platform.environment['MEDIA_PROCESSOR_ARCH'] ?? 'native',
    'scenario_cores': Platform.environment['MEDIA_PROCESSOR_CORES'] ?? 'native',
    'host_os': Platform.operatingSystem,
    'host_cores': Platform.numberOfProcessors,
    'dart': Platform.version,
    ...data,
  };
  File('${directory.path}/${verificationRound}_$suite.json')
      .writeAsStringSync(const JsonEncoder.withIndent('  ').convert(payload));
  print('[METRIC] round=${payload['round']} suite=$suite '
      '${data.entries.map((e) => '${e.key}=${e.value}').join(' ')}');
}
