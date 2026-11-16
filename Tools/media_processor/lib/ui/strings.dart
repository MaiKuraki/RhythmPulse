import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter/widgets.dart';

/// Closed set of localization keys. A typo is a compile-time error; a missing
/// translation is a recoverable, observable degradation.
abstract final class StringKey {
  static const appTitle = 'appTitle';
  static const appSubtitle = 'appSubtitle';
  static const about = 'about';
  static const language = 'language';
  static const version = 'version';
  static const copyright = 'copyright';
  static const ok = 'ok';
  static const selectMediaFile = 'selectMediaFile';
  static const clear = 'clear';
  static const supportedFormats = 'supportedFormats';
  static const noFileSelected = 'noFileSelected';
  static const fileSelectionFailed = 'fileSelectionFailed';
  static const errorOccurred = 'errorOccurred';
  static const selectedFile = 'selectedFile';
  static const operation = 'operation';
  static const configuration = 'configuration';
  static const actions = 'actions';
  static const videoFormat = 'videoFormat';
  static const output4K = 'output4K';
  static const quality = 'quality';
  static const qualitySpeed = 'qualitySpeed';
  static const qualityBalanced = 'qualityBalanced';
  static const qualityQuality = 'qualityQuality';
  static const generateFullMedia = 'generateFullMedia';
  static const generatePreviewMedia = 'generatePreviewMedia';
  static const generatePreview = 'generatePreview';
  static const cancelAllTasks = 'cancelAllTasks';
  static const state = 'state';
  static const taskStatusIdle = 'taskStatusIdle';
  static const taskStatusRunning = 'taskStatusRunning';
  static const taskStatusSuccess = 'taskStatusSuccess';
  static const taskStatusFailed = 'taskStatusFailed';
  static const taskStatusCanceled = 'taskStatusCanceled';
  static const processingPleaseWait = 'processingPleaseWait';
  static const invalidTimeRange = 'invalidTimeRange';
  static const executionLog = 'executionLog';
  static const consoleTitle = 'consoleTitle';
  static const clearLogs = 'clearLogs';
  static const exportLog = 'exportLog';
  static const logLevelAll = 'logLevelAll';
  static const logLevelDebug = 'logLevelDebug';
  static const logLevelInfo = 'logLevelInfo';
  static const logLevelWarning = 'logLevelWarning';
  static const logLevelError = 'logLevelError';
  static const outputs = 'outputs';
  static const mediaPreview = 'mediaPreview';
  static const clipRange = 'clipRange';
  static const rangeStart = 'rangeStart';
  static const rangeEnd = 'rangeEnd';
  static const rangeDuration = 'rangeDuration';
  static const loopSelection = 'loopSelection';
  static const stopLoop = 'stopLoop';
  static const setCurrent = 'setCurrent';
  static const playSelection = 'playSelection';
  static const waveformLoading = 'waveformLoading';
  static const aboutText = 'aboutText';
  static const hostInfo = 'hostInfo';
  static const logExported = 'logExported';
  static const failureInvalidInput = 'failureInvalidInput';
  static const failureBinaryUnavailable = 'failureBinaryUnavailable';
  static const failureProbeFailed = 'failureProbeFailed';
  static const failureEncoderUnavailable = 'failureEncoderUnavailable';
  static const failureProcessFailed = 'failureProcessFailed';
  static const failureTimeout = 'failureTimeout';
  static const failureCancelled = 'failureCancelled';
  static const failureIo = 'failureIo';
  static const failureUnsupported = 'failureUnsupported';
  static const failureBusy = 'failureBusy';
  static const failureInternal = 'failureInternal';
  static const outputFiles = 'outputFiles';
  static const openFolder = 'openFolder';
  static const mediaInfo = 'mediaInfo';
  static const mediaTypeVideo = 'mediaTypeVideo';
  static const mediaTypeAudio = 'mediaTypeAudio';
  static const sourceType = 'sourceType';
  static const sourceResolution = 'sourceResolution';
  static const sourceDuration = 'sourceDuration';
  static const sourceVideoCodec = 'sourceVideoCodec';
  static const sourceAudioCodec = 'sourceAudioCodec';
  static const sourceSize = 'sourceSize';
  static const sourceColor = 'sourceColor';
  static const outputResolution = 'outputResolution';
  static const resolutionFollowSource = 'resolutionFollowSource';
  static const audioQuality = 'audioQuality';
  static const outputHints = 'outputHints';
  static const hintUpscale = 'hintUpscale';
  static const hintDownscale = 'hintDownscale';
  static const hintLowRes = 'hintLowRes';
  static const hintHdr = 'hintHdr';
  static const hintStreamCopy = 'hintStreamCopy';
  static const hintAudioOnly = 'hintAudioOnly';
  static const targetSize = 'targetSize';
  static const emptyStateTitle = 'emptyStateTitle';
  static const emptyStateHint = 'emptyStateHint';

  static const List<String> all = [
    appTitle, appSubtitle, about, language, version, copyright, ok,
    selectMediaFile, clear, supportedFormats, noFileSelected,
    fileSelectionFailed, errorOccurred, selectedFile, operation, configuration,
    actions, videoFormat, output4K, quality, qualitySpeed, qualityBalanced,
    qualityQuality, generateFullMedia, generatePreviewMedia, generatePreview,
    cancelAllTasks, state, taskStatusIdle, taskStatusRunning, taskStatusSuccess,
    taskStatusFailed, taskStatusCanceled, processingPleaseWait,
    invalidTimeRange, executionLog, consoleTitle, clearLogs, exportLog,
    logLevelAll, logLevelDebug, logLevelInfo, logLevelWarning, logLevelError,
    outputs, mediaPreview, clipRange, rangeStart, rangeEnd, rangeDuration,
    loopSelection, stopLoop, setCurrent, playSelection, waveformLoading,
    aboutText, hostInfo, logExported,
    failureInvalidInput, failureBinaryUnavailable, failureProbeFailed,
    failureEncoderUnavailable, failureProcessFailed, failureTimeout,
    failureCancelled, failureIo, failureUnsupported, failureBusy,
    failureInternal, outputFiles, openFolder,
    mediaInfo, mediaTypeVideo, mediaTypeAudio,
    sourceType, sourceResolution, sourceDuration, sourceVideoCodec,
    sourceAudioCodec, sourceSize, sourceColor,
    outputResolution, resolutionFollowSource, audioQuality, outputHints,
    hintUpscale, hintDownscale, hintLowRes, hintHdr, hintStreamCopy,
    hintAudioOnly, targetSize, emptyStateTitle, emptyStateHint,
  ];
}

final class AppStrings {
  AppStrings(this.locale, Map<String, String> values, {AppStrings? fallback})
      : _values = Map<String, String>.unmodifiable(values),
        _fallback = fallback,
        missingKeys = [
          for (final key in StringKey.all)
            if (!values.containsKey(key)) key,
        ],
        extraKeys = [
          for (final key in values.keys)
            if (!StringKey.all.contains(key)) key,
        ];

  final Locale locale;
  final Map<String, String> _values;
  final AppStrings? _fallback;
  final List<String> missingKeys;
  final List<String> extraKeys;
  final Set<String> _degraded = <String>{};

  static const supportedLocales = [Locale('en', 'US'), Locale('zh', 'CN')];
  static const String assetDirectory = 'lib/i18n';

  /// Any locale of a supported language resolves to that language's resource.
  /// `zh_TW`, `en_GB`, `zh` and `zh_Hans_CN` no longer produce a missing asset.
  static Locale normalize(Locale requested) {
    for (final supported in supportedLocales) {
      if (supported.languageCode == requested.languageCode) return supported;
    }
    return supportedLocales.first;
  }

  static String assetFor(Locale locale) {
    final resolved = normalize(locale);
    return '$assetDirectory/'
        '${resolved.languageCode}_${resolved.countryCode}.json';
  }

  static AppStrings? maybeOf(BuildContext context) =>
      Localizations.of<AppStrings>(context, AppStrings);

  static AppStrings of(BuildContext context) {
    final strings = maybeOf(context);
    if (strings == null) {
      throw StateError('AppStrings is not available in this context');
    }
    return strings;
  }

  static Future<AppStrings> load(Locale locale, {AssetBundle? bundle}) async {
    final assets = bundle ?? rootBundle;
    final resolved = normalize(locale);
    final source = await assets.loadString(assetFor(resolved));
    final english = supportedLocales.first;
    final fallbackSource = resolved == english
        ? null
        : await assets.loadString(assetFor(english));
    return fromJson(resolved, source, fallbackSource: fallbackSource);
  }

  static AppStrings fromJson(Locale locale, String source,
      {String? fallbackSource}) {
    final primary = _decode(source, assetFor(locale));
    final fallback = fallbackSource == null
        ? null
        : AppStrings(supportedLocales.first,
            _decode(fallbackSource, assetFor(supportedLocales.first)));
    return AppStrings(locale, primary, fallback: fallback);
  }

  static Map<String, String> _decode(String source, String assetName) {
    final Object? decoded;
    try {
      decoded = json.decode(source);
    } catch (e) {
      throw FormatException('Localization resource is not valid JSON: '
          '$assetName', e);
    }
    if (decoded is! Map) {
      throw FormatException('Localization resource must be a JSON object: '
          '$assetName');
    }
    final result = <String, String>{};
    for (final entry in decoded.entries) {
      final value = entry.value;
      if (value is! String) {
        throw FormatException('Localization value must be a string: '
            '$assetName -> ${entry.key}');
      }
      result[entry.key.toString()] = value;
    }
    return result;
  }

  /// Keys that resolved through the English fallback on this instance.
  Set<String> get degradedKeys => Set<String>.unmodifiable(_degraded);

  String get(String key, [List<String> arguments = const []]) {
    var template = _values[key];
    if (template == null) {
      template = _fallback?._values[key];
      if (template != null) _degraded.add(key);
    }
    if (template == null) {
      throw StateError('Unknown localization key "$key" '
          'for ${locale.toLanguageTag()}');
    }
    final parts = template.split('%s');
    if (parts.length == 1) return template;
    final buffer = StringBuffer(parts.first);
    for (var i = 1; i < parts.length; i++) {
      buffer.write(i <= arguments.length ? arguments[i - 1] : '');
      buffer.write(parts[i]);
    }
    return buffer.toString();
  }

  /// Structural validation used by verification tests. Returns the problems
  /// instead of aborting application startup.
  List<String> validate({String? fallbackSource}) {
    final problems = <String>[
      for (final key in missingKeys) 'missing:$key',
      for (final key in extraKeys) 'extra:$key',
    ];
    if (fallbackSource != null) {
      final reference = _decode(fallbackSource, assetFor(supportedLocales.first));
      for (final key in StringKey.all) {
        final primary = _values[key];
        final fallback = reference[key];
        if (primary == null || fallback == null) continue;
        if (_placeholders(primary) != _placeholders(fallback)) {
          problems.add('placeholder-mismatch:$key');
        }
      }
    }
    return problems;
  }

  static int _placeholders(String value) => '%s'.allMatches(value).length;
}

final class AppStringsDelegate extends LocalizationsDelegate<AppStrings> {
  const AppStringsDelegate({this.bundle});

  /// Injectable for widget tests that provide an in-memory asset bundle.
  final AssetBundle? bundle;

  @override
  bool isSupported(Locale locale) => AppStrings.supportedLocales
      .any((l) => l.languageCode == locale.languageCode);

  @override
  Future<AppStrings> load(Locale locale) =>
      AppStrings.load(locale, bundle: bundle);

  @override
  bool shouldReload(covariant AppStringsDelegate old) =>
      old.bundle != bundle;
}
