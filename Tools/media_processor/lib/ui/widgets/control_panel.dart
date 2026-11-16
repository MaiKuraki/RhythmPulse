// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';

import '../../domain/encoding.dart';
import '../../domain/output_plan.dart';
import '../strings.dart';
import '../theme.dart';
import 'section_card.dart';

/// Presentation-only settings panel. Video-only controls disappear for audio
/// inputs, and the resolution policy replaces the old boolean 4K switch.
final class ControlPanel extends StatelessWidget {
  const ControlPanel({
    super.key,
    required this.isProcessing,
    required this.isAudioOnly,
    required this.container,
    required this.quality,
    required this.outputResolution,
    required this.targetSize,
    required this.insights,
    required this.onContainerChanged,
    required this.onQualityChanged,
    required this.onOutputResolutionChanged,
    required this.showPreview,
    this.onGenerateFull,
    this.onTogglePreview,
  });

  final bool isProcessing;
  final bool isAudioOnly;
  final ContainerFormat container;
  final QualityPreset quality;
  final OutputResolution outputResolution;
  final String targetSize;
  final List<Insight> insights;
  final ValueChanged<ContainerFormat> onContainerChanged;
  final ValueChanged<QualityPreset> onQualityChanged;
  final ValueChanged<OutputResolution> onOutputResolutionChanged;
  final bool showPreview;
  final VoidCallback? onGenerateFull;
  final VoidCallback? onTogglePreview;

  String _qualityLabel(QualityPreset preset, AppStrings s) => switch (preset) {
        QualityPreset.speed => s.get(StringKey.qualitySpeed),
        QualityPreset.balanced => s.get(StringKey.qualityBalanced),
        QualityPreset.quality => s.get(StringKey.qualityQuality),
      };

  String _resolutionLabel(OutputResolution mode, AppStrings s) =>
      mode.isFollow ? s.get(StringKey.resolutionFollowSource) : mode.label;

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final theme = Theme.of(context);
    final disabled = isProcessing;

    return SectionCard(
      title: s.get(StringKey.configuration),
      icon: Icons.tune,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          LayoutBuilder(builder: (context, constraints) {
            final fields = <Widget>[
              if (!isAudioOnly)
                LabeledField(
                  label: s.get(StringKey.videoFormat),
                  child: SizedBox(
                    height: AppSpacing.controlHeight,
                    child: Align(
                      alignment: Alignment.centerLeft,
                      child: SegmentedButton<ContainerFormat>(
                        segments: [
                          for (final format in ProfileRegistry.supported)
                            ButtonSegment<ContainerFormat>(
                              value: format,
                              label: Text(format.name.toUpperCase()),
                            ),
                        ],
                        selected: {container},
                        showSelectedIcon: false,
                        onSelectionChanged: disabled
                            ? null
                            : (selection) =>
                                onContainerChanged(selection.first),
                      ),
                    ),
                  ),
                ),
              LabeledField(
                label: s.get(isAudioOnly
                    ? StringKey.audioQuality
                    : StringKey.quality),
                child: SizedBox(
                  height: AppSpacing.controlHeight,
                  child: Align(
                    alignment: Alignment.centerLeft,
                    child: DropdownMenu<QualityPreset>(
                      key: const Key('quality-menu'),
                      enabled: !disabled,
                      width: 200,
                      initialSelection: quality,
                      dropdownMenuEntries: [
                        for (final preset in QualityPreset.values)
                          DropdownMenuEntry<QualityPreset>(
                            value: preset,
                            label: _qualityLabel(preset, s),
                          ),
                      ],
                      onSelected: disabled
                          ? null
                          : (value) => onQualityChanged(value ?? quality),
                    ),
                  ),
                ),
              ),
              if (!isAudioOnly)
                LabeledField(
                  label: s.get(StringKey.outputResolution),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      SizedBox(
                        height: AppSpacing.controlHeight,
                        child: Align(
                          alignment: Alignment.centerLeft,
                          child: DropdownMenu<OutputResolution>(
                            key: const Key('output-resolution-menu'),
                            enabled: !disabled,
                            width: 240,
                            initialSelection: outputResolution,
                            dropdownMenuEntries: [
                              for (final mode in OutputResolution.values)
                                DropdownMenuEntry<OutputResolution>(
                                  value: mode,
                                  label: _resolutionLabel(mode, s),
                                ),
                            ],
                            onSelected: disabled
                                ? null
                                : (value) => onOutputResolutionChanged(
                                    value ?? outputResolution),
                          ),
                        ),
                      ),
                      const SizedBox(height: AppSpacing.xs),
                      Text(
                        '${s.get(StringKey.targetSize)}  $targetSize',
                        style: theme.textTheme.labelSmall?.copyWith(
                            color: theme.colorScheme.onSurfaceVariant),
                      ),
                    ],
                  ),
                ),
            ];
            if (constraints.maxWidth < 720) {
              return Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (var i = 0; i < fields.length; i++) ...[
                    fields[i],
                    if (i < fields.length - 1)
                      const SizedBox(height: AppSpacing.m),
                  ],
                ],
              );
            }
            return Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (var i = 0; i < fields.length; i++) ...[
                  Expanded(child: fields[i]),
                  if (i < fields.length - 1)
                    const SizedBox(width: AppSpacing.l),
                ],
              ],
            );
          }),
          if (insights.isNotEmpty) ...[
            const SizedBox(height: AppSpacing.l),
            Text(s.get(StringKey.outputHints), style: theme.textTheme.titleSmall),
            const SizedBox(height: AppSpacing.s),
            for (final insight in insights)
              Padding(
                padding: const EdgeInsets.only(bottom: AppSpacing.s),
                child: _InsightTile(insight: insight, strings: s),
              ),
          ],
          const SizedBox(height: AppSpacing.l),
          Divider(height: 1, color: theme.colorScheme.outlineVariant),
          const SizedBox(height: AppSpacing.m),
          Text(s.get(StringKey.actions), style: theme.textTheme.titleSmall),
          const SizedBox(height: AppSpacing.s),
          Wrap(
            spacing: AppSpacing.m,
            runSpacing: AppSpacing.s,
            children: [
              FilledButton.icon(
                onPressed: onGenerateFull,
                icon: const Icon(Icons.movie_creation_outlined),
                label: Text(s.get(StringKey.generateFullMedia)),
              ),
              OutlinedButton.icon(
                onPressed: onTogglePreview,
                icon: Icon(showPreview ? Icons.expand_less : Icons.expand_more),
                label: Text(s.get(StringKey.generatePreviewMedia)),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _InsightTile extends StatelessWidget {
  const _InsightTile({required this.insight, required this.strings});

  final Insight insight;
  final AppStrings strings;

  (String, IconData) _content() => switch (insight.kind) {
        InsightKind.audioOnly =>
          (StringKey.hintAudioOnly, Icons.audiotrack_outlined),
        InsightKind.upscale =>
          (StringKey.hintUpscale, Icons.warning_amber_rounded),
        InsightKind.downscale =>
          (StringKey.hintDownscale, Icons.photo_size_select_small_outlined),
        InsightKind.lowResolutionSource =>
          (StringKey.hintLowRes, Icons.info_outline),
        InsightKind.hdrToneMap =>
          (StringKey.hintHdr, Icons.wb_twilight_outlined),
        InsightKind.streamCopy =>
          (StringKey.hintStreamCopy, Icons.bolt_outlined),
      };

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final warning = insight.level == InsightLevel.warning;
    final (key, icon) = _content();
    return Container(
      padding: const EdgeInsets.symmetric(
          horizontal: AppSpacing.m, vertical: AppSpacing.s),
      decoration: BoxDecoration(
        color: warning ? scheme.errorContainer : scheme.secondaryContainer,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon,
              size: 18,
              color: warning
                  ? scheme.onErrorContainer
                  : scheme.onSecondaryContainer),
          const SizedBox(width: AppSpacing.s),
          Expanded(
            child: Text(
              strings.get(key, insight.args),
              style: Theme.of(context).textTheme.bodySmall?.copyWith(
                  color: warning
                      ? scheme.onErrorContainer
                      : scheme.onSecondaryContainer),
            ),
          ),
        ],
      ),
    );
  }
}
