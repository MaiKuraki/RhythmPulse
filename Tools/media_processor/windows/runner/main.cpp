#include <flutter/dart_project.h>
#include <flutter/flutter_view_controller.h>
#include <windows.h>

#include <string>

#include "flutter_window.h"
#include "utils.h"

namespace {
/// Decides once, before any window is created, whether this process should
/// answer Windows accessibility (MSAA/UIA) queries.
///
/// Default is "blocked": this is an internal desktop tool and an always-on
/// UI Automation client (IME host, remote desktop, ...) otherwise makes the
/// Flutter engine log "Failed to update ui::AXTree" on every semantics tree
/// change. Set MEDIA_PROCESSOR_SEMANTICS=on to restore screen reader support.
/// MEDIA_PROCESSOR_DISABLE_SEMANTICS remains as a legacy alias.
const char* InitializeSemanticsPolicy() {
  char mode[16] = {};
  ::GetEnvironmentVariableA("MEDIA_PROCESSOR_SEMANTICS", mode, sizeof(mode));
  std::string value(mode);

  bool blocked = true;
  if (value == "on") {
    blocked = false;
  } else if (value == "off") {
    blocked = true;
  } else {
    char legacy[8] = {};
    ::GetEnvironmentVariableA("MEDIA_PROCESSOR_DISABLE_SEMANTICS", legacy,
                              sizeof(legacy));
    if (std::string(legacy) == "0") blocked = false;
  }

  const char* state = blocked ? "blocked" : "active";
  // Both the native message filter and the Dart app read this single value,
  // so their policies can never disagree.
  ::SetEnvironmentVariableA("MEDIA_PROCESSOR_SEMANTICS_STATE", state);
  std::string diagnostic = "media_processor: semantics=";
  diagnostic += state;
  diagnostic += "\n";
  ::OutputDebugStringA(diagnostic.c_str());
  return state;
}
}  // namespace

int APIENTRY wWinMain(_In_ HINSTANCE instance, _In_opt_ HINSTANCE prev,
                      _In_ wchar_t *command_line, _In_ int show_command) {
  // Attach to console when present (e.g., 'flutter run') or create a
  // new console when running with a debugger.
  if (!::AttachConsole(ATTACH_PARENT_PROCESS) && ::IsDebuggerPresent()) {
    CreateAndAttachConsole();
  }

  // Initialize COM, so that it is available for use in the library and/or
  // plugins.
  ::CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);

  InitializeSemanticsPolicy();

  // Bind this process and every descendant (ffmpeg / ffprobe) to a Job
  // Object. The handle is deliberately kept open: when the process exits for
  // any reason, including WM_DESTROY without a Dart shutdown notification,
  // Windows terminates every child instead of leaving orphan encoders.
  const char* job_status = "unavailable";
  HANDLE job_handle = ::CreateJobObject(nullptr, nullptr);
  if (job_handle != nullptr) {
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = {};
    limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
    if (::SetInformationJobObject(job_handle, JobObjectExtendedLimitInformation,
                                  &limits, sizeof(limits))) {
      if (::AssignProcessToJobObject(job_handle, ::GetCurrentProcess())) {
        job_status = "enabled";
      } else {
        job_status = "assign-failed";
      }
    } else {
      job_status = "configure-failed";
    }
    if (std::string(job_status) != "enabled") {
      ::CloseHandle(job_handle);
      job_handle = nullptr;
    }
  }
  ::SetEnvironmentVariableA("MEDIA_PROCESSOR_JOB_OBJECT", job_status);
  std::string diagnostic = "media_processor: child-process job object ";
  diagnostic += job_status;
  diagnostic += "\n";
  ::OutputDebugStringA(diagnostic.c_str());

  flutter::DartProject project(L"data");

  std::vector<std::string> command_line_arguments =
      GetCommandLineArguments();

  project.set_dart_entrypoint_arguments(std::move(command_line_arguments));

  FlutterWindow window(project);
  Win32Window::Point origin(10, 10);
  Win32Window::Size size(1280, 720);
  if (!window.Create(L"media_processor", origin, size)) {
    return EXIT_FAILURE;
  }
  window.SetQuitOnClose(true);

  ::MSG msg;
  while (::GetMessage(&msg, nullptr, 0, 0)) {
    ::TranslateMessage(&msg);
    ::DispatchMessage(&msg);
  }

  ::CoUninitialize();
  return EXIT_SUCCESS;
}
