import 'dart:async';
import 'dart:io';
import 'dart:ui'
    show AppExitResponse, AppLifecycleState, Locale, PlatformDispatcher;

import 'package:flutter/widgets.dart';
import 'package:media_kit/media_kit.dart';
import 'package:path/path.dart' as p;

import 'core/log.dart';
import 'platform/host.dart';
import 'services/binaries.dart';
import 'services/media_processor.dart';
import 'services/process_runner.dart';
import 'services/probe.dart';
import 'services/waveform.dart';
import 'state/task_controller.dart';
import 'ui/app.dart';
import 'ui/app_scope.dart';
import 'ui/strings.dart';

/// Graceful shutdown on both window-close and process signals. The native
/// runner additionally binds all children to a Windows Job Object, so even a
/// hard kill cannot leave orphaned ffmpeg processes.
final class _ShutdownHandler extends WidgetsBindingObserver {
  _ShutdownHandler(this._services);

  final AppServices _services;
  bool _done = false;

  Future<void> cleanup() async {
    if (_done) return;
    _done = true;
    await _services.dispose();
  }

  @override
  Future<AppExitResponse> didRequestAppExit() async {
    await cleanup();
    return AppExitResponse.exit;
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.detached) unawaited(cleanup());
  }
}

Future<void> main() async {
  final binding = WidgetsFlutterBinding.ensureInitialized();
  MediaKit.ensureInitialized();

  final host = detectHostCapabilities();
  final logs = LogStore(scheduler: const SystemFlushScheduler());
  final group = ProcessGroup();
  final maxProcesses =
      int.tryParse(Platform.environment['MEDIA_PROCESSOR_MAX_PROCESSES'] ?? '') ?? 4;
  final runner = SystemProcessRunner(
      group: group, maxConcurrentProcesses: maxProcesses.clamp(1, 16).toInt());
  final executableDirectory = p.dirname(Platform.resolvedExecutable);
  final binaries = ExeRelativeBinaryLocator(
    executableDirectory: executableDirectory,
    os: host.os,
    overrideDirectory: Platform.environment['MEDIA_PROCESSOR_FFMPEG_DIR'],
  );
  final probe = MediaProbeService(runner: runner, binaries: binaries);
  final processor = MediaProcessor(
    runner: runner,
    binaries: binaries,
    host: host,
    logs: logs,
    probe: probe,
    group: group,
  );
  final tasks = TaskController(processor: processor, logs: logs);
  final waveforms = WaveformService(
    runner: runner,
    binaries: binaries,
    logs: logs,
    cacheDirectory: p.join(
        Directory.systemTemp.path, 'media_processor', 'waveforms'),
  );
  final services = AppServices(
    logs: logs,
    processor: processor,
    probe: probe,
    tasks: tasks,
    waveforms: waveforms,
    host: host,
    locale: ValueNotifier<Locale>(
        AppStrings.normalize(PlatformDispatcher.instance.locale)),
  );

  final shutdown = _ShutdownHandler(services);
  binding.addObserver(shutdown);
  ProcessSignal.sigint.watch().listen((_) async {
    await shutdown.cleanup();
    exit(0);
  });

  final jobObject =
      Platform.environment['MEDIA_PROCESSOR_JOB_OBJECT'] ?? 'unavailable';
  // The native runner publishes this before creating any window; Dart uses the
  // same value so the app-level ExcludeSemantics matches the WM_GETOBJECT
  // filter exactly.
  final semanticsState =
      Platform.environment['MEDIA_PROCESSOR_SEMANTICS_STATE'] ?? 'active';
  final disableSemantics = semanticsState == 'blocked';
  logs.info('startup',
      'Detected $host; binaries at ${binaries.directory}; '
      'job_object=$jobObject; max_processes=$maxProcesses; '
      'semantics=$semanticsState');
  unawaited(waveforms.prune());
  runApp(MediaProcessorApp(
    services: services,
    disableSemantics: disableSemantics,
  ));
}
