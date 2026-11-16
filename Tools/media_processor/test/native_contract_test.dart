import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// The native AX guard cannot be exercised by a Dart unit test, so the
/// contract is pinned here to prevent accidental removal.
void main() {
  test('runner blocks WM_GETOBJECT by default and publishes one policy', () {
    final runner = File('windows/runner/flutter_window.cpp').readAsStringSync();
    expect(runner, contains('WM_GETOBJECT'));
    expect(runner, contains('MEDIA_PROCESSOR_SEMANTICS_STATE'));
    expect(runner, contains('OBJID_CLIENT'));
    expect(runner, contains('kUiaRootObjectId'));
    expect(runner, contains('AccessibilityQueriesDisabled'));
    expect(runner, contains('BlockChildAccessibilityQueries'));
    expect(runner, contains('FilteredChildWindowProc'));

    final entry = File('windows/runner/main.cpp').readAsStringSync();
    expect(entry, contains('InitializeSemanticsPolicy'));
    expect(entry, contains('MEDIA_PROCESSOR_SEMANTICS_STATE'));
    expect(entry, contains('MEDIA_PROCESSOR_SEMANTICS'));
    expect(entry, contains('const char* state = blocked ? "blocked" : "active"'));
    expect(entry, contains('InitializeSemanticsPolicy();'));

    final dart = File('lib/main.dart').readAsStringSync();
    expect(dart, contains('MEDIA_PROCESSOR_SEMANTICS_STATE'));
    expect(dart, contains('disableSemantics:'));

    final readme = File('README.md').readAsStringSync();
    expect(readme, contains("MEDIA_PROCESSOR_SEMANTICS='on'"));
    expect(readme, contains('blocked by default'));
  });
}
