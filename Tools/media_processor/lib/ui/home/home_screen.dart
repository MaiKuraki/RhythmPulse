import 'dart:async';
import 'dart:io';

import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'package:localization/localization.dart';
import 'package:path/path.dart' as path;

import '../../main.dart';
import '../../models/processing_state.dart';
import '../../services/ffmpeg_service.dart';
import '../../utils/cancellation_token.dart';
import '../widgets/control_panel.dart';
import '../widgets/log_view.dart';

class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  // State
  String? _selectedFilePath;
  TaskStatus _taskStatus = TaskStatus.idle;
  bool _showPreviewOptions = false;
  bool _videoOutputApply4K = false;
  VideoFormat _videoFormat = VideoFormat.mp4;
  double _progress = 0.0;
  
  // Controllers
  final LogController _logController = LogController();
  final TextEditingController _startTimeController = TextEditingController();
  final TextEditingController _endTimeController = TextEditingController();
  
  // Async management
  CancellationToken? _cancellationToken;

  final List<String> _allowedExtensions = [
    'mp4', 'avi', 'mkv', 'mov', 'flv', 'wmv', 'webm',
    'mp3', 'wav', 'ogg', 'aac', 'm4a', 'flac',
  ];

  @override
  void dispose() {
    _startTimeController.dispose();
    _endTimeController.dispose();
    _cancelCurrentTask();
    super.dispose();
  }

  // --- Actions ---

  Future<void> _pickMediaFile() async {
    _logController.clear();
    setState(() => _taskStatus = TaskStatus.idle);
    
    try {
      final result = await FilePicker.platform.pickFiles(
        type: FileType.custom,
        allowedExtensions: _allowedExtensions,
      );
      
      if (result != null && result.files.single.path != null) {
        final path = result.files.single.path!;
        setState(() {
          _selectedFilePath = path;
          _taskStatus = TaskStatus.idle;
        });
        _logController.addLog('${'selectedFile'.i18n()}\n$path');
      }
    } catch (e) {
      _logController.addLog('fileSelectionFailed'.i18n(['$e']));
      setState(() => _taskStatus = TaskStatus.failed);
    }
  }

  void _clearSelectedFile() {
    if (_taskStatus == TaskStatus.running) return;
    setState(() {
      _selectedFilePath = null;
      _taskStatus = TaskStatus.idle;
    });
    _logController.clear();
  }

  Future<void> _cancelCurrentTask() async {
    if (_taskStatus == TaskStatus.running) {
      _logController.addLog('\n${'taskCanceled'.i18n()}');
      setState(() => _taskStatus = TaskStatus.canceled);
      
      _cancellationToken?.cancel();
      await FfmpegProcessManager().killAll();
      _cancellationToken = null;
    }
  }

  Future<void> _generateFullMedia() async {
    if (_selectedFilePath == null) return;
    
    setState(() {
      _taskStatus = TaskStatus.running;
      _progress = 0.0;
    });
    _logController.addLog('processingPleaseWait'.i18n());
    
    _cancellationToken = CancellationToken();
    
    try {
      final inputFile = File(_selectedFilePath!);
      final dir = inputFile.parent.path.replaceAll('\\', '/');
      final baseName = path.basenameWithoutExtension(_selectedFilePath!);
      
      final outputAudioPath = '$dir/${baseName}_audio.ogg';
      String? outputVideoPath;
      
      final mediaType = await FfmpegService.detectMediaType(_selectedFilePath!);
      if (mediaType == MediaType.video) {
        final ext = _videoFormat == VideoFormat.webm ? 'webm' : 'mp4';
        outputVideoPath = '$dir/${baseName}_video_only.$ext';
      }

      // Map localized strings
      final locStrings = {
        'videoSplitCmd': 'videoSplitCmd'.i18n(['%s']),
        'audioSplitCmd': 'audioSplitCmd'.i18n(['%s']),
        'videoSplitSuccess': 'videoSplitSuccess'.i18n(['%s']),
        'audioSplitSuccess': 'audioSplitSuccess'.i18n(['%s']),
        'videoSplitFailed': 'videoSplitFailed'.i18n(['%s']),
        'audioSplitFailed': 'audioSplitFailed'.i18n(['%s']),
         'hdrToSdrWarning': 'Warning: HDR to SDR conversion applied.' 
      };

      await FfmpegService.generateFullMedia(
        inputPath: _selectedFilePath!,
        outputVideoPath: outputVideoPath,
        outputAudioPath: outputAudioPath,
        localizedStrings: locStrings,
        apply4K: _videoOutputApply4K,
        videoFormat: _videoFormat,
        onLog: (line) => _logController.addLog(line),
        onProgress: (p) => setState(() => _progress = p),
        cancelToken: _cancellationToken,
      );

      if (_taskStatus != TaskStatus.canceled) {
         // Check cancellation token first
         if (_cancellationToken?.isCancelled == true) {
            setState(() => _taskStatus = TaskStatus.canceled);
         } else {
             // Heuristic: check logs for failure
             final hasError = _logController.logs.any((l) => l.contains('❌'));
             setState(() => _taskStatus = hasError ? TaskStatus.failed : TaskStatus.success);
         }
      }

    } catch (e) {
      if (_taskStatus != TaskStatus.canceled) {
         _logController.addLog('errorOccurred'.i18n(['$e']));
         setState(() => _taskStatus = TaskStatus.failed);
      }
    } finally {
      if (_taskStatus != TaskStatus.canceled && mounted) {
        // UI consistency
      }
      _cancellationToken = null;
    }
  }

  Future<void> _generatePreview() async {
     if (_selectedFilePath == null) return;
     
     final startMs = int.tryParse(_startTimeController.text) ?? 0;
     final endMs = int.tryParse(_endTimeController.text) ?? 0;

     if (startMs < 0 || endMs <= startMs) {
       _logController.addLog('invalidTimeRange'.i18n());
       setState(() => _taskStatus = TaskStatus.failed);
       return;
     }

     setState(() {
       _taskStatus = TaskStatus.running;
       _progress = 0.0;
     });
     _logController.addLog('generatingPreview'.i18n());
     _cancellationToken = CancellationToken();

     try {
       final inputFile = File(_selectedFilePath!);
       final dir = inputFile.parent.path.replaceAll('\\', '/');
       final baseName = path.basenameWithoutExtension(_selectedFilePath!);
       
        final mediaType = await FfmpegService.detectMediaType(_selectedFilePath!);
        final isVideo = mediaType == MediaType.video;

        final outputVideoPath = isVideo 
            ? '$dir/${baseName}_preview.${_videoFormat == VideoFormat.webm ? 'webm' : 'mp4'}' 
            : null;
        final outputAudioPath = '$dir/${baseName}_preview.ogg';

        await FfmpegService.generatePreview(
          inputPath: _selectedFilePath!,
          outputVideoPath: outputVideoPath,
          outputAudioPath: outputAudioPath,
          startMs: startMs,
          endMs: endMs,
          videoFormat: _videoFormat,
          onLog: (line) => _logController.addLog(line),
          onProgress: (p) => setState(() => _progress = p),
          cancelToken: _cancellationToken,
        );
        
        if (_taskStatus != TaskStatus.canceled) {
           if (_cancellationToken?.isCancelled == true) {
               setState(() => _taskStatus = TaskStatus.canceled);
           } else {
               setState(() => _taskStatus = TaskStatus.success);
           }
        }

     } catch (e) {
        if (_taskStatus != TaskStatus.canceled) {
          _logController.addLog('errorOccurred'.i18n(['$e']));
          setState(() => _taskStatus = TaskStatus.failed);
        }
     } finally {
       _cancellationToken = null;
     }
  }

  // --- UI Builders ---

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
              style: const TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: Colors.deepPurple),
            ),
            const SizedBox(height: 12),
            Row(
              children: [
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(vertical: 12, horizontal: 16),
                    decoration: BoxDecoration(
                      color: Colors.deepPurple.shade50,
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(color: _selectedFilePath != null ? Colors.deepPurple : Colors.grey.shade400),
                    ),
                    child: Text(
                      _selectedFilePath ?? 'noFileSelected'.i18n(),
                      style: TextStyle(
                        fontSize: 14,
                        color: _selectedFilePath != null ? Colors.black87 : Colors.grey.shade600,
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
                  onPressed: _taskStatus == TaskStatus.running ? null : _pickMediaFile,
                ),
                 if (_selectedFilePath != null) ...[
                   const SizedBox(width: 8),
                   ElevatedButton.icon(
                      style: ElevatedButton.styleFrom(backgroundColor: Colors.redAccent, foregroundColor: Colors.white),
                      icon: const Icon(Icons.clear),
                      label: Text('clear'.i18n()),
                      onPressed: _taskStatus == TaskStatus.running ? null : _clearSelectedFile,
                   )
                 ]
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
      child: _showPreviewOptions ? Column(
        children: [
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _startTimeController,
                  keyboardType: TextInputType.number,
                  decoration: InputDecoration(
                    labelText: 'startTimeMs'.i18n(),
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
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
                    border: OutlineInputBorder(borderRadius: BorderRadius.circular(8)),
                  ),
                ),
              )
            ],
          ),
          const SizedBox(height: 16),
          SizedBox(
            width: double.infinity,
            child: ElevatedButton.icon(
              icon: const Icon(Icons.play_arrow),
              label: Text('generatePreview'.i18n()),
              onPressed: _taskStatus == TaskStatus.running ? null : _generatePreview,
              style: ElevatedButton.styleFrom(
                backgroundColor: Colors.deepPurple,
                foregroundColor: Colors.white,
              ),
            ),
          )
        ],
      ) : null,
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
                 fontWeight: FontWeight.bold, fontSize: 24, letterSpacing: 1.2, color: Colors.white,
                 shadows: [Shadow(color: Colors.black26, offset: Offset(1,1), blurRadius: 2)]
               ),
             ),
             const SizedBox(height: 4),
             Text(
               'appSubtitle'.i18n(),
               style: const TextStyle(fontSize: 12, color: Colors.white70, fontWeight: FontWeight.w400),
             )
           ],
        ),
        actions: [
          PopupMenuButton<Locale>(
            icon: const Icon(Icons.language),
            tooltip: 'Language',
            onSelected: (locale) {
              appLocaleNotifier.value = locale;
            },
            itemBuilder: (context) => [
              const PopupMenuItem(
                value: Locale('en', 'US'),
                child: Row(children: [Text('🇺🇸 English')]),
              ),
              const PopupMenuItem(
                value: Locale('zh', 'CN'),
                child: Row(children: [Text('🇨🇳 中文')]),
              ),
            ],
          ),
          IconButton(
            icon: const Icon(Icons.info_outline),
            tooltip: 'about'.i18n(),
             onPressed: () {
              showDialog(
                context: context,
                builder: (context) => AlertDialog(
                  title: Text('appTitle'.i18n()),
                  content: Column(
                    mainAxisSize: MainAxisSize.min,
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('v1.0.0', style: Theme.of(context).textTheme.bodyMedium),
                      const SizedBox(height: 12),
                      Text('copyright'.i18n(), style: Theme.of(context).textTheme.bodySmall),
                    ],
                  ),
                  actions: [
                    TextButton(
                      onPressed: () => Navigator.of(context).pop(),
                      child: Text('ok'.i18n()),
                    ),
                  ],
                ),
              );
            },
          )
        ],
      ),
      body: Container(
        decoration: const BoxDecoration(
           color: Color(0xFFF5F5F7), // Apple-like background
        ),
        child: SafeArea(
          child: Padding(
            padding: const EdgeInsets.all(24),
            child: SingleChildScrollView(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _buildFileSelector(),
                  const SizedBox(height: 24),
                  ControlPanel(
                    taskStatus: _taskStatus,
                    isFileSelected: _selectedFilePath != null,
                    showPreviewOptions: _showPreviewOptions,
                    apply4k: _videoOutputApply4K,
                    videoFormat: _videoFormat,
                    progress: _progress,
                    onGenerateFull: _generateFullMedia,
                    onTogglePreview: () => setState(() => _showPreviewOptions = !_showPreviewOptions),
                    onCancel: _cancelCurrentTask,
                    on4kChanged: (val) => setState(() => _videoOutputApply4K = val),
                    onFormatChanged: (val) => setState(() => _videoFormat = val),
                    previewOptionsChild: _buildPreviewOptions(),
                  ),
                  const SizedBox(height: 24),
                  
                  // Terminal connected below
                  SizedBox(
                    height: 320, // Fixed height for terminal in scrolling view
                    child: LogView(controller: _logController)
                  ),
                  
                  Padding(
                     padding: const EdgeInsets.only(top: 24, bottom: 8),
                     child: Center(
                        child: Text('copyright'.i18n(), style: TextStyle(color: Colors.grey.shade500, fontSize: 11)),
                     ),
                  )
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
