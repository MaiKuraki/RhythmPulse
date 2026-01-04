import 'package:flutter/material.dart';
import 'package:localization/localization.dart';
import '../../models/processing_state.dart';

class ControlPanel extends StatelessWidget {
  final TaskStatus taskStatus;
  final bool isFileSelected;
  final bool showPreviewOptions;
  final bool apply4k;
  final VideoFormat videoFormat;
  final VoidCallback? onGenerateFull;
  final VoidCallback? onTogglePreview;
  final VoidCallback? onCancel;
  final ValueChanged<bool>? on4kChanged;
  final ValueChanged<VideoFormat>? onFormatChanged;
  final Widget? previewOptionsChild;
  final double progress;

  const ControlPanel({
    super.key,
    required this.taskStatus,
    required this.isFileSelected,
    required this.showPreviewOptions,
    required this.apply4k,
    required this.videoFormat,
    this.progress = 0.0,
    this.onGenerateFull,
    this.onTogglePreview,
    this.onCancel,
    this.on4kChanged,
    this.onFormatChanged,
    this.previewOptionsChild,
  });

  bool get _isProcessing => taskStatus == TaskStatus.running;

  @override
  Widget build(BuildContext context) {
    // Status Logic
    String statusText;
    Color statusColor;
    IconData? statusIcon;

    switch (taskStatus) {
      case TaskStatus.running:
        statusText = 'taskStatusRunning'.i18n();
        statusColor = Colors.blue;
        break;
      case TaskStatus.success:
        statusText = 'taskStatusSuccess'.i18n();
        statusColor = Colors.green;
        statusIcon = Icons.check_circle;
        break;
      case TaskStatus.failed:
        statusText = 'taskStatusFailed'.i18n();
        statusColor = Colors.red;
        statusIcon = Icons.error;
        break;
      case TaskStatus.canceled:
        statusText = 'taskStatusCanceled'.i18n();
        statusColor = Colors.orange;
        statusIcon = Icons.warning;
        break;
      case TaskStatus.idle:
        statusText = 'taskStatusIdle'.i18n();
        statusColor = Colors.grey;
        break;
    }

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
                        onSelectionChanged: _isProcessing ? null : (set) => onFormatChanged?.call(set.first),
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
                        onChanged: _isProcessing ? null : on4kChanged,
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
                    onPressed: (isFileSelected && !_isProcessing) ? onGenerateFull : null,
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
              onPressed: (isFileSelected && !_isProcessing) ? onTogglePreview : null,
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(48),
                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
              ),
            ),

            // Preview Options Area
            AnimatedSize(
              duration: const Duration(milliseconds: 200),
              child: showPreviewOptions && previewOptionsChild != null
                  ? Padding(
                      padding: const EdgeInsets.only(top: 16),
                      child: Container(
                        padding: const EdgeInsets.all(16),
                        decoration: BoxDecoration(
                          color: Colors.deepPurple.shade50.withAlpha(128),
                          borderRadius: BorderRadius.circular(12),
                          border: Border.all(color: Colors.deepPurple.shade100),
                        ),
                        child: previewOptionsChild!,
                      ),
                    )
                  : const SizedBox.shrink(),
            ),

            if (_isProcessing) ...[
              const SizedBox(height: 24),
              SizedBox(
                width: double.infinity,
                child: ElevatedButton.icon(
                  icon: const Icon(Icons.stop_circle_outlined),
                  label: Text('cancelAllTasks'.i18n()),
                  onPressed: onCancel,
                  style: ElevatedButton.styleFrom(
                    backgroundColor: Colors.red.shade50,
                    foregroundColor: Colors.red,
                    elevation: 0,
                  ),
                ),
              ),
            ],

            // Status Footer
            const SizedBox(height: 24),
            Container(
              padding: const EdgeInsets.all(16),
              decoration: BoxDecoration(
                color: _isProcessing ? Colors.blue.shade50 : (statusColor == Colors.grey ? Colors.grey.shade50 : statusColor.withAlpha(26)),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: _isProcessing ? Colors.blue.shade100 : statusColor.withAlpha(51)),
              ),
              child: Column(
                children: [
                  Row(
                    children: [
                      if (_isProcessing)
                        SizedBox(width: 20, height: 20, child: CircularProgressIndicator(strokeWidth: 2.5, value: progress > 0 ? progress : null))
                      else
                        Icon(statusIcon ?? Icons.circle, size: 20, color: statusColor),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Text(
                          _isProcessing 
                            ? '${'processingPleaseWait'.i18n()} ${(progress * 100).toStringAsFixed(1)}%'
                            : statusText,
                          style: TextStyle(fontWeight: FontWeight.bold, color: statusColor),
                        ),
                      ),
                    ],
                  ),
                  if (_isProcessing) ...[
                    const SizedBox(height: 12),
                    ClipRRect(
                      borderRadius: BorderRadius.circular(4),
                      child: LinearProgressIndicator(
                        value: progress > 0 ? progress : null,
                        minHeight: 6,
                        backgroundColor: Colors.white,
                      ),
                    ),
                  ]
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
