import 'package:flutter/material.dart';
import 'package:flutter/scheduler.dart';

/// Log controller using a ring buffer to avoid O(n) removeAt(0).
/// Frame-synced auto-scroll to prevent layout thrashing.
class LogController extends ChangeNotifier {
  static const int _maxLogSize = 5000;

  // Ring buffer: once full, _start advances and old entries are overwritten
  final List<String?> _buffer = List.filled(_maxLogSize, null);
  int _start = 0;
  int _length = 0;

  final ScrollController scrollController = ScrollController();
  bool _scrollPending = false;
  bool _disposed = false;

  int get length => _length;

  String operator [](int index) {
    assert(index >= 0 && index < _length);
    return _buffer[(_start + index) % _maxLogSize]!;
  }

  List<String> get logs => List.generate(_length, (i) => this[i]);

  void addLog(String line) {
    if (_disposed) return;
    if (_length < _maxLogSize) {
      _buffer[_length] = line;
      _length++;
    } else {
      _buffer[_start] = line;
      _start = (_start + 1) % _maxLogSize;
    }
    notifyListeners();
    _scheduleScroll();
  }

  void clear() {
    _start = 0;
    _length = 0;
    notifyListeners();
  }

  void _scheduleScroll() {
    if (_scrollPending || !scrollController.hasClients) return;
    _scrollPending = true;
    SchedulerBinding.instance.addPostFrameCallback((_) {
      _scrollPending = false;
      if (!_disposed && scrollController.hasClients) {
        scrollController.jumpTo(scrollController.position.maxScrollExtent);
      }
    });
  }

  @override
  void dispose() {
    _disposed = true;
    scrollController.dispose();
    super.dispose();
  }
}

class LogView extends StatelessWidget {
  final LogController controller;

  const LogView({super.key, required this.controller});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: BoxDecoration(
        color: const Color(0xFF1E1E1E), // VS Code Dark background
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: Colors.white.withAlpha(25)),
        boxShadow: [
          BoxShadow(color: Colors.black.withAlpha(25), blurRadius: 10, offset: const Offset(0, 4)),
        ],
      ),
      child: Column(
        children: [
          // Terminal Header
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
            decoration: const BoxDecoration(
              color: Color(0xFF2D2D2D), // Header color
              borderRadius: BorderRadius.vertical(top: Radius.circular(16)),
            ),
            child: Row(
              children: [
                const Icon(Icons.terminal, color: Colors.grey, size: 18),
                const SizedBox(width: 10),
                const Text(
                  'TERMINAL OUTPUT',
                  style: TextStyle(
                    color: Colors.grey,
                    fontSize: 12,
                    fontWeight: FontWeight.bold,
                    letterSpacing: 1,
                  ),
                ),
                const Spacer(),
                // Clear Button
                IconButton(
                  icon: const Icon(Icons.delete_outline, color: Colors.grey, size: 18),
                  tooltip: 'Clear',
                  splashRadius: 20,
                  onPressed: controller.clear,
                )
              ],
            ),
          ),
          
          // Terminal Body
          Expanded(
            child: ClipRRect(
              borderRadius: const BorderRadius.vertical(bottom: Radius.circular(16)),
              child: SelectionArea(
                child: ListenableBuilder(
                  listenable: controller,
                  builder: (context, _) {
                    return ListView.builder(
                      padding: const EdgeInsets.all(16),
                      controller: controller.scrollController,
                      itemCount: controller.length,
                      itemBuilder: (context, index) {
                        final log = controller[index];
                        Color logColor = const Color(0xFFCCCCCC); // Default text color
                        
                        // Simple syntax highlighting
                        if (log.contains('❌') || log.contains('Error') || log.contains('Failed')) {
                          logColor = const Color(0xFFFF6B6B); // Soft Red
                        } else if (log.contains('✅') || log.contains('Success')) {
                          logColor = const Color(0xFF51CF66); // Soft Green
                        } else if (log.contains('Warning')) {
                          logColor = const Color(0xFFFFD43B); // Yellow
                        } else if (log.startsWith('frame=')) {
                          logColor = const Color(0xFF4DABF7); // Blue for progress stats
                        }

                        return Text(
                          log,
                          style: TextStyle(
                            color: logColor,
                            fontFamily: 'Consolas', // Monospace
                            fontSize: 12,
                            height: 1.4,
                          ),
                        );
                      },
                    );
                  },
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
