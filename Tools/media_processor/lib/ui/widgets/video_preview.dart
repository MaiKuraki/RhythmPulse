import 'dart:async';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:localization/localization.dart';
import 'package:media_kit/media_kit.dart';
import 'package:media_kit_video/media_kit_video.dart';
import 'package:path/path.dart' as p;

import '../../services/ffmpeg_service.dart';

class VideoPreviewPanel extends StatefulWidget {
  final String filePath;
  final bool isVideo;
  final ValueChanged<int>? onStartMsChanged;
  final ValueChanged<int>? onEndMsChanged;
  final VoidCallback? onGeneratePreview;
  final bool enabled;

  const VideoPreviewPanel({
    super.key,
    required this.filePath,
    required this.isVideo,
    this.onStartMsChanged,
    this.onEndMsChanged,
    this.onGeneratePreview,
    this.enabled = true,
  });

  @override
  State<VideoPreviewPanel> createState() => _VideoPreviewPanelState();
}

class _VideoPreviewPanelState extends State<VideoPreviewPanel> {
  late Player _player;
  VideoController? _videoController;

  int _durationMs = 0;
  bool _isPlaying = false;

  int _rangeStartMs = 0;
  int _rangeEndMs = 0;

  bool _loopSelection = false;

  // Position via ValueNotifier — avoids full widget tree rebuilds
  final ValueNotifier<int> _positionNotifier = ValueNotifier<int>(0);

  // Pre-seek margin in ms — seek this much before reaching the range end
  // to minimize the audible gap at the loop point.
  static const int _loopPreSeekMs = 50;

  late final TextEditingController _startMsCtrl;
  late final TextEditingController _endMsCtrl;

  StreamSubscription? _durationSub;
  StreamSubscription? _positionSub;
  StreamSubscription? _playingSub;

  // Waveform
  FileImage? _waveformImageProvider;
  bool _waveformLoading = false;

  @override
  void initState() {
    super.initState();
    _startMsCtrl = TextEditingController(text: '0');
    _endMsCtrl = TextEditingController(text: '0');
    _initPlayer();
    _generateWaveform();
  }

  @override
  void didUpdateWidget(covariant VideoPreviewPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.filePath != widget.filePath) {
      _destroyPlayer();
      _initPlayer();
      _generateWaveform();
    }
  }

  void _initPlayer() {
    _player = Player();
    if (widget.isVideo) {
      _videoController = VideoController(_player);
    }

    _durationSub = _player.stream.duration.listen((d) {
      if (!mounted || d.inMilliseconds == 0) return;
      setState(() {
        _durationMs = d.inMilliseconds;
        _rangeStartMs = 0;
        _rangeEndMs = d.inMilliseconds;
      });
      _startMsCtrl.text = '0';
      _endMsCtrl.text = '$_durationMs';
      widget.onStartMsChanged?.call(0);
      widget.onEndMsChanged?.call(d.inMilliseconds);
    });

    _positionSub = _player.stream.position.listen((p) {
      if (!mounted) return;
      final ms = p.inMilliseconds;
      _positionNotifier.value = ms;
      if (_loopSelection && ms >= _rangeEndMs - _loopPreSeekMs) {
        _player.seek(Duration(milliseconds: _rangeStartMs));
      }
    });

    _playingSub = _player.stream.playing.listen((playing) {
      if (!mounted) return;
      setState(() => _isPlaying = playing);
    });

    _player.open(Media(widget.filePath), play: false);
    _player.setVolume(50);
  }

  Future<void> _generateWaveform() async {
    _waveformImageProvider?.evict();
    _waveformImageProvider = null;
    setState(() {
      _waveformLoading = true;
    });

    final dir = Directory.systemTemp;
    final hash = widget.filePath.hashCode.toRadixString(16);
    final outPath = p.join(dir.path, 'media_processor_waveform_$hash.png');

    final result = await FfmpegService.generateWaveformImage(
      inputPath: widget.filePath,
      outputImagePath: outPath,
      width: 2048,
      height: 200,
      onLog: (_) {},
    );

    if (!mounted) return;
    final file = File(outPath);
    final exists = result.success && file.existsSync();
    setState(() {
      _waveformLoading = false;
      if (exists) {
        _waveformImageProvider = FileImage(file);
      }
    });
  }

  void _destroyPlayer() {
    _durationSub?.cancel();
    _positionSub?.cancel();
    _playingSub?.cancel();
    _player.dispose();
    _videoController = null;
    _durationMs = 0;
    _positionNotifier.value = 0;
    _isPlaying = false;
    _loopSelection = false;
  }

  @override
  void dispose() {
    _destroyPlayer();
    _positionNotifier.dispose();
    _startMsCtrl.dispose();
    _endMsCtrl.dispose();
    _waveformImageProvider?.evict();
    super.dispose();
  }

  // --- Range helpers ---

  void _setRangeStart(int ms) {
    ms = ms.clamp(0, _durationMs);
    if (ms >= _rangeEndMs) return;
    setState(() => _rangeStartMs = ms);
    _startMsCtrl.text = '$ms';
    widget.onStartMsChanged?.call(ms);
  }

  void _setRangeEnd(int ms) {
    ms = ms.clamp(0, _durationMs);
    if (ms <= _rangeStartMs) return;
    setState(() => _rangeEndMs = ms);
    _endMsCtrl.text = '$ms';
    widget.onEndMsChanged?.call(ms);
  }

  void _commitStartFromText() {
    final ms = int.tryParse(_startMsCtrl.text);
    if (ms == null) {
      _startMsCtrl.text = '$_rangeStartMs';
      return;
    }
    _setRangeStart(ms);
  }

  void _commitEndFromText() {
    final ms = int.tryParse(_endMsCtrl.text);
    if (ms == null) {
      _endMsCtrl.text = '$_rangeEndMs';
      return;
    }
    _setRangeEnd(ms);
  }

  void _startLoopPlayback() {
    setState(() => _loopSelection = true);
    _player.seek(Duration(milliseconds: _rangeStartMs));
    _player.play();
  }

  void _stopLoopPlayback() {
    setState(() => _loopSelection = false);
    _player.pause();
  }

  static String _formatMs(int ms) {
    if (ms < 0) ms = 0;
    final h = ms ~/ 3600000;
    final m = (ms ~/ 60000) % 60;
    final s = (ms ~/ 1000) % 60;
    final frac = ms % 1000;
    final time = '${m.toString().padLeft(2, '0')}:${s.toString().padLeft(2, '0')}.${frac.toString().padLeft(3, '0')}';
    return h > 0 ? '$h:$time' : time;
  }

  Widget _buildNudgeButton(String label, VoidCallback? onPressed) {
    return SizedBox(
      height: 28,
      child: OutlinedButton(
        onPressed: onPressed,
        style: OutlinedButton.styleFrom(
          padding: const EdgeInsets.symmetric(horizontal: 6),
          textStyle: const TextStyle(fontSize: 11, fontFamily: 'Consolas'),
          minimumSize: Size.zero,
          shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(6)),
        ),
        child: Text(label),
      ),
    );
  }

  Widget _buildMsEditor({
    required String label,
    required TextEditingController controller,
    required int currentMs,
    required VoidCallback onSubmit,
    required ValueChanged<int> onNudge,
    required VoidCallback onSetToCurrent,
  }) {
    final enabled = widget.enabled;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Text(label, style: TextStyle(
              fontWeight: FontWeight.bold,
              color: Colors.deepPurple.shade700,
              fontSize: 12,
            )),
            const SizedBox(width: 8),
            Text(
              _formatMs(currentMs),
              style: TextStyle(fontSize: 11, color: Colors.grey.shade600, fontFamily: 'Consolas'),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Row(
          children: [
            SizedBox(
              width: 110,
              height: 36,
              child: TextField(
                controller: controller,
                enabled: enabled,
                keyboardType: TextInputType.number,
                inputFormatters: [FilteringTextInputFormatter.digitsOnly],
                style: const TextStyle(fontSize: 13, fontFamily: 'Consolas'),
                decoration: InputDecoration(
                  isDense: true,
                  contentPadding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                  suffixText: 'ms',
                  suffixStyle: TextStyle(fontSize: 11, color: Colors.grey.shade500),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
                ),
                onSubmitted: (_) => onSubmit(),
              ),
            ),
            const SizedBox(width: 8),
            Tooltip(
              message: 'setCurrent'.i18n(),
              child: SizedBox(
                height: 36,
                width: 36,
                child: IconButton(
                  onPressed: enabled ? onSetToCurrent : null,
                  icon: const Icon(Icons.my_location, size: 18),
                  style: IconButton.styleFrom(
                    backgroundColor: Colors.deepPurple.shade50,
                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                    padding: EdgeInsets.zero,
                  ),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Wrap(
          spacing: 4,
          runSpacing: 4,
          children: [
            for (final delta in [-1000, -100, -10, -1, 1, 10, 100, 1000])
              _buildNudgeButton(
                '${delta > 0 ? '+' : ''}$delta',
                enabled ? () => onNudge(delta) : null,
              ),
          ],
        ),
      ],
    );
  }

  Widget _buildWaveformTimeline() {
    if (_waveformLoading) {
      return Container(
        height: 120,
        decoration: BoxDecoration(
          color: Colors.grey.shade900,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Center(
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              const SizedBox(width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2, color: Colors.white54)),
              const SizedBox(width: 12),
              Text('waveformLoading'.i18n(), style: const TextStyle(color: Colors.white54, fontSize: 12)),
            ],
          ),
        ),
      );
    }

    if (_waveformImageProvider == null || _durationMs == 0) {
      return const SizedBox.shrink();
    }

    return RepaintBoundary(
      child: LayoutBuilder(builder: (context, constraints) {
        final totalWidth = constraints.maxWidth;
        return GestureDetector(
          onTapDown: (details) {
            final fraction = (details.localPosition.dx / totalWidth).clamp(0.0, 1.0);
            final ms = (fraction * _durationMs).round();
            _player.seek(Duration(milliseconds: ms));
            if (_loopSelection) _stopLoopPlayback();
          },
          child: ClipRRect(
            borderRadius: BorderRadius.circular(8),
            child: SizedBox(
              height: 120,
              width: totalWidth,
              child: ValueListenableBuilder<int>(
                valueListenable: _positionNotifier,
                builder: (context, posMs, child) {
                  return CustomPaint(
                    foregroundPainter: _WaveformOverlayPainter(
                      durationMs: _durationMs,
                      positionMs: posMs,
                      rangeStartMs: _rangeStartMs,
                      rangeEndMs: _rangeEndMs,
                    ),
                    child: child,
                  );
                },
                child: Image(
                  image: _waveformImageProvider!,
                  fit: BoxFit.fill,
                  width: totalWidth,
                  height: 120,
                  gaplessPlayback: true,
                  errorBuilder: (_, _, _) => Container(
                    color: Colors.grey.shade900,
                    child: const Center(child: Icon(Icons.broken_image, color: Colors.white38)),
                  ),
                ),
              ),
            ),
          ),
        );
      }),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(Icons.ondemand_video, color: Colors.deepPurple.shade700),
                const SizedBox(width: 12),
                Text(
                  'mediaPreview'.i18n(),
                  style: Theme.of(context).textTheme.titleLarge?.copyWith(
                    fontWeight: FontWeight.bold,
                    color: Colors.deepPurple.shade800,
                  ),
                ),
              ],
            ),
            const Divider(height: 24),

            // Video player — constrained to 640px max, centered
            if (widget.isVideo && _videoController != null)
              Center(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 640),
                  child: ClipRRect(
                    borderRadius: BorderRadius.circular(12),
                    child: Container(
                      color: Colors.black,
                      child: AspectRatio(
                        aspectRatio: 16 / 9,
                        child: Video(controller: _videoController!),
                      ),
                    ),
                  ),
                ),
              ),

            if (_durationMs == 0) ...[
              const SizedBox(height: 40),
              const Center(child: CircularProgressIndicator()),
              const SizedBox(height: 40),
            ] else ...[
              const SizedBox(height: 16),

              // Audio waveform timeline (isolated repaint boundary)
              _buildWaveformTimeline(),
              const SizedBox(height: 12),

              // Playback controls — position text isolated via ValueListenableBuilder
              Row(
                children: [
                  IconButton(
                    onPressed: () {
                      if (_loopSelection) _stopLoopPlayback();
                      _player.playOrPause();
                    },
                    icon: Icon(_isPlaying ? Icons.pause_circle_filled : Icons.play_circle_filled),
                    iconSize: 36,
                    color: Colors.deepPurple,
                  ),
                  const SizedBox(width: 8),
                  ValueListenableBuilder<int>(
                    valueListenable: _positionNotifier,
                    builder: (context, posMs, _) {
                      return Text(
                        '${_formatMs(posMs)} / ${_formatMs(_durationMs)}',
                        style: const TextStyle(fontFamily: 'Consolas', fontSize: 14),
                      );
                    },
                  ),
                  const Spacer(),
                  if (_loopSelection)
                    FilledButton.tonalIcon(
                      onPressed: _stopLoopPlayback,
                      icon: const Icon(Icons.stop, size: 18),
                      label: Text('stopLoop'.i18n()),
                      style: FilledButton.styleFrom(
                        backgroundColor: Colors.red.shade50,
                        foregroundColor: Colors.red.shade700,
                      ),
                    )
                  else
                    FilledButton.tonalIcon(
                      onPressed: _startLoopPlayback,
                      icon: const Icon(Icons.loop, size: 18),
                      label: Text('loopSelection'.i18n()),
                    ),
                ],
              ),

              // Seek slider — isolated rebuild
              ValueListenableBuilder<int>(
                valueListenable: _positionNotifier,
                builder: (context, posMs, _) {
                  return SliderTheme(
                    data: SliderTheme.of(context).copyWith(
                      trackHeight: 4,
                      thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 7),
                      overlayShape: const RoundSliderOverlayShape(overlayRadius: 14),
                    ),
                    child: Slider(
                      value: posMs.clamp(0, _durationMs).toDouble(),
                      max: _durationMs.toDouble(),
                      activeColor: Colors.deepPurple,
                      inactiveColor: Colors.deepPurple.shade100,
                      onChanged: (v) {
                        if (_loopSelection) _stopLoopPlayback();
                        _player.seek(Duration(milliseconds: v.round()));
                      },
                    ),
                  );
                },
              ),
              const SizedBox(height: 8),

              // Clip range section (static — only rebuilds on range change)
              Container(
                padding: const EdgeInsets.all(16),
                decoration: BoxDecoration(
                  color: Colors.deepPurple.shade50.withAlpha(128),
                  borderRadius: BorderRadius.circular(12),
                  border: Border.all(color: Colors.deepPurple.shade100),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Text(
                          'clipRange'.i18n(),
                          style: TextStyle(
                            fontWeight: FontWeight.bold,
                            color: Colors.deepPurple.shade700,
                            fontSize: 13,
                          ),
                        ),
                        const Spacer(),
                        Text(
                          '${'rangeDuration'.i18n()}: ${_formatMs(_rangeEndMs - _rangeStartMs)}',
                          style: TextStyle(fontSize: 12, color: Colors.grey.shade600, fontFamily: 'Consolas'),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),

                    RangeSlider(
                      values: RangeValues(
                        _rangeStartMs.clamp(0, _durationMs).toDouble(),
                        _rangeEndMs.clamp(0, _durationMs).toDouble(),
                      ),
                      max: _durationMs.toDouble(),
                      activeColor: Colors.deepPurple.shade400,
                      inactiveColor: Colors.grey.shade300,
                      labels: RangeLabels(
                        _formatMs(_rangeStartMs),
                        _formatMs(_rangeEndMs),
                      ),
                      onChanged: widget.enabled ? (values) {
                        _setRangeStart(values.start.round());
                        final endMs = values.end.round().clamp(0, _durationMs);
                        if (endMs > _rangeStartMs) {
                          setState(() => _rangeEndMs = endMs);
                          _endMsCtrl.text = '$endMs';
                          widget.onEndMsChanged?.call(endMs);
                        }
                      } : null,
                    ),
                    const SizedBox(height: 12),

                    Row(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Expanded(
                          child: _buildMsEditor(
                            label: 'rangeStart'.i18n(),
                            controller: _startMsCtrl,
                            currentMs: _rangeStartMs,
                            onSubmit: _commitStartFromText,
                            onNudge: (delta) => _setRangeStart(_rangeStartMs + delta),
                            onSetToCurrent: () => _setRangeStart(_positionNotifier.value),
                          ),
                        ),
                        const SizedBox(width: 24),
                        Expanded(
                          child: _buildMsEditor(
                            label: 'rangeEnd'.i18n(),
                            controller: _endMsCtrl,
                            currentMs: _rangeEndMs,
                            onSubmit: _commitEndFromText,
                            onNudge: (delta) => _setRangeEnd(_rangeEndMs + delta),
                            onSetToCurrent: () => _setRangeEnd(_positionNotifier.value),
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 16),

                    SizedBox(
                      width: double.infinity,
                      child: ElevatedButton.icon(
                        icon: const Icon(Icons.content_cut),
                        label: Text('generatePreview'.i18n()),
                        onPressed: (widget.enabled && _rangeEndMs > _rangeStartMs)
                            ? widget.onGeneratePreview
                            : null,
                        style: ElevatedButton.styleFrom(
                          backgroundColor: Colors.deepPurple,
                          foregroundColor: Colors.white,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class _WaveformOverlayPainter extends CustomPainter {
  final int durationMs;
  final int positionMs;
  final int rangeStartMs;
  final int rangeEndMs;

  // Pre-allocated paints — zero GC per frame
  static final Paint _dimPaint = Paint()..color = const Color(0xAA000000);
  static final Paint _boundaryPaint = Paint()
    ..color = const Color(0xFF7C4DFF)
    ..strokeWidth = 2;
  static final Paint _posPaint = Paint()
    ..color = const Color(0xFFFFFFFF)
    ..strokeWidth = 1.5;

  const _WaveformOverlayPainter({
    required this.durationMs,
    required this.positionMs,
    required this.rangeStartMs,
    required this.rangeEndMs,
  });

  @override
  void paint(Canvas canvas, Size size) {
    if (durationMs <= 0) return;
    final w = size.width;
    final h = size.height;

    final startX = (rangeStartMs / durationMs) * w;
    final endX = (rangeEndMs / durationMs) * w;

    // Dim areas outside the selected range
    if (startX > 0) {
      canvas.drawRect(Rect.fromLTWH(0, 0, startX, h), _dimPaint);
    }
    if (endX < w) {
      canvas.drawRect(Rect.fromLTWH(endX, 0, w - endX, h), _dimPaint);
    }

    // Range boundary lines
    canvas.drawLine(Offset(startX, 0), Offset(startX, h), _boundaryPaint);
    canvas.drawLine(Offset(endX, 0), Offset(endX, h), _boundaryPaint);

    // Playback position line
    final posX = (positionMs / durationMs) * w;
    canvas.drawLine(Offset(posX, 0), Offset(posX, h), _posPaint);
  }

  @override
  bool shouldRepaint(covariant _WaveformOverlayPainter oldDelegate) {
    return oldDelegate.positionMs != positionMs ||
        oldDelegate.rangeStartMs != rangeStartMs ||
        oldDelegate.rangeEndMs != rangeEndMs ||
        oldDelegate.durationMs != durationMs;
  }
}
