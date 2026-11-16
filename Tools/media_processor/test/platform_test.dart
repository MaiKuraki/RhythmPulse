import 'dart:io' show Platform;

import 'package:flutter_test/flutter_test.dart';

import '../lib/domain/encoding.dart';
import '../lib/platform/encoders.dart';
import '../lib/platform/host.dart';

void main() {
  group('HostCapabilities', () {
    test('environment overrides reproduce hardware scenarios', () {
      final x64 = detectHostCapabilities(environment: {
        'MEDIA_PROCESSOR_OS': 'windows',
        'MEDIA_PROCESSOR_ARCH': 'x64',
        'MEDIA_PROCESSOR_CORES': '32',
      });
      expect(x64.os, HostOs.windows);
      expect(x64.architecture, CpuArchitecture.x64);
      expect(x64.logicalCores, 32);
      expect(x64.executableSuffix, '.exe');

      final arm = detectHostCapabilities(environment: {
        'MEDIA_PROCESSOR_OS': 'linux',
        'MEDIA_PROCESSOR_ARCH': 'arm64',
        'MEDIA_PROCESSOR_CORES': '8',
      });
      expect(arm.executableSuffix, '');
      expect(arm.planFor(GpuVendor.software).encodeThreads,
          lessThan(x64.planFor(GpuVendor.software).encodeThreads));
    });

    test('real host is detected with at least one core', () {
      final host = detectHostCapabilities();
      expect(host.logicalCores, greaterThanOrEqualTo(1));
      expect(architectureFromAbi(), isNot(CpuArchitecture.unknown));
      expect(currentHostOs(), isNot(HostOs.unknown));
      expect(Platform.numberOfProcessors, greaterThan(0));
    });

    test('thread plans respect architecture and concurrency bounds', () {
      for (final os in HostOs.values) {
        for (final arch in CpuArchitecture.values) {
          for (final cores in [1, 2, 8, 32, 256, 999]) {
            final host = HostCapabilities(
                os: os, architecture: arch, logicalCores: cores);
            for (final vendor in GpuVendor.values) {
              final plan = host.planFor(vendor, maxConcurrencyOverride: 99);
              expect(plan.filterThreads, inInclusiveRange(2, 8));
              expect(plan.decodeThreads, inInclusiveRange(2, 8));
              expect(plan.maxConcurrentEncodes, 4);
              if (vendor == GpuVendor.software) {
                expect(plan.encodeThreads, inInclusiveRange(2, 32));
              } else {
                expect(plan.encodeThreads, 0,
                    reason: 'hardware encoders must not receive -threads');
              }
            }
          }
        }
      }
    });
  });

  group('EncoderCatalog', () {
    test('platform and architecture filters are correct', () {
      final winX64 = HostCapabilities(
          os: HostOs.windows,
          architecture: CpuArchitecture.x64,
          logicalCores: 16);
      final winArm = HostCapabilities(
          os: HostOs.windows,
          architecture: CpuArchitecture.arm64,
          logicalCores: 8);
      final macArm = HostCapabilities(
          os: HostOs.macos,
          architecture: CpuArchitecture.arm64,
          logicalCores: 8);
      final linuxX64 = HostCapabilities(
          os: HostOs.linux,
          architecture: CpuArchitecture.x64,
          logicalCores: 16);

      final winIds = EncoderCatalog.candidates(VideoCodec.h264, winX64)
          .map((e) => e.id)
          .toList();
      expect(winIds, containsAll(['h264_nvenc', 'h264_qsv', 'h264_amf', 'libx264']));
      expect(winIds, isNot(contains('h264_videotoolbox')));
      expect(winIds, isNot(contains('h264_vaapi')));
      expect(winIds.last, 'libx264', reason: 'software is the final fallback');

      expect(
          EncoderCatalog.candidates(VideoCodec.h264, winArm).map((e) => e.id),
          ['libx264']);
      expect(
          EncoderCatalog.candidates(VideoCodec.h264, macArm).map((e) => e.id),
          contains('h264_videotoolbox'));
      expect(
          EncoderCatalog.candidates(VideoCodec.h264, linuxX64).map((e) => e.id),
          contains('h264_vaapi'));
      expect(
          EncoderCatalog.candidates(VideoCodec.vp8, winX64).map((e) => e.id),
          ['libvpx']);
    });

    test('every descriptor has a preset for every speed tier', () {
      for (final encoder in EncoderCatalog.all) {
        for (var tier = 0; tier < 3; tier++) {
          expect(EncoderCatalog.presetFor(encoder, tier), isNotEmpty);
          expect(encoder.supportedOs, isNotEmpty);
        }
      }
    });
  });
}
