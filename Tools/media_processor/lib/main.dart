import 'dart:async';
import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:localization/localization.dart';
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
    LocalJsonLocalization.delegate.directories = ['lib/i18n'];
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
      supportedLocales: const [Locale('en', 'US'), Locale('zh', 'CN')],
      localizationsDelegates: [
        LocalJsonLocalization.delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
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
  bool _videoOutputApply4K = false;
  bool _showPreviewOptions = false;
  final TextEditingController _startTimeController = TextEditingController();
  final TextEditingController _endTimeController = TextEditingController();

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
    'm4a',
    'flac',
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
    _startTimeController.dispose();
    _endTimeController.dispose();
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
        _log += '\n${'taskCanceled'.i18n()}';
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
            _log = '${'selectedFile'.i18n()}\n$path';
            _taskStatus = TaskStatus.idle;
          });
          _scrollLogToBottom();
        }
      }
    } catch (e) {
      setState(() {
        _log = 'fileSelectionFailed'.i18n(['$e']);
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
  Future<void> _generateFullMedia() async {
    if (_selectedFilePath == null) {
      setState(() {
        _log = 'noFileSelected'.i18n();
        _taskStatus = TaskStatus.failed;
      });
      _scrollLogToBottom();
      return;
    }

    setState(() {
      _isProcessing = true;
      _taskStatus = TaskStatus.running;
      _log = 'processingPleaseWait'.i18n();
    });
    _scrollLogToBottom();

    _currentTaskCompleter = Completer<void>();

    try {
      final inputFile = File(_selectedFilePath!);
      final dir = inputFile.parent.path.replaceAll('\\', '/');
      final baseName = inputFile.uri.pathSegments.last.split('.').first;

      // Generate output paths
      final outputVideoPath = '$dir/${baseName}_video_only.mp4';
      final outputAudioPath = '$dir/${baseName}_audio.ogg';

      // Track output files
      _outputFiles = [outputAudioPath];

      // Only add video path if it's actually a video file
      final mediaType = await FFmpegHelper.detectMediaType(_selectedFilePath!);
      if (mediaType == MediaType.video) {
        _outputFiles.add(outputVideoPath);
      }

      final localizedStringsMap = {
        'videoSplitCmd': 'videoSplitCmd'.i18n(['%s']),
        'audioSplitCmd': 'audioSplitCmd'.i18n(['%s']),
        'videoSplitSuccess': 'videoSplitSuccess'.i18n(['%s']),
        'audioSplitSuccess': 'audioSplitSuccess'.i18n(['%s']),
        'videoSplitFailed': 'videoSplitFailed'.i18n(['%s']),
        'audioSplitFailed': 'audioSplitFailed'.i18n(['%s']),
      };

      final log = await FFmpegHelper.generateFullMedia(
        inputPath: _selectedFilePath!,
        outputVideoPath: mediaType == MediaType.video ? outputVideoPath : null,
        outputAudioPath: outputAudioPath,
        localizedStrings: localizedStringsMap,
        apply4K: _videoOutputApply4K,
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

      if (_taskStatus != TaskStatus.canceled) {
        setState(() {
          _log = log;
          _taskStatus =
              log.contains('❌') ? TaskStatus.failed : TaskStatus.success;
        });
        _scrollLogToBottom();
      }
    } catch (e) {
      if (_taskStatus != TaskStatus.canceled) {
        setState(() {
          _log = 'errorOccurred'.i18n(['$e']);
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

  /// Generate preview based on current settings
  Future<void> _generatePreview() async {
    if (_selectedFilePath == null) {
      setState(() {
        _log = 'noFileSelected'.i18n();
        _taskStatus = TaskStatus.failed;
      });
      _scrollLogToBottom();
      return;
    }

    // Validate time inputs
    final startMs = int.tryParse(_startTimeController.text) ?? 0;
    final endMs = int.tryParse(_endTimeController.text) ?? 0;

    if (startMs < 0 || endMs <= startMs) {
      setState(() {
        _log = 'invalidTimeRange'.i18n();
        _taskStatus = TaskStatus.failed;
      });
      _scrollLogToBottom();
      return;
    }

    setState(() {
      _isProcessing = true;
      _taskStatus = TaskStatus.running;
      _log = 'generatingPreview'.i18n();
    });
    _scrollLogToBottom();

    _currentTaskCompleter = Completer<void>();

    try {
      final inputFile = File(_selectedFilePath!);
      final dir = inputFile.parent.path.replaceAll('\\', '/');
      final baseName = inputFile.uri.pathSegments.last.split('.').first;

      // Check if input is video (by extension)
      final isVideo = _allowedExtensions
          .where(
            (ext) =>
                ext != 'mp3' && ext != 'wav' && ext != 'ogg' && ext != 'aac',
          )
          .any((ext) => _selectedFilePath!.toLowerCase().endsWith('.$ext'));

      // Generate output path
      final outputPath = generateUniqueFilePath(
        '$dir/${baseName}_preview.${isVideo ? 'mp4' : 'ogg'}',
      );

      // Track output file for cleanup
      _outputFiles = [outputPath];

      // Prepare localized strings
      final localizedStringsMap = {
        'previewCmd': 'previewCmd'.i18n(['%s']),
        'previewVideoSplitSuccess': 'previewVideoSplitSuccess'.i18n(['%s']),
        'previewAudioSplitSuccess': 'previewAudioSplitSuccess'.i18n(['%s']),
        'previewVideoSplitFailed': 'previewVideoSplitFailed'.i18n(['%s']),
        'previewAudioSplitFailed': 'previewAudioSplitFailed'.i18n(['%s']),
      };

      final outputVideoPath = '$dir/${baseName}_preview.mp4';
      final outputAudioPath = '$dir/${baseName}_preview.ogg';

      final log = await FFmpegHelper.generatePreview(
        inputPath: _selectedFilePath!,
        outputVideoPath: isVideo ? outputVideoPath : null,
        outputAudioPath: outputAudioPath,
        startMs: startMs,
        endMs: endMs,
        localizedStrings: localizedStringsMap,
        onLog: (partialLog) {
          // 处理日志
        },
        cancelToken: _currentTaskCompleter?.future,
      );

      // Update status
      if (_taskStatus != TaskStatus.canceled) {
        setState(() {
          _log = log;
          _taskStatus =
              log.contains('❌') ? TaskStatus.failed : TaskStatus.success;
        });
        _scrollLogToBottom();
      }
    } catch (e) {
      if (_taskStatus != TaskStatus.canceled) {
        setState(() {
          _log = 'errorOccurred'.i18n(['$e']);
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

  /// Toggles preview options visibility
  void _togglePreviewOptions() {
    setState(() {
      _showPreviewOptions = !_showPreviewOptions;
    });
  }

  /// Builds the preview options UI with improved layout
  Widget _buildPreviewOptions() {
    return AnimatedContainer(
      duration: const Duration(milliseconds: 300),
      curve: Curves.easeInOut,
      height: _showPreviewOptions ? 180 : 0,
      padding: _showPreviewOptions ? const EdgeInsets.all(16) : EdgeInsets.zero,
      decoration: BoxDecoration(
        color: Colors.deepPurple.shade50,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: Colors.deepPurple.shade100),
      ),
      child:
          _showPreviewOptions
              ? Column(
                children: [
                  // Time range inputs
                  Row(
                    children: [
                      Expanded(
                        child: TextField(
                          controller: _startTimeController,
                          keyboardType: TextInputType.number,
                          decoration: InputDecoration(
                            labelText: 'startTimeMs'.i18n(),
                            border: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(8),
                            ),
                          ),
                        ),
                      ),
                      const SizedBox(width: 16),
                      Expanded(
                        child: TextField(
                          controller: _endTimeController,
                          keyboardType: TextInputType.number,
                          decoration: InputDecoration(
                            labelText: 'endTimeMs'.i18n(),
                            border: OutlineInputBorder(
                              borderRadius: BorderRadius.circular(8),
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 16),

                  // Generate Preview button
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton.icon(
                      icon: const Icon(Icons.play_arrow),
                      label: Text('generatePreview'.i18n()),
                      onPressed: _isProcessing ? null : _generatePreview,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.deepPurple,
                        foregroundColor: Colors.white,
                      ),
                    ),
                  ),
                ],
              )
              : null,
    );
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
    final formats = _allowedExtensions.join(',\t');
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
              'selectMediaFile'.i18n(),
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
                      _selectedFilePath ?? 'noFileSelected'.i18n(),
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
                  label: Text('selectMediaFile'.i18n()),
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
                    label: Text('clear'.i18n()),
                    onPressed: _isProcessing ? null : _clearSelectedFile,
                  ),
              ],
            ),
            const SizedBox(height: 8),
            Text(
              'supportedFormats'.i18n([formats]),
              style: TextStyle(fontSize: 12, color: Colors.grey.shade600),
            ),
          ],
        ),
      ),
    );
  }

  /// Builds the operation control UI component with improved layout
  Widget _buildOperationCard() {
    String statusText;
    Color statusColor;
    IconData? statusIcon;

    // Determine status display properties
    switch (_taskStatus) {
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
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      margin: const EdgeInsets.symmetric(vertical: 12),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 20, horizontal: 24),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'operation'.i18n(),
              style: const TextStyle(
                fontSize: 20,
                fontWeight: FontWeight.bold,
                color: Colors.deepPurple,
              ),
            ),

            // Resolution Toggle
            const SizedBox(height: 16),
            _buildResolutionToggle(),
            const SizedBox(height: 16),

            // Main operation buttons in a row
            Row(
              children: [
                // Split Audio/Video button
                Expanded(
                  child: ElevatedButton.icon(
                    icon: const Icon(Icons.video_library),
                    label: Text('generateFullMedia'.i18n()),
                    onPressed:
                        (_selectedFilePath == null || _isProcessing)
                            ? null
                            : _generateFullMedia,
                    style: ElevatedButton.styleFrom(
                      minimumSize: const Size.fromHeight(48),
                      backgroundColor: Colors.deepPurple,
                      foregroundColor: Colors.white,
                      disabledBackgroundColor: Colors.deepPurple.shade100,
                      disabledForegroundColor: Colors.deepPurple,
                    ),
                  ),
                ),
                const SizedBox(width: 16),

                // Preview Options toggle button
                Expanded(
                  child: ElevatedButton.icon(
                    icon: Icon(
                      _showPreviewOptions
                          ? Icons.expand_less
                          : Icons.expand_more,
                    ),
                    label: Text('generatePreviewMedia'.i18n()),
                    onPressed:
                        (_selectedFilePath == null || _isProcessing)
                            ? null
                            : _togglePreviewOptions,
                    style: ElevatedButton.styleFrom(
                      minimumSize: const Size.fromHeight(48),
                      backgroundColor:
                          _selectedFilePath == null || _isProcessing
                              ? Colors.deepPurple.shade100
                              : Colors.deepPurple.shade100,
                      foregroundColor:
                          _selectedFilePath == null || _isProcessing
                              ? Colors.deepPurple
                              : Colors.deepPurple,
                      disabledBackgroundColor: Colors.deepPurple.shade100,
                      disabledForegroundColor: Colors.deepPurple,
                    ),
                  ),
                ),
              ],
            ),

            // Preview options section
            const SizedBox(height: 16),
            _buildPreviewOptions(),

            // Cancel all button (only shown when processing)
            if (_isProcessing) ...[
              const SizedBox(height: 16),
              SizedBox(
                width: double.infinity,
                child: ElevatedButton.icon(
                  icon: const Icon(Icons.cancel),
                  label: Text('cancelAllTasks'.i18n()),
                  onPressed: _cancelCurrentTask,
                  style: ElevatedButton.styleFrom(
                    minimumSize: const Size.fromHeight(48),
                    backgroundColor: Colors.redAccent,
                    foregroundColor: Colors.white,
                  ),
                ),
              ),
            ],

            // Status indicator
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
                          ? 'processingPleaseWait'.i18n()
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

  /// Builds the resolution toggle switch
  Widget _buildResolutionToggle() {
    return Row(
      children: [
        Switch(
          value: _videoOutputApply4K,
          onChanged:
              _isProcessing
                  ? null
                  : (value) {
                    setState(() {
                      _videoOutputApply4K = value;
                    });
                  },
          activeColor: Colors.deepPurple,
        ),
        const SizedBox(width: 8),
        Text(
          'output4K'.i18n(),
          style: TextStyle(
            fontSize: 14,
            color: _isProcessing ? Colors.grey : Colors.black87,
          ),
        ),
      ],
    );
  }

  /// Builds the log output display component
  Widget _buildLogOutput() {
    return Card(
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      margin: const EdgeInsets.symmetric(vertical: 12),
      child: Container(
        constraints: const BoxConstraints(minHeight: 200, maxHeight: 200),
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: Colors.white,
          borderRadius: BorderRadius.circular(16),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'executionLog'.i18n(),
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
          'copyright'.i18n(),
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
              'appTitle'.i18n(),
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
              'appSubtitle'.i18n(),
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
            tooltip: 'about'.i18n(),
            onPressed: () {
              showAboutDialog(
                context: context,
                applicationName: 'appTitle'.i18n(),
                applicationVersion: 'v1.0.0',
                applicationLegalese: 'copyright'.i18n(),
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
