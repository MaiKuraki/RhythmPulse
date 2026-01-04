import 'package:flutter/foundation.dart';

/// Enumeration representing possible states of media processing tasks
enum TaskStatus { idle, running, success, failed, canceled }

/// Enhanced media type detection with proper MIME type checking
enum MediaType { video, audio, unknown }

/// Output video format: MP4 (H.264) or WebM (VP8)
enum VideoFormat { mp4, webm }

/// Represents the result of an FFmpeg operation
class FfmpegResult {
  final bool success; // Indicates whether the operation succeeded
  final String log; // Contains execution logs or error messages

  const FfmpegResult(this.success, this.log);
}

/// Contains metadata information about a media file
@immutable
class MediaInfo {
  final int videoBitrate; // Video bitrate in bits per second (bps)
  final int width; // Video width in pixels
  final int height; // Video height in pixels
  final int audioBitrate; // Audio bitrate in bits per second (bps)
  final double durationSec; // Duration in seconds

  const MediaInfo({
    required this.videoBitrate,
    required this.width,
    required this.height,
    required this.audioBitrate,
    required this.durationSec,
  });
}
