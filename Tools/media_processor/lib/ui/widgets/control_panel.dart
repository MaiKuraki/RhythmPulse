import 'package:flutter/material.dart';
import 'package:localization/localization.dart';
import '../../models/processing_state.dart';

class ControlPanel extends StatelessWidget {
  final bool isFileSelected;
  final bool isProcessing;
  final bool showPreviewOptions;
  final bool apply4k;
  final VideoFormat videoFormat;
  final VoidCallback? onGenerateFull;
  final VoidCallback? onTogglePreview;
  final ValueChanged<bool>? on4kChanged;
  final ValueChanged<VideoFormat>? onFormatChanged;

  const ControlPanel({
    super.key,
    required this.isFileSelected,
    required this.isProcessing,
    required this.showPreviewOptions,
    required this.apply4k,
    required this.videoFormat,
    this.onGenerateFull,
    this.onTogglePreview,
    this.on4kChanged,
    this.onFormatChanged,
  });

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            // Header
            Row(
              children: [
                Icon(Icons.tune, color: Colors.deepPurple.shade700),
                const SizedBox(width: 12),
                Text(
                  'operation'.i18n(),
                  style: Theme.of(context).textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.bold,
                    color: Colors.deepPurple.shade800,
                  ),
                ),
              ],
            ),
            const Divider(height: 32),

            // Section 1: Configuration
            Text(
              'configuration'.i18n(),
              style: Theme.of(context).textTheme.labelLarge?.copyWith(
                color: Colors.grey.shade600,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 16),
            
            // Format Selector
            Container(
              decoration: BoxDecoration(
                color: Colors.grey.shade50,
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: Colors.grey.shade200),
              ),
              padding: const EdgeInsets.all(16),
              child: Column(
                children: [
                  Row(
                    children: [
                      Text('videoFormat'.i18n(), style: const TextStyle(fontWeight: FontWeight.w500)),
                      const SizedBox(width: 24),
                      SegmentedButton<VideoFormat>(
                        segments: const [
                          ButtonSegment(value: VideoFormat.mp4, label: Text('MP4')),
                          ButtonSegment(value: VideoFormat.webm, label: Text('WebM')),
                        ],
                        selected: {videoFormat},
                        onSelectionChanged: isProcessing ? null : (set) => onFormatChanged?.call(set.first),
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Text('output4K'.i18n(), style: const TextStyle(fontWeight: FontWeight.w500)),
                      const SizedBox(width: 24),
                      Switch.adaptive(
                        value: apply4k,
                        onChanged: isProcessing ? null : on4kChanged,
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(height: 24),

            // Section 2: Actions
            Text(
              'actions'.i18n(),
              style: Theme.of(context).textTheme.labelLarge?.copyWith(
                color: Colors.grey.shade600,
                fontWeight: FontWeight.bold,
              ),
            ),
            const SizedBox(height: 16),

            Row(
              children: [
                Expanded(
                  child: ElevatedButton.icon(
                    icon: const Icon(Icons.movie_creation_outlined),
                    label: Text('generateFullMedia'.i18n()),
                    onPressed: (isFileSelected && !isProcessing) ? onGenerateFull : null,
                    style: ElevatedButton.styleFrom(
                      backgroundColor: Colors.deepPurple,
                      foregroundColor: Colors.white,
                      elevation: 4,
                      shadowColor: Colors.deepPurple.withAlpha(77),
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12),
            OutlinedButton.icon(
              icon: Icon(showPreviewOptions ? Icons.expand_less : Icons.expand_more),
              label: Text('generatePreviewMedia'.i18n()),
              onPressed: (isFileSelected && !isProcessing) ? onTogglePreview : null,
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(48),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
