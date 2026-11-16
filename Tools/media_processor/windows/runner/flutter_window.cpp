#include "flutter_window.h"

#include <optional>
#include <string>

#include "flutter/generated_plugin_registrant.h"

namespace {
// UiaRootObjectId is declared in uiautomation.h; avoiding that header keeps
// the runner free of the UI Automation import library.
constexpr DWORD kUiaRootObjectId = static_cast<DWORD>(-25);

/// The Flutter Windows engine enables its semantics/AXTree bridge lazily when
/// it receives WM_GETOBJECT (MSAA/UIA). An always-on Windows UI Automation
/// client (e.g. the IME host) triggers that path even for an internal tool,
/// and the engine's two-phase "remove reparented nodes" update can then log
/// "will not be in the tree and is not the new root" on every structural
/// change (opening the media_kit preview, resizing, ...).
///
/// main.cpp decides the policy once and publishes MEDIA_PROCESSOR_SEMANTICS_
/// STATE; both this native filter and the Dart app consume that single value.
/// When "blocked", every such query is answered with 0 so the bridge is never
/// enabled. MEDIA_PROCESSOR_SEMANTICS=on restores screen reader support.
bool AccessibilityQueriesDisabled() {
  static const bool disabled = [] {
    char value[16] = {};
    ::GetEnvironmentVariableA("MEDIA_PROCESSOR_SEMANTICS_STATE", value,
                              sizeof(value));
    return std::string(value) == "blocked";
  }();
  return disabled;
}

bool IsAccessibilityRootQuery(LPARAM lparam) {
  const DWORD object_id = static_cast<DWORD>(static_cast<DWORD_PTR>(lparam));
  return object_id == static_cast<DWORD>(OBJID_CLIENT) ||
         object_id == kUiaRootObjectId;
}

// The engine handles WM_GETOBJECT on both the top-level window and its child
// Flutter view HWND. Subclass the child too, otherwise an IME/UIA client that
// queries the focused child window can still enable the bridge.
WNDPROC g_original_child_proc = nullptr;

LRESULT CALLBACK FilteredChildWindowProc(HWND hwnd, UINT message,
                                         WPARAM wparam, LPARAM lparam) {
  if (message == WM_GETOBJECT && AccessibilityQueriesDisabled() &&
      IsAccessibilityRootQuery(lparam)) {
    return 0;
  }
  return ::CallWindowProc(g_original_child_proc, hwnd, message, wparam, lparam);
}

void BlockChildAccessibilityQueries(HWND child) {
  if (child == nullptr || g_original_child_proc != nullptr ||
      !AccessibilityQueriesDisabled()) {
    return;
  }
  const LONG_PTR previous = ::SetWindowLongPtrW(
      child, GWLP_WNDPROC,
      reinterpret_cast<LONG_PTR>(FilteredChildWindowProc));
  if (previous != 0) {
    g_original_child_proc = reinterpret_cast<WNDPROC>(previous);
  }
}
}  // namespace

FlutterWindow::FlutterWindow(const flutter::DartProject& project)
    : project_(project) {}

FlutterWindow::~FlutterWindow() {}

bool FlutterWindow::OnCreate() {
  if (!Win32Window::OnCreate()) {
    return false;
  }

  RECT frame = GetClientArea();

  // The size here must match the window dimensions to avoid unnecessary surface
  // creation / destruction in the startup path.
  flutter_controller_ = std::make_unique<flutter::FlutterViewController>(
      frame.right - frame.left, frame.bottom - frame.top, project_);
  // Ensure that basic setup of the controller was successful.
  if (!flutter_controller_->engine() || !flutter_controller_->view()) {
    return false;
  }
  RegisterPlugins(flutter_controller_->engine());
  const HWND child_view = flutter_controller_->view()->GetNativeWindow();
  SetChildContent(child_view);
  BlockChildAccessibilityQueries(child_view);

  flutter_controller_->engine()->SetNextFrameCallback([&]() {
    this->Show();
  });

  // Flutter can complete the first frame before the "show window" callback is
  // registered. The following call ensures a frame is pending to ensure the
  // window is shown. It is a no-op if the first frame hasn't completed yet.
  flutter_controller_->ForceRedraw();

  return true;
}

void FlutterWindow::OnDestroy() {
  if (flutter_controller_) {
    flutter_controller_ = nullptr;
  }

  Win32Window::OnDestroy();
}

LRESULT
FlutterWindow::MessageHandler(HWND hwnd, UINT const message,
                              WPARAM const wparam,
                              LPARAM const lparam) noexcept {
  // Intercept before forwarding: FlutterWindow::OnGetObject in the engine
  // calls OnUpdateSemanticsEnabled(true) for these object ids.
  if (message == WM_GETOBJECT && AccessibilityQueriesDisabled() &&
      IsAccessibilityRootQuery(lparam)) {
    return 0;
  }

  // Give Flutter, including plugins, an opportunity to handle window messages.
  if (flutter_controller_) {
    std::optional<LRESULT> result =
        flutter_controller_->HandleTopLevelWindowProc(hwnd, message, wparam,
                                                      lparam);
    if (result) {
      return *result;
    }
  }

  switch (message) {
    case WM_FONTCHANGE:
      flutter_controller_->engine()->ReloadSystemFonts();
      break;
  }

  return Win32Window::MessageHandler(hwnd, message, wparam, lparam);
}
