import 'dart:async';
import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_gen/gen_l10n/app_localizations.dart';
import 'package:path/path.dart' as path;
import 'utils/ffmpeg_helper.dart';

void main() async {
  ProcessSignal.sigint.watch().listen((signal) {
    FfmpegProcessManager().killAll().then((_) {
      exit(0);
    });
  });
  runApp(const MediaProcessingMasterApp());
}

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

enum TaskStatus { idle, running, success, failed }

class MediaProcessingHomePage extends StatefulWidget {
  const MediaProcessingHomePage({super.key});

  @override
  State<MediaProcessingHomePage> createState() =>
      _MediaProcessingHomePageState();
}

class _MediaProcessingHomePageState extends State<MediaProcessingHomePage>
    with WidgetsBindingObserver {
  String? _selectedFilePath;
  String _log = '';
  bool _isProcessing = false;
  TaskStatus _taskStatus = TaskStatus.idle;

  final ScrollController _logScrollController = ScrollController();

  final List<String> _allowedExtensions = [
    'mp4',
    'avi',
    'mkv',
    'mov',
    'flv',
    'wmv',
    'mp3',
    'wav',
    'aac',
    'ogg',
  ];

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _scrollDebounce?.cancel();
    _logScrollController.dispose();
    // 页面销毁时杀死所有 FFmpeg 进程，防止残留
    FfmpegProcessManager().killAll();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.detached ||
        state == AppLifecycleState.inactive) {
      // 应用即将退出或进入后台，杀死所有 FFmpeg 进程
      FfmpegProcessManager().killAll();
    }
  }

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

  void _clearSelectedFile() {
    if (_isProcessing) return;
    setState(() {
      _selectedFilePath = null;
      _log = '';
      _taskStatus = TaskStatus.idle;
    });
  }

  /// 生成一个不重复的文件路径，如果存在则自动添加数字后缀
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

    try {
      final inputFile = File(_selectedFilePath!);
      final dir = inputFile.parent.path.replaceAll('\\', '/');
      final baseName = inputFile.uri.pathSegments.last.split('.').first;

      // 生成唯一文件路径，避免覆盖
      final outputVideoPathInitial = '$dir/${baseName}_video_only.mp4';
      final outputAudioPathInitial = '$dir/${baseName}_audio.ogg';

      final outputVideoPath = generateUniqueFilePath(outputVideoPathInitial);
      final outputAudioPath = generateUniqueFilePath(outputAudioPathInitial);

      final localizedStringsMap = {
        'videoSplitCmd': S.of(context)!.videoSplitCmd(''),
        'videoSplitSuccess': S.of(context)!.videoSplitSuccess(''),
        'videoSplitFailed': S.of(context)!.videoSplitFailed(''),
        'audioSplitCmd': S.of(context)!.audioSplitCmd(''),
        'audioSplitSuccess': S.of(context)!.audioSplitSuccess(''),
        'audioSplitFailed': S.of(context)!.audioSplitFailed(''),
      };

      final log = await FFmpegHelper.splitAudioVideo(
        inputPath: _selectedFilePath!,
        outputVideoPath: outputVideoPath,
        outputAudioPath: outputAudioPath,
        localizedStrings: localizedStringsMap,
        onLog: (partialLog) {
          setState(() {
            _log = partialLog;
          });
          _scrollLogToBottom();
        },
      );

      setState(() {
        _log = log;
        if (log.contains('❌')) {
          _taskStatus = TaskStatus.failed;
        } else {
          _taskStatus = TaskStatus.success;
        }
      });
      _scrollLogToBottom();
    } catch (e) {
      setState(() {
        _log =
            S.of(context)?.errorOccurred(e.toString()) ??
            'An error occurred: $e';
        _taskStatus = TaskStatus.failed;
      });
      _scrollLogToBottom();
    } finally {
      setState(() {
        _isProcessing = false;
      });
    }
  }

  Timer? _scrollDebounce;

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

  Widget _buildFileSelector() {
    final formats = _allowedExtensions.join(',   ');
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

  Widget _buildActionButtons() {
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
            ElevatedButton.icon(
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
            if (_isProcessing)
              Padding(
                padding: const EdgeInsets.only(top: 16),
                child: Row(
                  children: [
                    const CircularProgressIndicator(),
                    const SizedBox(width: 16),
                    Text(
                      S.of(context)?.processingPleaseWait ??
                          'Processing, please wait...',
                      style: const TextStyle(
                        fontSize: 16,
                        fontWeight: FontWeight.w500,
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

  Widget _buildTaskStatusWidget() {
    String statusText;
    Color statusColor;

    switch (_taskStatus) {
      case TaskStatus.running:
        statusText = S.of(context)?.taskStatusRunning ?? 'Running';
        statusColor = Colors.blue;
        break;
      case TaskStatus.success:
        statusText = S.of(context)?.taskStatusSuccess ?? 'Success';
        statusColor = Colors.green;
        break;
      case TaskStatus.failed:
        statusText = S.of(context)?.taskStatusFailed ?? 'Failed';
        statusColor = Colors.red;
        break;
      case TaskStatus.idle:
      default:
        statusText = S.of(context)?.taskStatusIdle ?? 'Idle';
        statusColor = Colors.grey;
        break;
    }

    return Card(
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      margin: const EdgeInsets.symmetric(vertical: 12),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 16, horizontal: 24),
        child: Row(
          children: [
            Text(
              '${S.of(context)?.state ?? 'State'}: ',
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.bold,
                color: Colors.deepPurple,
              ),
            ),
            const SizedBox(width: 8),
            Text(
              statusText,
              style: TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w600,
                color: statusColor,
              ),
            ),
            const SizedBox(width: 8),
            if (_taskStatus == TaskStatus.running)
              const SizedBox(
                width: 18,
                height: 18,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            if (_taskStatus == TaskStatus.success)
              const Icon(Icons.check_circle, color: Colors.green),
            if (_taskStatus == TaskStatus.failed)
              const Icon(Icons.error, color: Colors.red),
          ],
        ),
      ),
    );
  }

  Widget _buildLogOutput() {
    return Card(
      elevation: 4,
      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
      margin: const EdgeInsets.symmetric(vertical: 12),
      child: Container(
        constraints: const BoxConstraints(
          minHeight: 150, // 与黑色部分的最小高度一致
          maxHeight: 150, // 与黑色部分的最大高度一致
        ),
        padding: const EdgeInsets.all(16),
        decoration: BoxDecoration(
          color: Colors.white, // 背景颜色设置为白色
          borderRadius: BorderRadius.circular(16), // 保持圆角
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
              // 使用 Expanded 使其适应父级
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
                color: Colors.white, // 更改为白色
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
                color: Colors.white70, // 更改为更浅的白色
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
                        _buildActionButtons(),
                        _buildTaskStatusWidget(),
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
