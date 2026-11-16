import 'dart:collection';

/// Fixed-capacity FIFO ring buffer.
///
/// Append, indexed read and overwrite are O(1); capacity is allocated once,
/// so long-running tasks never grow the heap. [clear] nulls every slot so
/// strings are not retained after the buffer is reset.
final class RingBuffer<T> {
  RingBuffer(this.capacity)
      : assert(capacity > 0, 'capacity must be positive'),
        _items = List<T?>.filled(capacity, null);

  final int capacity;
  final List<T?> _items;
  int _start = 0;
  int _length = 0;
  int _revision = 0;

  int get length => _length;
  int get revision => _revision;
  bool get isEmpty => _length == 0;
  bool get isFull => _length == capacity;

  T operator [](int index) {
    assert(index >= 0 && index < _length, 'index out of range: $index');
    return _items[(_start + index) % capacity] as T;
  }

  void add(T item) {
    if (_length < capacity) {
      _items[(_start + _length) % capacity] = item;
      _length++;
    } else {
      _items[_start] = item;
      _start = (_start + 1) % capacity;
    }
    _revision++;
  }

  void clear() {
    for (var i = 0; i < capacity; i++) {
      _items[i] = null;
    }
    _start = 0;
    _length = 0;
    _revision++;
  }

  Iterable<T> values() sync* {
    for (var i = 0; i < _length; i++) {
      yield this[i];
    }
  }
}

/// Immutable cache counters, used both by callers and by verification tests.
final class CacheStats {
  const CacheStats({
    this.hits = 0,
    this.misses = 0,
    this.evictions = 0,
    this.writes = 0,
  });

  final int hits;
  final int misses;
  final int evictions;
  final int writes;

  int get lookups => hits + misses;
  double get hitRate => lookups == 0 ? 0.0 : hits / lookups;

  CacheStats copyWith({int? hits, int? misses, int? evictions, int? writes}) =>
      CacheStats(
        hits: hits ?? this.hits,
        misses: misses ?? this.misses,
        evictions: evictions ?? this.evictions,
        writes: writes ?? this.writes,
      );
}

/// O(1) LRU cache built on insertion-ordered [LinkedHashMap].
///
/// Moving an entry to the back is a remove + reinsert; eviction always takes
/// the oldest key, so no linear scans are performed.
final class LruCache<K, V> {
  LruCache(this.capacity, {this.onEvict})
      : assert(capacity > 0, 'capacity must be positive');

  final int capacity;
  final void Function(K key, V value)? onEvict;
  final LinkedHashMap<K, V> _entries = LinkedHashMap<K, V>();
  CacheStats _stats = const CacheStats();

  int get length => _entries.length;
  CacheStats get stats => _stats;
  Iterable<K> get keys => _entries.keys;

  V? get(K key) {
    final value = _entries.remove(key);
    if (value == null) {
      _stats = _stats.copyWith(misses: _stats.misses + 1);
      return null;
    }
    _entries[key] = value;
    _stats = _stats.copyWith(hits: _stats.hits + 1);
    return value;
  }

  void put(K key, V value) {
    _entries.remove(key);
    _entries[key] = value;
    _stats = _stats.copyWith(writes: _stats.writes + 1);
    while (_entries.length > capacity) {
      final oldestKey = _entries.keys.first;
      final oldestValue = _entries[oldestKey] as V;
      _entries.remove(oldestKey);
      onEvict?.call(oldestKey, oldestValue);
      _stats = _stats.copyWith(evictions: _stats.evictions + 1);
    }
  }

  bool containsKey(K key) => _entries.containsKey(key);
  void remove(K key) => _entries.remove(key);
  void clear() => _entries.clear();

  /// Overwrites a value without disturbing its recency.
  void updateIfPresent(K key, V value) {
    if (_entries.containsKey(key)) _entries[key] = value;
  }
}
