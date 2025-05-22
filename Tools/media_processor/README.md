# media_processor

This project is a Flutter application designed primarily for media file processing. Its main features include extracting audio from video files and converting videos into standardized formats compatible with Unity.

## Supported Platforms

- Windows Only

## Build Instructions

### Compile and Build
[Flutter](https://flutter.dev/) is required to build this project.
```bash
flutter clean
flutter pub get
flutter build windows --release
```
### Deploying the Package

To run the application correctly, you must use the [ffmpeg-release-full-shared](https://www.gyan.dev/ffmpeg/builds/) package. Place this package into the `Release/data/` directory of your build output.