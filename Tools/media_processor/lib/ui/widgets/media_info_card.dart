// ignore_for_file: deprecated_member_use
import 'package:flutter/material.dart';

import '../../domain/media.dart';
import '../strings.dart';
import '../theme.dart';
import 'section_card.dart';

/// Read-only source metadata. A media tool must show what it actually loaded
/// before asking the user to choose output settings.
final class MediaInfoCard extends StatelessWidget {
  const MediaInfoCard({super.key, required this.media});

  final MediaInfo media;

  static String formatDuration(Duration duration) {
    final ms = duration.inMilliseconds;
    final hours = ms ~/ 3600000;
    final minutes = (ms % 3600000) ~/ 60000;
    final seconds = (ms % 60000) ~/ 1000;
    final millis = ms % 1000;
    final prefix = hours > 0 ? '${hours.toString().padLeft(2, '0')}:' : '';
    return '$prefix${minutes.toString().padLeft(2, '0')}:'
        '${seconds.toString().padLeft(2, '0')}.'
        '${millis.toString().padLeft(3, '0')}';
  }

  static String formatBytes(int bytes) {
    if (bytes <= 0) return '-';
    if (bytes >= 1 << 30) {
      return '${(bytes / (1 << 30)).toStringAsFixed(2)} GB';
    }
    return '${(bytes / (1 << 20)).toStringAsFixed(1)} MB';
  }

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final entries = <(String, String)>[
      (
        s.get(StringKey.sourceType),
        s.get(media.hasVideo
            ? StringKey.mediaTypeVideo
            : StringKey.mediaTypeAudio)
      ),
      if (media.hasVideo)
        (
          s.get(StringKey.sourceResolution),
          '${media.resolution.display} · ${media.resolution.tier}'
        ),
      (s.get(StringKey.sourceDuration), formatDuration(media.duration)),
      if (media.hasVideo && media.videoCodec != null)
        (s.get(StringKey.sourceVideoCodec), media.videoCodec!.toUpperCase()),
      if (media.hasAudio && media.audioCodec != null)
        (s.get(StringKey.sourceAudioCodec), media.audioCodec!.toUpperCase()),
      (s.get(StringKey.sourceSize), formatBytes(media.sizeBytes)),
      if (media.hasVideo)
        (s.get(StringKey.sourceColor), media.hdr ? 'HDR' : 'SDR'),
    ];
    return SectionCard(
      title: s.get(StringKey.mediaInfo),
      icon: Icons.insert_drive_file_outlined,
      child: Wrap(
        spacing: AppSpacing.l,
        runSpacing: AppSpacing.m,
        children: [
          for (final entry in entries)
            SizedBox(
              width: 150,
              child: _InfoTile(label: entry.$1, value: entry.$2),
            ),
        ],
      ),
    );
  }
}

class _InfoTile extends StatelessWidget {
  const _InfoTile({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(label,
            style: theme.textTheme.labelSmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        const SizedBox(height: AppSpacing.xs),
        Text(value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodyMedium
                ?.copyWith(fontWeight: FontWeight.w600)),
      ],
    );
  }
}
