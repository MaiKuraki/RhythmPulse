import 'encoding.dart';
import 'media.dart';

/// Explicit output resolution policy. Unlike a boolean "keep 4K" switch this
/// covers the full 720p–4K range and makes the no-upscale default obvious.
enum OutputResolution {
  follow(0),
  p720(720),
  p1080(1080),
  p1440(1440),
  p2160(2160);

  const OutputResolution(this.height);

  /// Target height in pixels; 0 means "keep the source resolution".
  final int height;

  bool get isFollow => this == OutputResolution.follow;

  /// Short labels: dropdown menu items must never be ellipsized. The exact
  /// pixel dimensions are shown separately below the field.
  String get label => switch (this) {
        OutputResolution.follow => 'source',
        OutputResolution.p720 => '720p',
        OutputResolution.p1080 => '1080p',
        OutputResolution.p1440 => '2K',
        OutputResolution.p2160 => '4K',
      };
}

enum InsightLevel { info, warning }

enum InsightKind {
  audioOnly,
  upscale,
  downscale,
  lowResolutionSource,
  hdrToneMap,
  streamCopy,
}

/// A domain-level advisory. The UI maps [kind] to localized text; no strings
/// from the widget layer leak into the decision logic.
final class Insight {
  const Insight(this.kind,
      {this.level = InsightLevel.info, this.args = const <String>[]});

  final InsightKind kind;
  final InsightLevel level;
  final List<String> args;
}

/// Pure resolution plan shared by the processor and the UI, so the preview in
/// the settings panel is exactly what the encoder will receive.
final class OutputPlan {
  const OutputPlan({
    required this.source,
    required this.target,
    required this.mode,
  });

  final Resolution source;
  final Resolution target;
  final OutputResolution mode;

  bool get hasVideo => !source.isEmpty;
  bool get transform => hasVideo &&
      (target.width != source.width || target.height != source.height);
  bool get upscale => hasVideo && target.height > source.height;
  bool get downscale => hasVideo && target.height < source.height;

  String get sourceLabel => source.isEmpty ? '-' : '${source.width}×${source.height}';
  String get targetLabel => mode.isFollow
      ? sourceLabel
      : '${mode.label} (${target.width}×${target.height})';

  static OutputPlan build(MediaInfo media, OutputResolution mode) {
    final source = media.hasVideo
        ? media.resolution
        : const Resolution(0, 0);
    if (!media.hasVideo || source.isEmpty || mode.isFollow) {
      return OutputPlan(source: source, target: source, mode: mode);
    }
    final height = mode.height;
    var width = (height * source.width / source.height).round();
    // H.264/VP8 encoders require even dimensions.
    if (width.isOdd) width++;
    if (width < 2) width = 2;
    return OutputPlan(
      source: source,
      target: Resolution(width, height),
      mode: mode,
    );
  }
}

/// Advisories shown next to the resolution selector. Ordering is intentional:
/// the most consequential warning comes first.
List<Insight> buildInsights(
  MediaInfo media,
  OutputPlan plan,
  EncodingProfile profile,
) {
  if (!media.hasVideo) {
    return const [Insight(InsightKind.audioOnly)];
  }
  final result = <Insight>[];
  if (plan.upscale) {
    result.add(Insight(InsightKind.upscale,
        level: InsightLevel.warning,
        args: [plan.targetLabel, plan.sourceLabel]));
  } else if (plan.downscale) {
    result.add(Insight(InsightKind.downscale,
        args: [plan.sourceLabel, plan.targetLabel]));
  }
  if (media.resolution.height > 0 && media.resolution.height < 720) {
    result.add(Insight(InsightKind.lowResolutionSource,
        args: [plan.sourceLabel]));
  }
  if (media.hdr) {
    result.add(const Insight(InsightKind.hdrToneMap));
  }
  if (!plan.transform && !media.hdr &&
      media.canCopyVideoFor(profile, needsTransform: false)) {
    result.add(const Insight(InsightKind.streamCopy));
  }
  return result;
}
