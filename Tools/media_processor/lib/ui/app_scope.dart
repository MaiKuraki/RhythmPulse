import 'package:flutter/widgets.dart';

import '../core/log.dart';
import '../platform/host.dart';
import '../services/media_processor.dart';
import '../services/probe.dart';
import '../services/waveform.dart';
import '../state/task_controller.dart';

/// Composition root. All dependencies are injected once; no globals, no
/// service locator, and no UI file needs to import the entry point.
final class AppServices {
  AppServices({
    required this.logs,
    required this.processor,
    required this.probe,
    required this.tasks,
    required this.waveforms,
    required this.host,
    required this.locale,
  });

  final LogStore logs;
  final MediaProcessor processor;
  final MediaProbeService probe;
  final TaskController tasks;
  final WaveformService waveforms;
  final HostCapabilities host;
  final ValueNotifier<Locale> locale;

  Future<void> dispose() async {
    await tasks.dispose();
    await processor.dispose();
    await logs.dispose();
    locale.dispose();
  }
}

final class AppScope extends InheritedWidget {
  const AppScope({super.key, required this.services, required super.child});

  final AppServices services;

  static AppServices of(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AppScope>()!.services;

  @override
  bool updateShouldNotify(covariant AppScope oldWidget) => false;
}
