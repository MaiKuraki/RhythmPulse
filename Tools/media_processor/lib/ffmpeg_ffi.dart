import 'dart:ffi';
import 'dart:io';
import 'package:ffi/ffi.dart';
import 'package:flutter/foundation.dart';
import 'package:path/path.dart' as path;

/// Loads all required FFmpeg shared libraries with platform-specific handling
final DynamicLibrary _avcodecLib = _loadFfmpegLibrary('avcodec-61');
final DynamicLibrary _avdeviceLib = _loadFfmpegLibrary('avdevice-61');
final DynamicLibrary _avfilterLib = _loadFfmpegLibrary('avfilter-10');
final DynamicLibrary _avformatLib = _loadFfmpegLibrary('avformat-61');
final DynamicLibrary _avutilLib = _loadFfmpegLibrary('avutil-59');
final DynamicLibrary _postprocLib = _loadFfmpegLibrary('postproc-58');
final DynamicLibrary _swresampleLib = _loadFfmpegLibrary('swresample-5');
final DynamicLibrary _swscaleLib = _loadFfmpegLibrary('swscale-8');

/// Dynamically loads FFmpeg libraries based on the host operating system
///
/// [libraryName] The base name of the FFmpeg library (e.g., 'avcodec-61')
/// Returns a DynamicLibrary instance for the specified library
/// Throws UnsupportedError if the platform is not supported
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
    }
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
    }
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
    }
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

/// FFmpeg function type definitions for native interoperation
typedef AvFormatOpenInput =
    Int32 Function(
      Pointer<Pointer<AVFormatContext>> ps,
      Pointer<Utf8> url,
      Pointer<AVInputFormat> fmt,
      Pointer<AVDictionary> options,
    );

typedef AvFormatCloseInput =
    Void Function(Pointer<Pointer<AVFormatContext>> ps);

/// Binds native FFmpeg functions from the avformat library
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

/// FFmpeg structure definitions (placeholder implementations)
///
/// These structs serve as type markers for FFI operations and require
/// proper field definitions matching the native implementations.
final class AVFormatContext extends Struct {
  @Int32()
  external int placeholder;
}

final class AVInputFormat extends Struct {
  @Int32()
  external int placeholder;
}

final class AVDictionary extends Struct {
  @Int32()
  external int placeholder;
}
