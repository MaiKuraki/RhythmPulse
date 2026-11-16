# media_processor

A Windows desktop tool for extracting audio from video files and converting
video into standardized, Unity-compatible formats.

## Architecture

```
lib/core       structured results, bounded logs, cancellation, O(1) caches
lib/domain     media model, table-driven encoding profiles, task API,
               output_plan.dart (explicit 720p–4K policy + pure advisories)
lib/platform   CPU architecture / OS / GPU capability and thread planning
lib/services   process runner, binary locator, probe, encoder selector,
               pure ffmpeg command builder, atomic output, waveform cache
lib/state      explicit task state machine
lib/ui         presentation only, wired through AppScope dependency injection;
               theme.dart (M3 light/dark + 8dp grid), section_card.dart
               (uniform section shell), localized strings, desktop shortcuts
```

All fallible operations return `Outcome<T>`; nothing is inferred from log text.
Outputs are encoded into `.part` files, then published as one transaction:
existing targets are parked under recoverable `.bak` names, all parts are
renamed into place only after every input validates, and any failure restores
every original. Encoder and stream-copy failures fall back through an ordered
hardware→software chain. Every `StageReport` carries the exact command and the
retained stderr tail.

## Build

Flutter 3.50 / Dart 3.14 or newer is required.

```bash
flutter pub get
flutter build windows --release
```

The bundled `data/ffmpeg-release-full-shared` toolchain is copied into
`Release/data/` automatically by CMake. For a custom toolchain set
`MEDIA_PROCESSOR_FFMPEG_DIR` to the directory containing `ffmpeg`/`ffprobe`.

Optional runtime tuning: `MEDIA_PROCESSOR_OS`, `MEDIA_PROCESSOR_ARCH`,
`MEDIA_PROCESSOR_CORES`, `MEDIA_PROCESSOR_MAX_PROCESSES`. The native runner
reports `MEDIA_PROCESSOR_JOB_OBJECT=enabled|assign-failed|configure-failed|
unavailable`; when it is not `enabled`, orphan-process protection relies on
the Dart-side shutdown hooks instead.

Windows accessibility (MSAA/UIA) support is **blocked by default**. This is
the deterministic workaround for the engine's `accessibility_bridge.cc` /
`ui::AXTree` "will not be in the tree and is not the new root" spam, triggered
by an always-on Windows UI Automation client (IME host, Narrator, remote
desktop) whenever the semantics tree changes structurally — notably when the
media_kit preview texture is created or the window is resized. Those messages
are engine-side and harmless, but they flood `flutter run` output.

The native runner decides once at startup, publishes
`MEDIA_PROCESSOR_SEMANTICS_STATE=blocked|active`, and both layers consume it:

- native: answers `WM_GETOBJECT` (`OBJID_CLIENT` / UIA root) with 0 on **both**
  the top-level window and the child Flutter view HWND, so
  `OnUpdateSemanticsEnabled(true)` is never reached and no `ui::AXTree` is built;
- Dart: wraps the app in `ExcludeSemantics`.

To restore screen reader / UI Automation support (and accept the engine logs):

```powershell
$env:MEDIA_PROCESSOR_SEMANTICS='on'
flutter run -d windows
```

`MEDIA_PROCESSOR_SEMANTICS=off` forces the blocked mode;
`MEDIA_PROCESSOR_DISABLE_SEMANTICS=0|1` is kept as a legacy alias. The startup
log (`[INFO] startup ... semantics=...`) always shows the effective policy.

## UI & interaction

Material 3 with `ColorScheme.fromSeed`, light/dark themes following the system,
and an 8dp spacing grid in `lib/ui/theme.dart`. Every section uses the same
`SectionCard` shell; all colors come from `ColorScheme` so dark mode and future
dynamic color need no per-widget changes.

Layout uses three breakpoints instead of one centered column:
- `<760px`: compact single column, stacked form fields, 220px log panel.
- `760–1200px`: single column with a three-column settings row, 300px log.
- `>=1200px`: 6:4 two-column desktop layout (source / settings / preview on
  the left, status / log on the right), 460px log. Content width follows the
  viewport up to 1760px, so wide windows are not surrounded by dead space.

All form controls share a 44dp height and all toolbar controls a 36dp height;
the log level selector is a compact popup chip rather than an input-style
field. The video preview uses the source aspect ratio (clamped to 0.5–3.0) on
a black rounded canvas.

Desktop shortcuts: `Ctrl+O` pick file, `Ctrl+E` export log,
`Ctrl+P` toggle preview, `Esc` cancel the running task. Actions that require a
selected file render in M3 disabled state.

## Media-aware workflow

- With **no file selected** the app shows only the file picker and a guided
  empty state; settings, source information, preview and processing actions are
  not rendered at all (status and log stay visible as global panels).
- A source information card shows type, resolution + tier, duration, codecs,
  file size and HDR/SDR, populated from the single ffprobe result.
- ffprobe requests `stream_disposition=attached_pic` and skips cover-art
  streams, so an **MP3 with ID3 album art is correctly reported as audio** while
  a real MJPEG/PNG video is kept. Only `attached_pic` is used as the
  discriminator; codec or duration heuristics would misclassify real video.
- **Audio-only inputs hide every video setting**: no container selector, no
  output-resolution selector, no video preview surface. Quality becomes
  "Audio quality" and only the audio stage runs.
- Output resolution is an explicit `OutputResolution` policy:
  `follow` (no scaling, default), `720p`, `1080p`, `1440p / 2K`, `2160p / 4K`.
  Width is derived from the source aspect ratio and forced even. Loading a new
  file resets the policy to `follow`. Dropdown labels are deliberately short
  (`Follow source`, `2K`, `4K`) and the exact pixel dimensions are shown in a
  supporting line, so no menu entry is ellipsized.
- Contextual advisories: upscale warning (with both sizes), downscale notice,
  sub-720p source warning, HDR→SDR tone-mapping notice, and lossless stream-copy
  availability. The same `OutputPlan` used by the UI drives the encoder.

## Verification

```bash
dart analyze lib test tool
VERIFICATION_ROUND=round-1 flutter test
dart run tool/summarize.dart
```

Real-ffmpeg integration tests run by default whenever the binaries are
present (they skip only on a source-only checkout). `VERIFICATION_ROUND`,
`VERIFICATION_DIR`, `COMBO_DEPTH`, `TEST_SEED`, `FUZZ_CASES` and `SOAK_MS`
control depth, seeds, fuzz volume, soak duration and report naming. Each round
writes one JSON report per suite into `verification/reports/`; `summarize.dart`
produces a cross-round comparison table and `summary.json`.

## Compatibility contract

The following surface is preserved by the refactor. Everything not listed here
(class names, file layout, log wording, encoder ordering, ffmpeg option
details) may change freely.

**User-visible behaviour**
- Select one media file, choose container / quality / output resolution, run
  full processing,
  open a clip preview, play/loop a range, watch the waveform, read the log,
  switch language, cancel a running task, export the log.
- Outputs are written next to the input file: `<base>_video_only.<ext>`,
  `<base>_audio.ogg`; previews use `<base>_preview.<ext>` / `<base>_preview.ogg`.
- Existing user files are never silently overwritten; a numeric suffix is used.
  When overwrite is requested, the original is recoverable if publishing fails.
- A cancelled / failed task leaves no half-written files behind.

**Runtime contract**
- Flutter 3.50 / Dart 3.14+, Windows x64/ARM64 primary.
- `ffmpeg`/`ffprobe` are resolved relative to the executable
  (`data/ffmpeg-release-full-shared/bin`), or from `MEDIA_PROCESSOR_FFMPEG_DIR`;
  both must pass a `-version` check.
- Environment: `MEDIA_PROCESSOR_OS`, `MEDIA_PROCESSOR_ARCH`,
  `MEDIA_PROCESSOR_CORES`, `MEDIA_PROCESSOR_MAX_PROCESSES`,
  `MEDIA_PROCESSOR_SEMANTICS`, `MEDIA_PROCESSOR_DISABLE_SEMANTICS` (legacy);
  `MEDIA_PROCESSOR_SEMANTICS_STATE` and `MEDIA_PROCESSOR_JOB_OBJECT` are
  published by the native runner as diagnostics.
- Verification switches: `COMBO_DEPTH`, `TEST_SEED`, `FUZZ_CASES`, `SOAK_MS`,
  `VERIFICATION_ROUND`, `VERIFICATION_DIR`.

**Library API kept stable for callers and tests**
- `Outcome<T>` / `Ok` / `Err` / `Failure` / `FailureKind`.
- `TaskRequest`, `TaskResult`, `TaskStatus`, `StageReport`, `Progress`.
- `ProcessRunner` (`run(ProcessRequest)`), `ProcessRequest`, `ProcessOutcome`,
  `OutputPlanner.plan`, `AtomicOutput.commit`.
- `ProfileRegistry.of`, `QualityPreset`, `ContainerFormat`.
- `OutputResolution`, `OutputPlan.build`, `buildInsights`.
- `AppStrings.get`, `AppStrings.fromJson`, `StringKey`, `AppStringsDelegate`.
- `MediaProcessorApp.initialMedia` (optional pre-selected source, used by
  layout verification and future session restore).
- `StringKey.all` must be present in both `en_US.json` and `zh_CN.json`.

**Intentional breaking change**: `TaskRequest.keep4K` (boolean) was replaced
by `TaskRequest.outputResolution` (`OutputResolution`, default `follow`). The
old flag could only express "cap at 1080p" and silently downscaled 4K sources;
the replacement covers 720p–4K explicitly and defaults to no scaling.

**Deliberately not in scope**
- No navigation stack, no window geometry persistence, no locale persistence
  beyond the process lifetime.
- No native `WM_CLOSE` interception: closing the window relies on the Job
  Object (when active) plus `didRequestAppExit` / lifecycle / SIGINT cleanup.
  The activation status is observable, not assumed.

## Supported platforms

Windows (x64 / ARM64). The encoder catalog also covers Linux and macOS
strategies; platform runners are generated by Flutter.
