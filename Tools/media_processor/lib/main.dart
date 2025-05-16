import 'dart:async';
import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_gen/gen_l10n/app_localizations.dart';
import 'package:path/path.dart' as path;
import 'utils/ffmpeg_helper.dart';

/// Main application entry point
void main() async {
  // Handle process termination signals (Ctrl+C)
  ProcessSignal.sigint.watch().listen((signal) {
    FfmpegProcessManager().killAll().then((_) {
      exit(0);
    });
  });

  //  Check FFMPEG and FFPROBE availability
  if (kDebugMode) {
    try {
      final ffmpegPath = FFmpegHelper.getBundledFfmpegPath();
      print('Expected ffmpeg path: $ffmpegPath');
      final exists = await File(ffmpegPath).exists();
      print('ffmpeg exists: $exists');
      if (exists && !Platform.isWindows) {
        final stat = await File(ffmpegPath).stat();
        print('ffmpeg permissions: ${stat.mode}');
      }
    } catch (e) {
      print('Error checking ffmpeg: $e');
    }

    try {
      final ffprobePath = FFmpegHelper.getBundledFfprobePath();
      print('Expected ffprobe path: $ffprobePath');
      final exists = await File(ffprobePath).exists();
      print('ffprobe exists: $exists');
      if (exists && !Platform.isWindows) {
        final stat = await File(ffprobePath).stat();
        print('ffprobe permissions: ${stat.mode}');
      }
    } catch (e) {
      print('Error checking ffprobe: $e');
    }
  }

  // Initialize and run the application
  runApp(const MediaProcessingMasterApp());
}

/// Root application widget configuring MaterialApp with internationalization support
class MediaProcessingMasterApp extends StatelessWidget {
  const MediaProcessingMasterApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      title: 'Media Processor',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        primarySwatch: Colors.deepPurple,
        brightness: Brightness.light,
        useMaterial3: true,
        elevatedButtonTheme: ElevatedButtonThemeData(
          style: ElevatedButton.styleFrom(
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
            padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 28),
            textStyle: const TextStyle(
              fontSize: 16,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
      ),
      localizationsDelegates: S.localizationsDelegates,
      supportedLocales: S.supportedLocales,
      home: const MediaProcessingHomePage(),
    );
  }
}

/// Enumeration representing possible states of media processing tasks
enum TaskStatus { idle, running, success, failed, canceled }

/// Primary application screen containing media processing functionality
class MediaProcessingHomePage extends StatefulWidget {
  const MediaProcessingHomePage({super.key});

  @override
  State<MediaProcessingHomePage> createState() =>
      _MediaProcessingHomePageState();
}

/// State management for the media processing home page
class _MediaProcessingHomePageState extends State<MediaProcessingHomePage>
    with WidgetsBindingObserver {
  String? _selectedFilePath;
  List<String> _outputFiles = [];
  String _log = '';
  bool _isProcessing = false;
  TaskStatus _taskStatus = TaskStatus.idle;
  Completer<void>? _currentTaskCompleter;

  final ScrollController _logScrollController = ScrollController();

  /// Supported media file extensions for processing
  final List<String> _allowedExtensions = [
    'mp4',
    'avi',
    'mkv',
    'mov',
    'flv',
    'wmv',
    'webm',
    'mp3',
    'wav',
    'ogg',
    'aac',
  ];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    // Clean up resources and observers
    WidgetsBinding.instance.removeObserver(this);
    _scrollDebounce?.cancel();
    _logScrollController.dispose();
    _cancelCurrentTask();
    super.dispose();
  }

  /// Terminates the current processing task and cleans up resources
  Future<void> _cancelCurrentTask() async {
    if (_isProcessing) {
      if (kDebugMode) {
        print('User requested cancellation');
      }
      setState(() {
        _taskStatus = TaskStatus.canceled;
        _isProcessing = false;
        _log += '\n${S.of(context)?.taskCanceled ?? 'Task canceled by user'}';
      });
      _scrollLogToBottom();

      // Complete the current task completer and terminate FFmpeg processes
      _currentTaskCompleter?.complete();
      await FfmpegProcessManager().killAll();
      _currentTaskCompleter = null;

      // Remove incomplete output files
      for (final filePath in _outputFiles) {
        final file = File(filePath);
        if (await file.exists()) {
          try {
            await file.delete();
            if (kDebugMode) {
              print('Deleted incomplete file: $filePath');
            }
          } catch (e) {
            if (kDebugMode) {
              print('Error deleting file $filePath: $e');
            }
          }
        }
      }
      _outputFiles.clear();
    }
  }

  /// Opens a file picker dialog to select media files
  Future<void> _pickMediaFile() async {
    setState(() {
      _log = '';
      _taskStatus = TaskStatus.idle;
    });
    try {
      final result = await FilePicker.platform.pickFiles(
        type: FileType.custom,
        allowedExtensions: _allowedExtensions,
      );
      if (result != null && result.files.isNotEmpty) {
        final path = result.files.single.path;
        if (path != null) {
          setState(() {
            _selectedFilePath = path;
            _log = '${S.of(context)?.selectedFile ?? 'Selected File:'}\n$path';
            _taskStatus = TaskStatus.idle;
          });
          _scrollLogToBottom();
        }
      }
    } catch (e) {
      setState(() {
        _log =
            S.of(context)?.fileSelectionFailed(e.toString()) ??
            'File selection failed: $e';
        _taskStatus = TaskStatus.failed;
      });
      _scrollLogToBottom();
    }
  }

  /// Clears the currently selected file
  void _clearSelectedFile() {
    if (_isProcessing) return;
    setState(() {
      _selectedFilePath = null;
      _log = '';
      _taskStatus = TaskStatus.idle;
    });
  }

  /// Generates a unique file path by appending a counter if the file exists
  String generateUniqueFilePath(String originalPath) {
    var file = File(originalPath);
    if (!file.existsSync()) {
      return originalPath;
    }

    final dir = file.parent.path;
    final ext = path.extension(originalPath);
    final baseName = path.basenameWithoutExtension(originalPath);

    int counter = 1;
    String newPath;

    do {
      newPath = path.join(dir, '$baseName($counter)$ext');
      counter++;
    } while (File(newPath).existsSync());

    return newPath;
  }

  /// Executes the audio/video separation process
  Future<void> _splitAudioVideo() async {
    if (_selectedFilePath == null) {
      setState(() {
        _log =
            S.of(context)?.noFileSelected ??
            'Please select a media file first.';
        _taskStatus = TaskStatus.failed;
      });
      _scrollLogToBottom();
      return;
    }

    setState(() {
      _isProcessing = true;
      _taskStatus = TaskStatus.running;
      _log =
          S.of(context)?.processingPleaseWait ?? 'Processing, please wait...';
    });
    _scrollLogToBottom();

    _currentTaskCompleter = Completer<void>();

    try {
      final hasFfprobe = await FFmpegHelper.verifyBundledFfprobe();
      if (!hasFfprobe) {
        throw Exception(
          'Bundled ffprobe not found. Please ensure FFmpeg binaries are properly installed.',
        );
      }
      final inputFile = File(_selectedFilePath!);
      final dir = inputFile.parent.path.replaceAll('\\', '/');
      final baseName = inputFile.uri.pathSegments.last.split('.').first;

      // Generate initial and unique output paths
      final outputVideoPathInitial = '$dir/${baseName}_video_only.mp4';
      final outputAudioPathInitial = '$dir/${baseName}_audio.ogg';

      final outputVideoPath = generateUniqueFilePath(outputVideoPathInitial);
      final outputAudioPath = generateUniqueFilePath(outputAudioPathInitial);

      // Track output file paths for cleanup
      _outputFiles = [outputVideoPath, outputAudioPath];

      // Prepare localized strings for FFmpeg commands
      final localizedStringsMap = {
        'videoSplitCmd': S.of(context)!.videoSplitCmd(''),
        'videoSplitSuccess': S.of(context)!.videoSplitSuccess(''),
        'videoSplitFailed': S.of(context)!.videoSplitFailed(''),
        'audioSplitCmd': S.of(context)!.audioSplitCmd(''),
        'audioSplitSuccess': S.of(context)!.audioSplitSuccess(''),
        'audioSplitFailed': S.of(context)!.audioSplitFailed(''),
      };

      // Execute the FFmpeg processing
      final log = await FFmpegHelper.splitAudioVideo(
        inputPath: _selectedFilePath!,
        outputVideoPath: outputVideoPath,
        outputAudioPath: outputAudioPath,
        localizedStrings: localizedStringsMap,
        onLog: (partialLog) {
          if (_currentTaskCompleter?.isCompleted == false) {
            setState(() {
              _log = partialLog;
            });
            _scrollLogToBottom();
          }
        },
        cancelToken: _currentTaskCompleter?.future,
      );

      // Update status based on processing outcome
      if (_taskStatus != TaskStatus.canceled) {
        setState(() {
          _log = log;
          if (log.contains('❌')) {
            _taskStatus = TaskStatus.failed;
          } else {
            _taskStatus = TaskStatus.success;
          }
        });
        _scrollLogToBottom();
      }
    } catch (e) {
      if (_taskStatus != TaskStatus.canceled) {
        setState(() {
          _log =
              S.of(context)?.errorOccurred(e.toString()) ??
              'An error occurred: $e';
          _taskStatus = TaskStatus.failed;
        });
        _scrollLogToBottom();
      }
    } finally {
      await FfmpegProcessManager().killAll();
      if (_taskStatus != TaskStatus.canceled && mounted) {
        setState(() {
          _isProcessing = false;
        });
      }
      _currentTaskCompleter = null;
    }
  }

  Timer? _scrollDebounce;

  /// Scrolls the log output to the bottom with debouncing
  void _scrollLogToBottom() {
    _scrollDebounce?.cancel();
    _scrollDebounce = Timer(const Duration(milliseconds: 100), () {
      if (_logScrollController.hasClients) {
        _logScrollController.jumpTo(
          _logScrollController.position.maxScrollExtent,
        );
      }
    });
  }

  /// Builds the file selection UI component
  Widget _buildFileSelector() {
    final formats = _allowedExtensions.join(',    ');
    return Card(
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      margin: const EdgeInsets.symmetric(vertical: 12),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 20, horizontal: 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              S.of(context)?.selectMediaFile ?? 'Select Media File',
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.bold,
                color: Colors.deepPurple,
              ),
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(
                      vertical: 12,
                      horizontal: 16,
                    ),
                    decoration: BoxDecoration(
                      color: Colors.deepPurple.shade50,
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(
                        color:
                            _selectedFilePath != null
                                ? Colors.deepPurple
                                : Colors.grey.shade400,
                      ),
                    ),
                    child: Text(
                      _selectedFilePath ??
                          S.of(context)?.noFileSelected ??
                          'Please select a media file first.',
                      style: TextStyle(
                        fontSize: 14,
                        color:
                            _selectedFilePath != null
                                ? Colors.black87
                                : Colors.grey.shade600,
                        overflow: TextOverflow.ellipsis,
                      ),
                      maxLines: 2,
                    ),
                  ),
                ),
                const SizedBox(width: 12),
                ElevatedButton.icon(
                  icon: const Icon(Icons.folder_open),
                  label: Text(
                    S.of(context)?.selectMediaFile ?? 'Select Media File',
                  ),
                  onPressed: _isProcessing ? null : _pickMediaFile,
                ),
                const SizedBox(width: 8),
                if (_selectedFilePath != null)
                  ElevatedButton.icon(
                    style: ElevatedButton.styleFrom(
                      backgroundColor: Colors.redAccent,
                      foregroundColor: Colors.white,
                    ),
                    icon: const Icon(Icons.clear),
                    label: Text(S.of(context)?.clear ?? 'Clear'),
                    onPressed: _isProcessing ? null : _clearSelectedFile,
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              S.of(context)?.supportedFormats(formats) ??
                  'Supported formats: $formats',
              style: TextStyle(fontSize: 12, color: Colors.grey.shade600),
            ),
          ],
        ),
      ),
    );
  }

  /// Builds the operation control UI component
  Widget _buildOperationCard() {
    String statusText;
    Color statusColor;
    IconData? statusIcon;

    // Determine status display properties
    switch (_taskStatus) {
      case TaskStatus.running:
        statusText = S.of(context)?.taskStatusRunning ?? 'Running';
        statusColor = Colors.blue;
        break;
      case TaskStatus.success:
        statusText = S.of(context)?.taskStatusSuccess ?? 'Success';
        statusColor = Colors.green;
        statusIcon = Icons.check_circle;
        break;
      case TaskStatus.failed:
        statusText = S.of(context)?.taskStatusFailed ?? 'Failed';
        statusColor = Colors.red;
        statusIcon = Icons.error;
        break;
      case TaskStatus.canceled:
        statusText = S.of(context)?.taskStatusCanceled ?? 'Canceled';
        statusColor = Colors.orange;
        statusIcon = Icons.warning;
        break;
      case TaskStatus.idle:
        statusText = S.of(context)?.taskStatusIdle ?? 'Idle';
        statusColor = Colors.grey;
        break;
    }

    return Card(
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      margin: const EdgeInsets.symmetric(vertical: 12),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 20, horizontal: 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              S.of(context)?.operation ?? 'Operation',
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.bold,
                color: Colors.deepPurple,
              ),
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                Expanded(
                  child: ElevatedButton.icon(
                    icon: const Icon(Icons.content_cut),
                    label: Text(
                      S.of(context)?.splitAudioVideo ?? 'Split Audio and Video',
                    ),
                    onPressed:
                        (_selectedFilePath == null || _isProcessing)
                            ? null
                            : _splitAudioVideo,
                    style: ElevatedButton.styleFrom(
                      minimumSize: const Size.fromHeight(48),
                      backgroundColor: Colors.deepPurple,
                      foregroundColor: Colors.white,
                      disabledBackgroundColor: Colors.deepPurple.shade100,
                      disabledForegroundColor: Colors.deepPurple,
                    ),
                  ),
                ),
                if (_isProcessing) ...[
                  const SizedBox(width: 16),
                  Expanded(
                    child: ElevatedButton.icon(
                      icon: const Icon(Icons.cancel),
                      label: Text(S.of(context)?.cancelTask ?? 'Cancel Task'),
                      onPressed: _cancelCurrentTask,
                      style: ElevatedButton.styleFrom(
                        minimumSize: const Size.fromHeight(48),
                        backgroundColor: Colors.redAccent,
                        foregroundColor: Colors.white,
                      ),
                    ),
                  ),
                ],
              ],
            ),
            if (_isProcessing || _taskStatus != TaskStatus.idle)
              Padding(
                padding: const EdgeInsets.only(top: 16),
                child: Row(
                  children: [
                    if (_taskStatus == TaskStatus.running)
                      const CircularProgressIndicator()
                    else if (statusIcon != null)
                      Icon(statusIcon, color: statusColor),
                    const SizedBox(width: 16),
                    Text(
                      _isProcessing
                          ? S.of(context)?.processingPleaseWait ??
                              'Processing, please wait...'
                          : statusText,
                      style: TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w500,
                        color: statusColor,
                      ),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }

  /// Builds the log output display component
  Widget _buildLogOutput() {
    return Card(
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      margin: const EdgeInsets.symmetric(vertical: 12),
      child: Container(
        constraints: const BoxConstraints(minHeight: 150, maxHeight: 150),
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              S.of(context)?.executionLog ?? 'Execution Log',
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.bold,
                color: Colors.deepPurple,
              ),
            ),
            const SizedBox(height: 12),
            Expanded(
              child: Container(
                width: double.infinity,
                decoration: BoxDecoration(
                  color: Colors.black87,
                  borderRadius: BorderRadius.circular(12),
                ),
                padding: const EdgeInsets.all(12),
                child: Scrollbar(
                  thumbVisibility: true,
                  controller: _logScrollController,
                  child: SingleChildScrollView(
                    controller: _logScrollController,
                    child: SelectableText(
                      _log,
                      style: const TextStyle(
                        color: Colors.greenAccent,
                        fontFamily: 'Courier',
                        fontSize: 14,
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  /// Builds the application footer component
  Widget _buildFooter() {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Center(
        child: Text(
          S.of(context)?.copyright ??
              '© 2025 CycloneGames. All rights reserved.',
          style: TextStyle(color: Colors.grey.shade600, fontSize: 12),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        centerTitle: true,
        elevation: 6,
        shadowColor: Colors.deepPurple.shade200,
        flexibleSpace: Container(
          decoration: BoxDecoration(
            gradient: LinearGradient(
              colors: [Colors.deepPurple.shade700, Colors.deepPurple.shade400],
              begin: Alignment.topLeft,
              end: Alignment.bottomRight,
            ),
          ),
        ),
        title: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              S.of(context)?.appTitle ?? 'Media Processor',
              style: const TextStyle(
                fontWeight: FontWeight.bold,
                fontSize: 24,
                letterSpacing: 1.2,
                color: Colors.white,
                shadows: [
                  Shadow(
                    color: Colors.black26,
                    offset: Offset(1, 1),
                    blurRadius: 2,
                  ),
                ],
              ),
            ),
            const SizedBox(height: 4),
            Text(
              S.of(context)?.appSubtitle ?? 'Audio & Video Processing Tool',
              style: TextStyle(
                fontSize: 12,
                color: Colors.white70,
                fontWeight: FontWeight.w400,
              ),
            ),
          ],
        ),
        actions: [
          IconButton(
            icon: const Icon(Icons.info_outline),
            tooltip: S.of(context)?.about ?? 'About',
            onPressed: () {
              showAboutDialog(
                context: context,
                applicationName: S.of(context)?.appTitle ?? 'Media Processor',
                applicationVersion: 'v1.0.0',
                applicationLegalese:
                    S.of(context)?.copyright ??
                    '© 2025 CycloneGames. All rights reserved.',
              );
            },
          ),
        ],
      ),

      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 12),
          child: LayoutBuilder(
            builder: (context, constraints) {
              return SingleChildScrollView(
                child: ConstrainedBox(
                  constraints: BoxConstraints(minHeight: constraints.maxHeight),
                  child: IntrinsicHeight(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        _buildFileSelector(),
                        _buildOperationCard(),
                        Expanded(child: _buildLogOutput()),
                        _buildFooter(),
                      ],
                    ),
                  ),
                ),
              );
            },
          ),
        ),
      ),
    );
  }
}
