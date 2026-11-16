// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

import '../../core/log.dart';
import '../strings.dart';
import '../theme.dart';
import 'section_card.dart';

/// Structured log viewer. Rebuilds once per batched revision; the level
/// filter is owned by the parent so log export uses exactly the same view.
final class LogView extends StatefulWidget {
  const LogView({
    super.key,
    required this.store,
    required this.filter,
    required this.onFilterChanged,
    required this.onExport,
  });

  final LogStore store;
  final LogLevel? filter;
  final ValueChanged<LogLevel?> onFilterChanged;
  final Future<void> Function() onExport;

  @override
  State<LogView> createState() => _LogViewState();
}

class _LogViewState extends State<LogView> {
  final ScrollController _scroll = ScrollController();
  int _lastScrolledRevision = -1;

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  Color _color(LogLevel level, ColorScheme scheme) => switch (level) {
        LogLevel.debug => scheme.onSurfaceVariant,
        LogLevel.info => scheme.onSurface,
        LogLevel.warning => scheme.tertiary,
        LogLevel.error => scheme.error,
      };

  String _levelLabel(LogLevel? level, AppStrings s) => switch (level) {
        null => s.get(StringKey.logLevelAll),
        LogLevel.debug => s.get(StringKey.logLevelDebug),
        LogLevel.info => s.get(StringKey.logLevelInfo),
        LogLevel.warning => s.get(StringKey.logLevelWarning),
        LogLevel.error => s.get(StringKey.logLevelError),
      };

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final scheme = Theme.of(context).colorScheme;

    return SectionCard(
      title: s.get(StringKey.executionLog),
      icon: Icons.terminal,
      expandChild: true,
      actions: [
        // A compact popup chip, not a form field: it shares the 36dp toolbar
        // height with the neighbouring icon buttons.
        PopupMenuButton<LogLevel?>(
          key: const Key('log-filter-menu'),
          tooltip: s.get(StringKey.logLevelAll),
          initialValue: widget.filter,
          onSelected: widget.onFilterChanged,
          itemBuilder: (context) => [
            for (final level in <LogLevel?>[null, ...LogLevel.values])
              PopupMenuItem<LogLevel?>(
                value: level,
                child: Text(_levelLabel(level, s)),
              ),
          ],
          child: Container(
            height: AppSpacing.toolbarHeight,
            padding: const EdgeInsets.symmetric(horizontal: AppSpacing.m),
            decoration: BoxDecoration(
              border: Border.all(color: scheme.outlineVariant),
              borderRadius: BorderRadius.circular(8),
            ),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_levelLabel(widget.filter, s),
                    style: Theme.of(context).textTheme.labelMedium),
                const SizedBox(width: AppSpacing.xs),
                const Icon(Icons.arrow_drop_down, size: 18),
              ],
            ),
          ),
        ),
        const SizedBox(width: AppSpacing.xs),
        IconButton(
          constraints: const BoxConstraints.tightFor(
              width: AppSpacing.toolbarHeight,
              height: AppSpacing.toolbarHeight),
          padding: EdgeInsets.zero,
          tooltip: s.get(StringKey.exportLog),
          onPressed: widget.onExport,
          icon: const Icon(Icons.download, size: 18),
        ),
        const SizedBox(width: AppSpacing.xs),
        IconButton(
          constraints: const BoxConstraints.tightFor(
              width: AppSpacing.toolbarHeight,
              height: AppSpacing.toolbarHeight),
          padding: EdgeInsets.zero,
          tooltip: s.get(StringKey.clearLogs),
          onPressed: widget.store.clear,
          icon: const Icon(Icons.delete_outline, size: 18),
        ),
      ],
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: scheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(12),
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(12),
          child: StreamBuilder<int>(
            stream: widget.store.revisions,
            initialData: widget.store.revision,
            builder: (context, snapshot) {
              final indices = <int>[];
              for (var i = 0; i < widget.store.length; i++) {
                if (widget.filter == null ||
                    widget.store[i].level == widget.filter) {
                  indices.add(i);
                }
              }
              final revision = snapshot.data ?? widget.store.revision;
              if (revision != _lastScrolledRevision) {
                _lastScrolledRevision = revision;
                SchedulerBinding.instance.addPostFrameCallback((_) {
                  if (_scroll.hasClients) {
                    _scroll.jumpTo(_scroll.position.maxScrollExtent);
                  }
                });
              }
              return ListView.builder(
                controller: _scroll,
                // Dense log rows do not need per-item semantic indexes;
                // omitting them avoids Windows AXTree update churn.
                addSemanticIndexes: false,
                padding: const EdgeInsets.all(AppSpacing.m),
                itemCount: indices.length,
                itemBuilder: (context, index) {
                  final event = widget.store[indices[index]];
                  return Text(
                    event.format(),
                    style: TextStyle(
                      color: _color(event.level, scheme),
                      fontFamily: 'Consolas',
                      fontFamilyFallback: const ['monospace'],
                      fontSize: 12,
                      height: 1.4,
                    ),
                  );
                },
              );
            },
          ),
        ),
      ),
    );
  }
}
