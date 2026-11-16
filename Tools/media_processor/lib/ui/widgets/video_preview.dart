// ignore_for_file: deprecated_member_use
import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:media_kit/media_kit.dart';
import 'package:media_kit_video/media_kit_video.dart';

import '../../core/cancellation.dart';
import '../../domain/media.dart';
import '../../services/waveform.dart';
import '../strings.dart';
import '../theme.dart';
import 'section_card.dart';

/// Preview panel with generation-guarded waveform loading and complete
/// resource disposal. Switching files can no longer let an old async result
/// overwrite a newer one.
final class VideoPreviewPanel extends StatefulWidget {
  const VideoPreviewPanel({
    super.key,
    required this.filePath,
    required this.isVideo,
    required this.waveforms,
    required this.enabled,
    required this.onRangeChanged,
    required this.onGeneratePreview,
    this.aspectRatio = 16 / 9,
  });

  /// Real source aspect ratio; avoids forcing vertical / 4:3 media into 16:9.
  final double aspectRatio;
  final String filePath;
  final bool isVideo;
  final WaveformService waveforms;
  final bool enabled;
  final ValueChanged<ClipRange> onRangeChanged;
  final ValueChanged<ClipRange> onGeneratePreview;

  @override
  State<VideoPreviewPanel> createState() => _VideoPreviewPanelState();
}

class _VideoPreviewPanelState extends State<VideoPreviewPanel> {
  static const int _preSeekMs = 50;

  Player? _player;
  VideoController? _videoController;
  StreamSubscription<Duration>? _durationSub;
  StreamSubscription<Duration>? _positionSub;
  StreamSubscription<bool>? _playingSub;

  final ValueNotifier<int> _position = ValueNotifier<int>(0);
  final TextEditingController _startCtrl = TextEditingController();
  final TextEditingController _endCtrl = TextEditingController();

  int _durationMs = 0;
  int _rangeStartMs = 0;
  int _rangeEndMs = 0;
  bool _isPlaying = false;
  bool _looping = false;

  String? _waveformPath;
  bool _waveformLoading = false;
  CancellationToken? _waveformToken;
  int _generation = 0;

  ClipRange get _range =>
      ClipRange(startMs: _rangeStartMs, endMs: _rangeEndMs);

  @override
  void initState() {
    super.initState();
    _initPlayer();
    _loadWaveform();
  }

  @override
  void didUpdateWidget(covariant VideoPreviewPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.filePath != widget.filePath) {
      _generation++;
      _destroyPlayer();
      _initPlayer();
      _loadWaveform();
    }
  }

  void _initPlayer() {
    final player = Player();
    _player = player;
    if (widget.isVideo) _videoController = VideoController(player);

    _durationSub = player.stream.duration.listen((duration) {
      if (!mounted || duration.inMilliseconds == 0) return;
      final ms = duration.inMilliseconds;
      setState(() {
        _durationMs = ms;
        _rangeStartMs = 0;
        _rangeEndMs = ms;
      });
      _startCtrl.text = '0';
      _endCtrl.text = '$ms';
      widget.onRangeChanged(_range);
    });
    _positionSub = player.stream.position.listen((position) {
      if (!mounted) return;
      final ms = position.inMilliseconds;
      _position.value = ms;
      if (_looping && ms >= _rangeEndMs - _preSeekMs) {
        player.seek(Duration(milliseconds: _rangeStartMs));
      }
    });
    _playingSub = player.stream.playing.listen((playing) {
      if (mounted) setState(() => _isPlaying = playing);
    });
    player.open(Media(widget.filePath), play: false);
  }

  Future<void> _loadWaveform() async {
    _waveformToken?.cancel();
    _waveformToken?.dispose();
    final token = CancellationToken();
    _waveformToken = token;
    final generation = _generation;
    if (mounted) setState(() => _waveformLoading = true);

    final outcome = await widget.waveforms.ensure(
      widget.filePath,
      cancellation: token,
    );
    if (!mounted || generation != _generation || token.isCancelled) return;
    setState(() {
      _waveformLoading = false;
      _waveformPath = outcome.valueOrNull;
    });
  }

  void _destroyPlayer() {
    _durationSub?.cancel();
    _positionSub?.cancel();
    _playingSub?.cancel();
    _durationSub = _positionSub = null;
    _playingSub = null;
    _player?.dispose();
    _player = null;
    _videoController = null;
    _position.value = 0;
    _durationMs = 0;
    _isPlaying = false;
    _looping = false;
  }

  @override
  void dispose() {
    _generation++;
    _waveformToken?.cancel();
    _waveformToken?.dispose();
    _destroyPlayer();
    _position.dispose();
    _startCtrl.dispose();
    _endCtrl.dispose();
    super.dispose();
  }

  void _setStart(int value) {
    final clamped = value.clamp(0, _durationMs).toInt();
    if (clamped >= _rangeEndMs) return;
    setState(() => _rangeStartMs = clamped);
    _startCtrl.text = '$clamped';
    widget.onRangeChanged(_range);
  }

  void _setEnd(int value) {
    final clamped = value.clamp(0, _durationMs).toInt();
    if (clamped <= _rangeStartMs) return;
    setState(() => _rangeEndMs = clamped);
    _endCtrl.text = '$clamped';
    widget.onRangeChanged(_range);
  }

  void _startLoop() {
    setState(() => _looping = true);
    _player?.seek(Duration(milliseconds: _rangeStartMs));
    _player?.play();
  }

  void _stopLoop() {
    setState(() => _looping = false);
    _player?.pause();
  }

  static String _formatMs(int ms) {
    final value = ms < 0 ? 0 : ms;
    final minutes = (value ~/ 60000) % 60;
    final seconds = (value ~/ 1000) % 60;
    final millis = value % 1000;
    return '${minutes.toString().padLeft(2, '0')}:'
        '${seconds.toString().padLeft(2, '0')}.'
        '${millis.toString().padLeft(3, '0')}';
  }

  Widget _timeEditor({
    required String label,
    required TextEditingController controller,
    required int currentMs,
    required ValueChanged<int> onSubmitted,
  }) {
    final s = AppStrings.of(context);
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          height: AppSpacing.toolbarHeight,
          child: Row(
            children: [
              Text(label, style: theme.textTheme.labelMedium),
              const Spacer(),
              Text(_formatMs(currentMs),
                  style: theme.textTheme.labelMedium?.copyWith(
                      fontFamily: 'Consolas',
                      fontFamilyFallback: const ['monospace'])),
              const SizedBox(width: AppSpacing.xs),
              IconButton(
                constraints: const BoxConstraints.tightFor(
                    width: AppSpacing.toolbarHeight,
                    height: AppSpacing.toolbarHeight),
                padding: EdgeInsets.zero,
                tooltip: s.get(StringKey.setCurrent),
                onPressed: widget.enabled
                    ? () => onSubmitted(_position.value)
                    : null,
                icon: const Icon(Icons.my_location, size: 16),
              ),
            ],
          ),
        ),
        const SizedBox(height: AppSpacing.s),
        SizedBox(
          width: double.infinity,
          height: AppSpacing.controlHeight,
          child: TextField(
            controller: controller,
            enabled: widget.enabled,
            keyboardType: TextInputType.number,
            style: theme.textTheme.bodyMedium,
            decoration: const InputDecoration(
              isDense: true,
              suffixText: 'ms',
              border: OutlineInputBorder(),
              contentPadding: EdgeInsets.symmetric(
                  horizontal: AppSpacing.m, vertical: AppSpacing.s),
            ),
            onSubmitted: (text) {
              final value = int.tryParse(text);
              if (value != null) onSubmitted(value);
            },
          ),
        ),
        const SizedBox(height: AppSpacing.s),
        Wrap(
          spacing: AppSpacing.xs,
          runSpacing: AppSpacing.xs,
          children: [
            for (final delta in [-1000, -100, 1, 100, 1000])
              SizedBox(
                height: AppSpacing.microControlHeight,
                child: OutlinedButton(
                  style: OutlinedButton.styleFrom(
                    padding: const EdgeInsets.symmetric(
                        horizontal: AppSpacing.s),
                    minimumSize: Size.zero,
                    textStyle: theme.textTheme.labelSmall,
                  ),
                  onPressed: widget.enabled
                      ? () => onSubmitted(currentMs + delta)
                      : null,
                  child: Text('${delta > 0 ? '+' : ''}$delta'),
                ),
              ),
          ],
        ),
      ],
    );
  }

  Widget _waveform() {
    final s = AppStrings.of(context);
    final scheme = Theme.of(context).colorScheme;
    const double height = 112;
    if (_waveformLoading) {
      return Container(
        height: height,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: scheme.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(12),
        ),
        child: Text(s.get(StringKey.waveformLoading),
            style: TextStyle(color: scheme.onSurfaceVariant, fontSize: 12)),
      );
    }
    final path = _waveformPath;
    if (path == null || _durationMs == 0) return const SizedBox.shrink();
    // Purely decorative waveform + unlabeled tap region: excluding it keeps
    // LayoutBuilder-driven resize churn out of the semantics tree. The
    // accessible RangeSlider below remains the primary seek control.
    return ExcludeSemantics(
      child: RepaintBoundary(
        child: LayoutBuilder(builder: (context, constraints) {
          return GestureDetector(
            onTapDown: (details) {
              final fraction = (details.localPosition.dx /
                      constraints.maxWidth)
                  .clamp(0.0, 1.0)
                  .toDouble();
              _player?.seek(Duration(
                  milliseconds: (fraction * _durationMs).round()));
              if (_looping) _stopLoop();
            },
            child: SizedBox(
              height: height,
              width: constraints.maxWidth,
              child: ValueListenableBuilder<int>(
                valueListenable: _position,
                builder: (context, positionMs, child) => CustomPaint(
                  foregroundPainter: _WaveformOverlayPainter(
                    durationMs: _durationMs,
                    positionMs: positionMs,
                    rangeStartMs: _rangeStartMs,
                    rangeEndMs: _rangeEndMs,
                    dimColor: scheme.surface.withValues(alpha: 0.62),
                    boundaryColor: scheme.primary,
                    positionColor: scheme.onSurface,
                  ),
                  child: child,
                ),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(12),
                  child: Image.file(
                    File(path),
                    fit: BoxFit.fill,
                    width: constraints.maxWidth,
                    height: height,
                    gaplessPlayback: true,
                    errorBuilder: (_, _, _) => Container(
                        color: scheme.surfaceContainerHighest),
                  ),
                ),
              ),
            ),
          );
        }),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final s = AppStrings.of(context);
    final theme = Theme.of(context);
    final player = _player;
    final controller = _videoController;
    return SectionCard(
      title: s.get(StringKey.mediaPreview),
      icon: Icons.play_circle_outline,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (widget.isVideo && controller != null && player != null)
            Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(
                    maxWidth: 720, maxHeight: 400),
                // A media canvas needs a real backdrop: white source frames
                // otherwise blend into the light card surface.
                child: DecoratedBox(
                  decoration: BoxDecoration(
                    color: Colors.black,
                    borderRadius: BorderRadius.circular(12),
                  ),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(12),
                    child: ExcludeSemantics(
                      child: AspectRatio(
                        aspectRatio: widget.aspectRatio.clamp(0.5, 3.0).toDouble(),
                        child: Video(controller: controller),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          if (_durationMs == 0)
            const Padding(
              padding: EdgeInsets.all(AppSpacing.l),
              child: Center(child: CircularProgressIndicator()),
            )
          else ...[
            const SizedBox(height: AppSpacing.m),
            _waveform(),
            const SizedBox(height: AppSpacing.s),
            Row(
              children: [
                IconButton(
                  visualDensity: VisualDensity.compact,
                  tooltip: s.get(StringKey.playSelection),
                  icon: Icon(_isPlaying
                      ? Icons.pause_circle_filled
                      : Icons.play_circle_filled),
                  iconSize: 34,
                  onPressed: () => _player?.playOrPause(),
                ),
                ValueListenableBuilder<int>(
                  valueListenable: _position,
                  builder: (context, position, _) => Text(
                    '${_formatMs(position)} / ${_formatMs(_durationMs)}',
                    style: theme.textTheme.bodyMedium?.copyWith(
                      fontFamily: 'Consolas',
                      fontFamilyFallback: const ['monospace'],
                    ),
                  ),
                ),
                const Spacer(),
                FilledButton.tonalIcon(
                  onPressed: _looping ? _stopLoop : _startLoop,
                  icon: Icon(_looping ? Icons.stop : Icons.loop, size: 16),
                  label: Text(s.get(
                      _looping ? StringKey.stopLoop : StringKey.loopSelection)),
                ),
              ],
            ),
            const SizedBox(height: AppSpacing.s),
            Text(s.get(StringKey.clipRange),
                style: theme.textTheme.titleSmall),
            RangeSlider(
              values: RangeValues(
                _rangeStartMs.toDouble(),
                _rangeEndMs.toDouble(),
              ),
              max: _durationMs.toDouble(),
              labels: RangeLabels(
                  _formatMs(_rangeStartMs), _formatMs(_rangeEndMs)),
              onChanged: widget.enabled
                  ? (values) {
                      _setStart(values.start.round());
                      _setEnd(values.end.round());
                    }
                  : null,
            ),
            const SizedBox(height: AppSpacing.s),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(
                  child: _timeEditor(
                    label: s.get(StringKey.rangeStart),
                    controller: _startCtrl,
                    currentMs: _rangeStartMs,
                    onSubmitted: _setStart,
                  ),
                ),
                const SizedBox(width: AppSpacing.m),
                Expanded(
                  child: _timeEditor(
                    label: s.get(StringKey.rangeEnd),
                    controller: _endCtrl,
                    currentMs: _rangeEndMs,
                    onSubmitted: _setEnd,
                  ),
                ),
              ],
            ),
            const SizedBox(height: AppSpacing.m),
            SizedBox(
              width: double.infinity,
              child: FilledButton.icon(
                onPressed: (widget.enabled && _range.isValid)
                    ? () => widget.onGeneratePreview(_range)
                    : null,
                icon: const Icon(Icons.content_cut),
                label: Text(s.get(StringKey.generatePreview)),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

final class _WaveformOverlayPainter extends CustomPainter {
  const _WaveformOverlayPainter({
    required this.durationMs,
    required this.positionMs,
    required this.rangeStartMs,
    required this.rangeEndMs,
    required this.dimColor,
    required this.boundaryColor,
    required this.positionColor,
  });

  final int durationMs;
  final int positionMs;
  final int rangeStartMs;
  final int rangeEndMs;
  final Color dimColor;
  final Color boundaryColor;
  final Color positionColor;

  @override
  void paint(Canvas canvas, Size size) {
    if (durationMs <= 0) return;
    final dim = Paint()..color = dimColor;
    final boundary = Paint()
      ..color = boundaryColor
      ..strokeWidth = 2;
    final position = Paint()
      ..color = positionColor
      ..strokeWidth = 1.5;
    final w = size.width;
    final h = size.height;
    final startX = (rangeStartMs / durationMs) * w;
    final endX = (rangeEndMs / durationMs) * w;
    final posX = (positionMs / durationMs) * w;
    if (startX > 0) canvas.drawRect(Rect.fromLTWH(0, 0, startX, h), dim);
    if (endX < w) canvas.drawRect(Rect.fromLTWH(endX, 0, w - endX, h), dim);
    canvas.drawLine(Offset(startX, 0), Offset(startX, h), boundary);
    canvas.drawLine(Offset(endX, 0), Offset(endX, h), boundary);
    canvas.drawLine(Offset(posX, 0), Offset(posX, h), position);
  }

  @override
  bool shouldRepaint(covariant _WaveformOverlayPainter old) =>
      old.positionMs != positionMs ||
      old.rangeStartMs != rangeStartMs ||
      old.rangeEndMs != rangeEndMs ||
      old.durationMs != durationMs ||
      old.dimColor != dimColor ||
      old.boundaryColor != boundaryColor ||
      old.positionColor != positionColor;
}
