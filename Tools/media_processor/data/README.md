# Bundled FFmpeg

Place an [ffmpeg-release-full-shared](https://www.gyan.dev/ffmpeg/builds/) build
(or your own from https://github.com/FFmpeg/FFmpeg) into
`ffmpeg-release-full-shared/bin/` with `ffmpeg`, `ffprobe` and their DLLs.

CMake copies this directory next to the executable during
`flutter build windows --release`; no manual deployment step is needed. The
application resolves binaries relative to the executable and verifies that both
`-version` calls succeed before starting any task.

To use a different toolchain without rebuilding, set
`MEDIA_PROCESSOR_FFMPEG_DIR` to its `bin` directory.
