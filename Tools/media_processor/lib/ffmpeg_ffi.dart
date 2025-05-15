import 'dart:ffi';
import 'dart:io';
import 'package:ffi/ffi.dart';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart' as path;

// 加载所有必要的 FFmpeg 库
final DynamicLibrary _avcodecLib = _loadFfmpegLibrary('avcodec-61');
final DynamicLibrary _avdeviceLib = _loadFfmpegLibrary('avdevice-61');
final DynamicLibrary _avfilterLib = _loadFfmpegLibrary('avfilter-10');
final DynamicLibrary _avformatLib = _loadFfmpegLibrary('avformat-61');
final DynamicLibrary _avutilLib = _loadFfmpegLibrary('avutil-59');
final DynamicLibrary _postprocLib = _loadFfmpegLibrary('postproc-58');
final DynamicLibrary _swresampleLib = _loadFfmpegLibrary('swresample-5');
final DynamicLibrary _swscaleLib = _loadFfmpegLibrary('swscale-8');

DynamicLibrary _loadFfmpegLibrary(String libraryName) {
  if (Platform.isWindows) {
    final libraryPath = path.join(
      Directory.current.path,
      'data',
      'ffmpeg-release-full-shared',
      'bin',
      '$libraryName.dll',
    );
    if (kDebugMode) {
      print('Loading FFmpeg library: $libraryPath');
    } // 添加日志输出
    try {
      return DynamicLibrary.open(libraryPath);
    } catch (e) {
      if (kDebugMode) {
        print('Failed to load FFmpeg library $libraryName on Windows: $e');
      }
      rethrow;
    }
  } else if (Platform.isLinux) {
    if (kDebugMode) {
      print('Loading FFmpeg library: lib$libraryName.so');
    } // 添加日志输出
    try {
      return DynamicLibrary.open('lib$libraryName.so');
    } catch (e) {
      if (kDebugMode) {
        print('Failed to load FFmpeg library $libraryName on Linux: $e');
      }
      rethrow;
    }
  } else if (Platform.isMacOS) {
    if (kDebugMode) {
      print('Loading FFmpeg library: lib$libraryName.dylib');
    } // 添加日志输出
    try {
      return DynamicLibrary.open('lib$libraryName.dylib');
    } catch (e) {
      if (kDebugMode) {
        print('Failed to load FFmpeg library $libraryName on macOS: $e');
      }
      rethrow;
    }
  }
  throw UnsupportedError('Unsupported platform');
}

// FFmpeg 函数绑定
typedef AvFormatOpenInput =
    Int32 Function(
      Pointer<Pointer<AVFormatContext>> ps,
      Pointer<Utf8> url,
      Pointer<AVInputFormat> fmt,
      Pointer<AVDictionary> options,
    );

typedef AvFormatCloseInput =
    Void Function(Pointer<Pointer<AVFormatContext>> ps);

// 从 avformat 库中获取函数
final int Function(
  Pointer<Pointer<AVFormatContext>> ps,
  Pointer<Utf8> url,
  Pointer<AVInputFormat> fmt,
  Pointer<AVDictionary> options,
)
avFormatOpenInput =
    _avformatLib
        .lookup<NativeFunction<AvFormatOpenInput>>('avformat_open_input')
        .asFunction();

final void Function(Pointer<Pointer<AVFormatContext>> ps) avFormatCloseInput =
    _avformatLib
        .lookup<NativeFunction<AvFormatCloseInput>>('avformat_close_input')
        .asFunction();

// FFmpeg 结构体定义
final class AVFormatContext extends Struct {
  @Int32() // 占位字段
  external int placeholder;
}

final class AVInputFormat extends Struct {
  @Int32() // 占位字段
  external int placeholder;
}

final class AVDictionary extends Struct {
  @Int32() // 占位字段
  external int placeholder;
}
