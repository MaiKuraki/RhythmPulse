import 'dart:convert';
import 'dart:io';

/// Cross-round comparison of all verification reports.
/// Usage: dart run tool/summarize.dart [report-directory]
void main(List<String> arguments) {
  final directory = Directory(
      arguments.isEmpty ? 'verification/reports' : arguments.first);
  if (!directory.existsSync()) {
    stderr.writeln('No reports in ${directory.path}');
    exit(1);
  }
  final reports = <Map<String, dynamic>>[];
  for (final file in directory.listSync().whereType<File>()) {
    if (!file.path.endsWith('.json')) continue;
    reports.add(json.decode(file.readAsStringSync()) as Map<String, dynamic>);
  }
  reports.sort((a, b) => '${a['round']}/${a['suite']}'
      .compareTo('${b['round']}/${b['suite']}'));

  final rounds = reports.map((r) => r['round'].toString()).toSet().toList()
    ..sort();
  final suites = <String>{
    'command_matrix',
    'fuzz',
    'soak',
    'real_ffmpeg',
    'real_matrix',
    'process_runner',
  };

  stdout.writeln('=== Verification cross-round comparison ===');
  stdout.writeln('reports=${reports.length} rounds=${rounds.join(",")}');
  stdout.writeln('');
  for (final suite in suites) {
    stdout.writeln('--- $suite ---');
    stdout.writeln('round'.padRight(12) +
        'seed'.padRight(10) +
        'scenario'.padRight(20) +
        metricsFor(suite));
    for (final report in reports.where((r) => r['suite'] == suite)) {
      stdout.writeln(
          report['round'].toString().padRight(12) +
          report['seed'].toString().padRight(10) +
          '${report['scenario_os']}/${report['scenario_arch']}'
              .padRight(20) +
          valuesFor(suite, report));
    }
    stdout.writeln('');
  }

  final summary = {
    'generated': DateTime.now().toIso8601String(),
    'rounds': rounds,
    'reports': reports,
  };
  final output = File('${directory.path}/summary.json');
  output.writeAsStringSync(
      const JsonEncoder.withIndent('  ').convert(summary));
  stdout.writeln('Wrote ${output.path}');
}

String metricsFor(String suite) => switch (suite) {
      'command_matrix' => 'commands  invariants  hosts',
      'fuzz' => 'cases     invariants',
      'soak' => 'ms        tasks      ops/s     hit_rate',
      'real_ffmpeg' => 'outputs   bytes      stages',
      'process_runner' => 'peak      queued     stderr',
      _ => 'containers tasks      bytes',
    };

String valuesFor(String suite, Map<String, dynamic> r) {
  String v(String key) => (r[key] ?? '-').toString();
  return switch (suite) {
    'command_matrix' =>
      '${v('commands').padRight(10)}${v('invariants').padRight(12)}${v('hosts')}',
    'fuzz' => '${v('cases').padRight(10)}${v('invariants')}',
    'soak' =>
      '${v('duration_ms').padRight(10)}${v('tasks').padRight(11)}'
          '${v('ops_per_sec').padRight(10)}${v('probe_hit_rate')}',
    'real_ffmpeg' =>
      '${v('outputs').padRight(10)}${v('bytes').padRight(11)}${v('stages')}',
    'process_runner' =>
      '${v('peak').padRight(10)}${v('queued').padRight(11)}${v('stderr_lines')}',
    _ => '${v('containers').padRight(10)}${v('tasks').padRight(11)}'
        '${v('bytes')}',
  };
}
