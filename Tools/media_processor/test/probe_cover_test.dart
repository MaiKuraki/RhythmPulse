import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:path/path.dart' as p;

import '../lib/domain/media.dart';
import '../lib/platform/host.dart';
import '../lib/services/binaries.dart';
import '../lib/services/process_runner.dart';
import '../lib/services/probe.dart';

void main() {
  test('attached_pic video stream is not treated as video', () {
    const source = '''
{"streams":[
 {"index":0,"codec_type":"audio","codec_name":"mp3","bit_rate":"192000"},
 {"index":1,"codec_type":"video","codec_name":"mjpeg","width":64,"height":64,
  "pix_fmt":"yuvj420p","disposition":{"attached_pic":1}}
],"format":{"duration":"1.000000","size":"1024"}}
''';
    final result = MediaProbeService.parse(source,
        path: 'song.mp3', sizeBytes: 1024, modifiedMs: 1);
    expect(result.isOk, isTrue);
    final info = result.valueOrNull!;
    expect(info.type, MediaType.audio);
    expect(info.hasVideo, isFalse);
    expect(info.hasAudio, isTrue);
    expect(info.resolution.isEmpty, isTrue);
    expect(info.videoCodec, isNull);
    expect(info.audioCodec, 'mp3');
  });

  test('a real MJPEG video stream is kept', () {
    const source = '''
{"streams":[
 {"index":0,"codec_type":"video","codec_name":"mjpeg","width":160,"height":120,
  "pix_fmt":"yuvj420p","disposition":{"attached_pic":0}}
],"format":{"duration":"1.000000","size":"1024"}}
''';
    final result = MediaProbeService.parse(source,
        path: 'clip.avi', sizeBytes: 1024, modifiedMs: 1);
    expect(result.isOk, isTrue);
    expect(result.valueOrNull!.hasVideo, isTrue);
    expect(result.valueOrNull!.resolution.height, 120);
  });

  test('real ffprobe: MP3 with album art is audio-only', () async {
    final binDir = p.join(Directory.current.path, 'data',
        'ffmpeg-release-full-shared', 'bin');
    final suffix = currentHostOs() == HostOs.windows ? '.exe' : '';
    final ffmpeg = p.join(binDir, 'ffmpeg$suffix');
    final ffprobe = p.join(binDir, 'ffprobe$suffix');
    if (!File(ffmpeg).existsSync() || !File(ffprobe).existsSync()) {
      return; // source-only checkout
    }
    final temp = Directory.systemTemp.createTempSync('mp_cover_probe_');
    final mp3 = p.join(temp.path, 'cover.mp3');
    try {
      final generation = await Process.run(ffmpeg, [
        '-hide_banner', '-loglevel', 'error',
        '-f', 'lavfi', '-i', 'sine=frequency=440:duration=1',
        '-f', 'lavfi', '-i', 'color=c=red:s=64x64:d=1',
        '-map', '0:a', '-map', '1:v',
        '-c:a', 'libmp3lame', '-c:v', 'mjpeg',
        '-disposition:v:0', 'attached_pic', '-id3v2_version', '3',
        '-y', mp3,
      ]);
      expect(generation.exitCode, 0,
          reason: generation.stderr.toString());

      final service = MediaProbeService(
        runner: SystemProcessRunner(),
        binaries: ExeRelativeBinaryLocator(
          executableDirectory: temp.path,
          os: currentHostOs(),
          overrideDirectory: binDir,
        ),
      );
      final result = await service.inspect(mp3);
      expect(result.isOk, isTrue,
          reason: result.failureOrNull?.message ?? 'probe failed');
      final info = result.valueOrNull!;
      expect(info.hasVideo, isFalse,
          reason: 'an ID3 cover must not turn an MP3 into a video');
      expect(info.type, MediaType.audio);
      expect(info.resolution.isEmpty, isTrue);
      expect(info.hasAudio, isTrue);
    } finally {
      temp.deleteSync(recursive: true);
    }
  });
}
