// ignore_for_file: deprecated_member_use
import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:path/path.dart' as p;

import '../../core/app_version.dart';
import '../../core/log.dart';
import '../../core/outcome.dart';
import '../../domain/encoding.dart';
import '../../domain/media.dart';
import '../../domain/output_plan.dart';
import '../../domain/task.dart';
import '../app_scope.dart';
import '../strings.dart';
import '../theme.dart';
import '../widgets/control_panel.dart';
import '../widgets/log_view.dart';
import '../widgets/media_info_card.dart';
import '../widgets/section_card.dart';
import '../widgets/video_preview.dart';

final class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key, this.initialMedia});

  /// Optional pre-selected source (verification / session restore).
  final MediaInfo? initialMedia;

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  static const _extensions = [
    'mp4', 'avi', 'mkv', 'mov', 'flv', 'wmv', 'webm',
    'mp3', 'wav', 'ogg', 'aac', 'm4a', 'flac',
  ];

  late AppServices _services;
  Stream<bool>? _busyStream;
  String? _path;
  MediaInfo? _media;
  ContainerFormat _container = ContainerFormat.mp4;
  QualityPreset _quality = QualityPreset.balanced;
  OutputResolution _outputResolution = OutputResolution.follow;
  bool _showPreview = false;
  LogLevel? _logFilter;
  double _aspectRatio = 16 / 9;
  ClipRange _range = const ClipRange(startMs: 0, endMs: 1000);

  @override
  void initState() {
    super.initState();
    final initial = widget.initialMedia;
    if (initial != null) {
      _path = initial.path;
      _media = initial;
      _aspectRatio = initial.resolution.height > 0
          ? initial.resolution.width / initial.resolution.height
          : 16 / 9;
      _range = ClipRange(startMs: 0, endMs: initial.duration.inMilliseconds);
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _services = AppScope.of(context);
    // Only running/idle transitions rebuild the player and control panel;
    // progress ticks must not touch the video texture or semantics tree.
    _busyStream ??= _services.tasks.states
        .map((state) => state.isRunning)
        .distinct();
  }

  Future<void> _pickFile() async {
    try {
      final picked = await FilePicker.pickFile(
        type: FileType.custom,
        allowedExtensions: _extensions,
      );
      final path = picked?.path;
      if (path == null) return;
      final probe = await _services.probe.inspect(path);
      if (!mounted) return;
      final s = AppStrings.of(context);
      if (probe.isErr) {
        _services.logs.error('input',
            s.get(StringKey.errorOccurred, [probe.failureOrNull!.message]));
        return;
      }
      final info = probe.valueOrNull!;
      setState(() {
        _path = path;
        _media = info;
        _aspectRatio = info.resolution.height > 0
            ? info.resolution.width / info.resolution.height
            : 16 / 9;
        // A new source invalidates the previous resolution choice; never
        // silently upscale a freshly loaded file.
        _outputResolution = OutputResolution.follow;
        _range = ClipRange(startMs: 0, endMs: info.duration.inMilliseconds);
      });
      _services.logs.info('input',
          s.get(StringKey.selectedFile, ['${info.type.name} $path']));
    } catch (e) {
      _services.logs.error('input',
          AppStrings.of(context).get(StringKey.fileSelectionFailed, ['$e']));
    }
  }

  void _clearFile() {
    if (_services.tasks.isRunning) return;
    setState(() {
      _path = null;
      _media = null;
      _aspectRatio = 16 / 9;
      _outputResolution = OutputResolution.follow;
      _showPreview = false;
    });
  }

  Future<void> _run(TaskKind kind, ClipRange? range) async {
    final path = _path;
    if (path == null) return;
    final effectiveRange = kind == TaskKind.preview ? (range ?? _range) : null;
    if (kind == TaskKind.preview &&
        (effectiveRange == null || !effectiveRange.isValid)) {
      _services.logs.error('task', AppStrings.of(context)
          .get(StringKey.invalidTimeRange));
      return;
    }
    await _services.tasks.run(TaskRequest(
      kind: kind,
      inputPath: path,
      container: _container,
      quality: _quality,
      outputResolution: _outputResolution,
      range: effectiveRange,
      maxConcurrentEncodes: 2,
    ));
  }

  Future<void> _exportLog() async {
    final s = AppStrings.of(context);
    _services.logs.flush();
    try {
      final text = _services.logs.exportText(level: _logFilter);
      final uri = await FilePicker.saveFile(
        fileName: 'media_processor_log.txt',
        bytes: Uint8List.fromList(utf8.encode(text)),
      );
      if (uri != null && mounted) {
        _services.logs.info('log', s.get(StringKey.logExported, ['$uri']));
      }
    } catch (e) {
      _services.logs.error('log', s.get(StringKey.errorOccurred, ['$e']));
    }
  }

  String _statusLabel(TaskStatus status, AppStrings s) => switch (status) {
        TaskStatus.idle => s.get(StringKey.taskStatusIdle),
        TaskStatus.running => s.get(StringKey.taskStatusRunning),
        TaskStatus.completed => s.get(StringKey.taskStatusSuccess),
        TaskStatus.failed => s.get(StringKey.taskStatusFailed),
        TaskStatus.cancelled => s.get(StringKey.taskStatusCanceled),
      };

  Color _statusColor(BuildContext context, TaskStatus status) {
    final scheme = Theme.of(context).colorScheme;
    return switch (status) {
      TaskStatus.idle => scheme.outline,
      TaskStatus.running => scheme.primary,
      TaskStatus.completed => scheme.tertiary,
      TaskStatus.failed => scheme.error,
      TaskStatus.cancelled => scheme.secondary,
    };
  }

  Widget _statusCard(TaskState state) {
    final s = AppStrings.of(context);
    final theme = Theme.of(context);
    final statusColor = _statusColor(context, state.status);
    final result = state.result;
    final bytes = result == null
        ? 0
        : result.outputs.fold<int>(0, (sum, o) => sum + o.bytes);
    return SectionCard(
      title: s.get(StringKey.state),
      icon: Icons.monitor_heart_outlined,
      actions: [
        if (state.isRunning)
          TextButton.icon(
            onPressed: _services.tasks.cancel,
            icon: const Icon(Icons.stop_circle_outlined, size: 16),
            label: Text(s.get(StringKey.cancelAllTasks)),
          ),
      ],
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Icon(
                state.isRunning ? Icons.hourglass_top : Icons.circle,
                size: 18,
                color: statusColor,
              ),
              const SizedBox(width: AppSpacing.m),
              Expanded(
                child: Text(
                  state.isRunning
                      ? s.get(StringKey.processingPleaseWait,
                          ['${state.progress.percent.toStringAsFixed(1)}%'])
                      : _statusLabel(state.status, s),
                  style: theme.textTheme.titleSmall
                      ?.copyWith(color: statusColor, fontWeight: FontWeight.w600),
                ),
              ),
            ],
          ),
          if (state.isRunning) ...[
            const SizedBox(height: AppSpacing.m),
            ClipRRect(
              borderRadius: BorderRadius.circular(8),
              child: LinearProgressIndicator(
                value: state.progress.fraction,
                minHeight: 8,
              ),
            ),
          ],
          if (result?.failure != null) ...[
            const SizedBox(height: AppSpacing.m),
            Text(
              _failureMessage(result!.failure!, s),
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.error),
            ),
          ],
          if (result != null && result.outputs.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.m),
            Text(s.get(StringKey.outputs,
                ['${result.outputs.length}', '$bytes'])),
            const SizedBox(height: AppSpacing.s),
            Text(s.get(StringKey.outputFiles),
                style: theme.textTheme.labelMedium?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant)),
            const SizedBox(height: AppSpacing.xs),
            for (final output in result.outputs)
              SizedBox(
                height: AppSpacing.toolbarHeight,
                child: Row(
                  children: [
                    Expanded(
                      child: SelectableText(
                        output.path,
                        maxLines: 1,
                        style: theme.textTheme.bodySmall,
                      ),
                    ),
                    IconButton(
                      constraints: const BoxConstraints.tightFor(
                          width: AppSpacing.toolbarHeight,
                          height: AppSpacing.toolbarHeight),
                      padding: EdgeInsets.zero,
                      tooltip: s.get(StringKey.openFolder),
                      icon: const Icon(Icons.folder_open, size: 16),
                      onPressed: () =>
                          unawaited(_openOutputFolder(output.path)),
                    ),
                  ],
                ),
              ),
          ],
        ],
      ),
    );
  }

  Widget _fileSelector(AppStrings s, bool busy) => SectionCard(
        title: s.get(StringKey.selectMediaFile),
        icon: Icons.attach_file,
        trailing:
            Text(s.get(StringKey.supportedFormats, [_extensions.join(', ')])),
        child: LayoutBuilder(builder: (context, constraints) {
          final compact = constraints.maxWidth < 640;
          final pathBox = Container(
            padding: const EdgeInsets.symmetric(
                horizontal: AppSpacing.m, vertical: AppSpacing.s),
            decoration: BoxDecoration(
              color: Theme.of(context).colorScheme.surfaceContainerHighest,
              borderRadius: BorderRadius.circular(12),
            ),
            child: Text(
              _path ?? s.get(StringKey.noFileSelected),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: Theme.of(context).textTheme.bodyMedium,
            ),
          );
          final buttons = <Widget>[
            FilledButton.icon(
              onPressed: busy ? null : _pickFile,
              icon: const Icon(Icons.folder_open),
              label: Text(s.get(StringKey.selectMediaFile)),
            ),
            const SizedBox(width: AppSpacing.s),
            OutlinedButton.icon(
              onPressed: (_path == null || busy) ? null : _clearFile,
              icon: const Icon(Icons.clear),
              label: Text(s.get(StringKey.clear)),
            ),
          ];
          return compact
              ? Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    pathBox,
                    const SizedBox(height: AppSpacing.m),
                    Wrap(spacing: AppSpacing.s, children: buttons),
                  ],
                )
              : Row(
                  children: [
                    Expanded(child: pathBox),
                    const SizedBox(width: AppSpacing.m),
                    ...buttons,
                  ],
                );
        }),
      );

  Widget _emptyState(AppStrings s, bool busy) {
    final theme = Theme.of(context);
    return Card(
      key: const Key('empty-state'),
      margin: EdgeInsets.zero,
      child: Padding(
        padding: const EdgeInsets.symmetric(
            vertical: AppSpacing.xl * 2, horizontal: AppSpacing.l),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.add_photo_alternate_outlined,
                  size: 48, color: theme.colorScheme.primary),
              const SizedBox(height: AppSpacing.m),
              Text(s.get(StringKey.emptyStateTitle),
                  style: theme.textTheme.titleMedium),
              const SizedBox(height: AppSpacing.s),
              Text(
                s.get(StringKey.emptyStateHint),
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall?.copyWith(
                    color: theme.colorScheme.onSurfaceVariant),
              ),
              const SizedBox(height: AppSpacing.l),
              FilledButton.icon(
                onPressed: busy ? null : _pickFile,
                icon: const Icon(Icons.folder_open),
                label: Text(s.get(StringKey.selectMediaFile)),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _openOutputFolder(String path) async {
    if (!Platform.isWindows) return;
    try {
      final nativePath = p.normalize(path).replaceAll('/', '\\');
      await Process.run('explorer.exe', ['/select,', nativePath]);
    } catch (e) {
      _services.logs.error('ui', 'Cannot open output folder: $e');
    }
  }

  String _failureMessage(Failure failure, AppStrings s) {
    final key = switch (failure.kind) {
      FailureKind.invalidInput => StringKey.failureInvalidInput,
      FailureKind.binaryUnavailable => StringKey.failureBinaryUnavailable,
      FailureKind.probeFailed => StringKey.failureProbeFailed,
      FailureKind.encoderUnavailable => StringKey.failureEncoderUnavailable,
      FailureKind.processFailed => StringKey.failureProcessFailed,
      FailureKind.timeout => StringKey.failureTimeout,
      FailureKind.cancelled => StringKey.failureCancelled,
      FailureKind.io => StringKey.failureIo,
      FailureKind.unsupported => StringKey.failureUnsupported,
      FailureKind.busy => StringKey.failureBusy,
      FailureKind.internal => StringKey.failureInternal,
    };
    return '${s.get(key)} (${failure.message})';
  }

  void _showAbout(AppStrings s) => showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          icon: Icon(Icons.info_outline,
              color: Theme.of(context).colorScheme.primary),
          title: Text(s.get(StringKey.about)),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(s.get(StringKey.aboutText)),
              const SizedBox(height: AppSpacing.m),
              Text(s.get(StringKey.hostInfo, [_services.host.toString()]),
                  style: Theme.of(context).textTheme.bodySmall),
              const SizedBox(height: AppSpacing.s),
              Text('${s.get(StringKey.version)} $kAppVersion',
                  style: Theme.of(context).textTheme.bodySmall),
              Text(s.get(StringKey.copyright),
                  style: Theme.of(context).textTheme.bodySmall),
              const SizedBox(height: AppSpacing.m),
              Text('Ctrl+O  ·  Ctrl+E  ·  Ctrl+P  ·  Esc',
                  style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      fontFamily: 'Consolas',
                      fontFamilyFallback: const ['monospace'])),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(context).pop(),
              child: Text(s.get(StringKey.ok)),
            ),
          ],
        ),
      );

  Widget _busySection(Widget Function(bool busy) builder) =>
      StreamBuilder<bool>(
        stream: _busyStream,
        initialData: _services.tasks.isRunning,
        builder: (context, snapshot) => builder(snapshot.data ?? false),
      );

  List<Widget> _withGaps(List<Widget> sections) => [
        for (var i = 0; i < sections.length; i++) ...[
          sections[i],
          if (i < sections.length - 1)
            const SizedBox(height: AppSpacing.sectionGap),
        ],
      ];

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final theme = Theme.of(context);
    return Shortcuts(
      shortcuts: const <ShortcutActivator, Intent>{
        SingleActivator(LogicalKeyboardKey.keyO, control: true):
            _OpenFileIntent(),
        SingleActivator(LogicalKeyboardKey.keyE, control: true):
            _ExportLogIntent(),
        SingleActivator(LogicalKeyboardKey.keyP, control: true):
            _TogglePreviewIntent(),
        SingleActivator(LogicalKeyboardKey.escape): _CancelTaskIntent(),
      },
      child: Actions(
        actions: <Type, Action<Intent>>{
          _OpenFileIntent: CallbackAction<_OpenFileIntent>(
            onInvoke: (_) {
              unawaited(_pickFile());
              return null;
            },
          ),
          _ExportLogIntent: CallbackAction<_ExportLogIntent>(
            onInvoke: (_) {
              unawaited(_exportLog());
              return null;
            },
          ),
          _TogglePreviewIntent: CallbackAction<_TogglePreviewIntent>(
            onInvoke: (_) {
              if (_path != null && !_services.tasks.isRunning) {
                setState(() => _showPreview = !_showPreview);
              }
              return null;
            },
          ),
          _CancelTaskIntent: CallbackAction<_CancelTaskIntent>(
            onInvoke: (_) {
              _services.tasks.cancel();
              return null;
            },
          ),
        },
        child: Focus(
          autofocus: true,
          child: Scaffold(
            appBar: AppBar(
              title: Text(s.get(StringKey.appTitle)),
              actions: [
                PopupMenuButton<Locale>(
                  tooltip: s.get(StringKey.language),
                  onSelected: (locale) => _services.locale.value = locale,
                  itemBuilder: (context) => [
                    for (final locale in AppStrings.supportedLocales)
                      PopupMenuItem<Locale>(
                        value: locale,
                        child: Text(
                            locale.languageCode == 'zh' ? '中文' : 'English'),
                      ),
                  ],
                  icon: const Icon(Icons.language),
                ),
                IconButton(
                  visualDensity: VisualDensity.compact,
                  tooltip: s.get(StringKey.about),
                  onPressed: () => _showAbout(s),
                  icon: const Icon(Icons.info_outline),
                ),
              ],
            ),
            body: SafeArea(
              child: LayoutBuilder(builder: (context, constraints) {
                final viewport = constraints.maxWidth;
                final compact = viewport < AppSpacing.compactBreakpoint;
                final wide = viewport >= AppSpacing.twoColumnBreakpoint;
                final pagePadding = compact
                    ? AppSpacing.compactPagePadding
                    : AppSpacing.pagePadding;
                // Fill the viewport instead of parking a narrow column in the
                // middle of a wide window; only cap it on ultra-wide displays.
                final contentWidth =
                    (viewport - pagePadding * 2).clamp(
                        0.0, AppSpacing.maxContentWidth).toDouble();
                final logHeight = wide
                    ? AppSpacing.wideLogHeight
                    : compact
                        ? AppSpacing.compactLogHeight
                        : AppSpacing.regularLogHeight;

                final media = _media;
                final profile = ProfileRegistry.of(_container);
                final plan = media == null
                    ? null
                    : OutputPlan.build(media, _outputResolution);
                final insights = media == null || plan == null
                    ? const <Insight>[]
                    : buildInsights(media, plan, profile);

                final targetSize = plan == null || !plan.hasVideo
                    ? '-'
                    : '${plan.target.width}×${plan.target.height}';

                final primary = <Widget>[
                  _busySection((busy) => _fileSelector(s, busy)),
                  if (media == null)
                    _busySection((busy) => _emptyState(s, busy))
                  else ...[
                    MediaInfoCard(media: media),
                    _busySection(
                      (busy) => ControlPanel(
                        isProcessing: busy,
                        isAudioOnly: !media.hasVideo,
                        container: _container,
                        quality: _quality,
                        outputResolution: _outputResolution,
                        targetSize: targetSize,
                        insights: insights,
                        showPreview: _showPreview,
                        onContainerChanged: (value) =>
                            setState(() => _container = value),
                        onQualityChanged: (value) =>
                            setState(() => _quality = value),
                        onOutputResolutionChanged: (value) =>
                            setState(() => _outputResolution = value),
                        onGenerateFull: busy
                            ? null
                            : () => _run(TaskKind.full, null),
                        onTogglePreview: busy
                            ? null
                            : () =>
                                setState(() => _showPreview = !_showPreview),
                      ),
                    ),
                    if (_showPreview)
                      _busySection(
                        (busy) => VideoPreviewPanel(
                          filePath: media.path,
                          isVideo: media.hasVideo,
                          waveforms: _services.waveforms,
                          enabled: !busy,
                          aspectRatio: _aspectRatio,
                          onRangeChanged: (range) => _range = range,
                          onGeneratePreview: (range) =>
                              _run(TaskKind.preview, range),
                        ),
                      ),
                  ],
                ];
                final secondary = <Widget>[
                  StreamBuilder<TaskState>(
                    stream: _services.tasks.states,
                    initialData: _services.tasks.state,
                    builder: (context, snapshot) =>
                        _statusCard(snapshot.data ?? _services.tasks.state),
                  ),
                  SizedBox(
                    key: const Key('log-panel'),
                    height: logHeight,
                    child: LogView(
                      store: _services.logs,
                      filter: _logFilter,
                      onFilterChanged: (level) =>
                          setState(() => _logFilter = level),
                      onExport: _exportLog,
                    ),
                  ),
                ];

                final Widget sections = wide
                    ? Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Expanded(
                            flex: AppSpacing.primaryColumnFlex,
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: _withGaps(primary),
                            ),
                          ),
                          const SizedBox(width: AppSpacing.sectionGap),
                          Expanded(
                            flex: AppSpacing.secondaryColumnFlex,
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.stretch,
                              children: _withGaps(secondary),
                            ),
                          ),
                        ],
                      )
                    : Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: _withGaps([...primary, ...secondary]),
                      );

                return Center(
                  child: ConstrainedBox(
                    key: const Key('content-root'),
                    constraints: BoxConstraints(maxWidth: contentWidth),
                    child: SingleChildScrollView(
                      padding: EdgeInsets.all(pagePadding),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.stretch,
                        children: [
                          Text(
                            s.get(StringKey.appSubtitle),
                            style: theme.textTheme.bodySmall?.copyWith(
                                color: theme.colorScheme.onSurfaceVariant),
                          ),
                          const SizedBox(height: AppSpacing.m),
                          sections,
                        ],
                      ),
                    ),
                  ),
                );
              }),
            ),
          ),
        ),
      ),
    );
  }
}

final class _OpenFileIntent extends Intent {
  const _OpenFileIntent();
}

final class _ExportLogIntent extends Intent {
  const _ExportLogIntent();
}

final class _TogglePreviewIntent extends Intent {
  const _TogglePreviewIntent();
}

final class _CancelTaskIntent extends Intent {
  const _CancelTaskIntent();
}
