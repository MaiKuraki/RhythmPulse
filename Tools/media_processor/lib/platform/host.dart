import 'dart:ffi' as ffi;
import 'dart:io' show Platform;

import 'encoders.dart';

enum CpuArchitecture { x64, arm64, ia32, arm32, riscv, unknown }
enum HostOs { windows, linux, macos, unknown }

/// Immutable host facts plus a derived thread/concurrency plan.
final class ThreadPlan {
  const ThreadPlan({
    required this.decodeThreads,
    required this.filterThreads,
    required this.encodeThreads,
    required this.maxConcurrentEncodes,
  });

  /// `encodeThreads == 0` means the encoder is hardware-bound and ffmpeg
  /// must not be given a `-threads` output option.
  final int decodeThreads;
  final int filterThreads;
  final int encodeThreads;
  final int maxConcurrentEncodes;
}

final class HostCapabilities {
  const HostCapabilities({
    required this.os,
    required this.architecture,
    required this.logicalCores,
  });

  final HostOs os;
  final CpuArchitecture architecture;
  final int logicalCores;

  bool get isWindows => os == HostOs.windows;
  String get executableSuffix => isWindows ? '.exe' : '';

  ThreadPlan planFor(GpuVendor vendor, {int? maxConcurrencyOverride}) {
    final hardware = vendor != GpuVendor.software;
    final cores = logicalCores.clamp(1, 256).toInt();
    final factor = switch (architecture) {
      CpuArchitecture.arm64 => 0.60,
      CpuArchitecture.riscv => 0.50,
      CpuArchitecture.ia32 || CpuArchitecture.arm32 => 0.50,
      _ => 0.75,
    };
    final maxEncode = architecture == CpuArchitecture.x64 ? 32 : 16;
    final encodeThreads = hardware
        ? 0
        : (cores * factor).ceil().clamp(2, maxEncode).toInt();
    final filterThreads = (cores / 2).ceil().clamp(2, 8).toInt();
    final decodeThreads = (cores / 4).ceil().clamp(2, 8).toInt();
    final concurrency = maxConcurrencyOverride ?? (hardware ? 2 : 1);
    return ThreadPlan(
      decodeThreads: decodeThreads,
      filterThreads: filterThreads,
      encodeThreads: encodeThreads,
      maxConcurrentEncodes: concurrency.clamp(1, 4).toInt(),
    );
  }

  @override
  String toString() =>
      'Host(${os.name}/${architecture.name}, $logicalCores cores)';
}

CpuArchitecture architectureFromAbi() {
  final abi = ffi.Abi.current();
  return switch (abi) {
    ffi.Abi.windowsX64 ||
    ffi.Abi.linuxX64 ||
    ffi.Abi.macosX64 ||
    ffi.Abi.androidX64 ||
    ffi.Abi.iosX64 ||
    ffi.Abi.fuchsiaX64 => CpuArchitecture.x64,
    ffi.Abi.windowsArm64 ||
    ffi.Abi.linuxArm64 ||
    ffi.Abi.macosArm64 ||
    ffi.Abi.macosArm64e ||
    ffi.Abi.androidArm64 ||
    ffi.Abi.iosArm64 ||
    ffi.Abi.iosArm64e ||
    ffi.Abi.fuchsiaArm64 => CpuArchitecture.arm64,
    ffi.Abi.windowsIA32 ||
    ffi.Abi.linuxIA32 ||
    ffi.Abi.androidIA32 => CpuArchitecture.ia32,
    ffi.Abi.linuxArm ||
    ffi.Abi.androidArm ||
    ffi.Abi.iosArm => CpuArchitecture.arm32,
    ffi.Abi.linuxRiscv32 ||
    ffi.Abi.linuxRiscv64 ||
    ffi.Abi.androidRiscv64 ||
    ffi.Abi.fuchsiaRiscv64 => CpuArchitecture.riscv,
    _ => CpuArchitecture.unknown,
  };
}

HostOs currentHostOs() {
  if (Platform.isWindows) return HostOs.windows;
  if (Platform.isLinux) return HostOs.linux;
  if (Platform.isMacOS) return HostOs.macos;
  return HostOs.unknown;
}

/// Environment overrides exist for deployment tuning and for verification of
/// hardware scenarios that the CI machine does not physically have.
HostCapabilities detectHostCapabilities({Map<String, String>? environment}) {
  final env = environment ?? Platform.environment;
  final archOverride = env['MEDIA_PROCESSOR_ARCH'];
  final coresOverride = int.tryParse(env['MEDIA_PROCESSOR_CORES'] ?? '');
  final osOverride = env['MEDIA_PROCESSOR_OS'];
  return HostCapabilities(
    os: osOverride == null
        ? currentHostOs()
        : HostOs.values.firstWhere(
            (v) => v.name == osOverride,
            orElse: () => HostOs.unknown,
          ),
    architecture: archOverride == null
        ? architectureFromAbi()
        : CpuArchitecture.values.firstWhere(
            (v) => v.name == archOverride,
            orElse: () => CpuArchitecture.unknown,
          ),
    logicalCores:
        (coresOverride ?? Platform.numberOfProcessors).clamp(1, 256).toInt(),
  );
}
