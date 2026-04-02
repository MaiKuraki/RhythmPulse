import 'package:flutter/foundation.dart';

enum TaskStatus { idle, running, success, failed, canceled }

enum MediaType { video, audio, unknown }

enum VideoFormat { mp4, webm }

class FfmpegResult {
  final bool success;
  final String log;

  const FfmpegResult(this.success, this.log);
}

@immutable
class MediaInfo {
  final int videoBitrate;
  final int width;
  final int height;
  final int audioBitrate;
  final double durationSec;

  const MediaInfo({
    required this.videoBitrate,
    required this.width,
    required this.height,
    required this.audioBitrate,
    required this.durationSec,
  });
}
