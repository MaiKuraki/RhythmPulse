import 'package:flutter_test/flutter_test.dart';

import '../lib/core/cancellation.dart';
import '../lib/core/log.dart';
import '../lib/core/outcome.dart';
import '../lib/core/ring_buffer.dart';
import 'helpers/fakes.dart';

void main() {
  group('Outcome', () {
    test('sealed variants fold/map without sentinels', () {
      const Outcome<int> ok = Ok<int>(7);
      const Outcome<int> err =
          Err<int>(Failure(FailureKind.io, 'disk full'));
      expect(ok.fold((v) => v * 2, (_) => -1), 14);
      expect(err.fold((v) => v, (f) => f.kind), FailureKind.io);
      expect(ok.map((v) => '$v').valueOrNull, '7');
      expect(err.map((v) => '$v').failureOrNull?.message, 'disk full');
    });
  });

  group('RingBuffer', () {
    test('O(1) append, overwrite oldest, clear releases references', () {
      final buffer = RingBuffer<String>(3);
      buffer.add('a');
      buffer.add('b');
      buffer.add('c');
      expect([for (final v in buffer.values()) v], ['a', 'b', 'c']);
      expect(buffer.isFull, isTrue);
      buffer.add('d');
      expect([for (final v in buffer.values()) v], ['b', 'c', 'd']);
      expect(buffer.revision, 4);
      buffer.clear();
      expect(buffer.length, 0);
      expect(buffer.values(), isEmpty);
    });

    test('invalid capacity is rejected', () {
      expect(() => RingBuffer<int>(0), throwsAssertionError);
    });
  });

  group('LruCache', () {
    test('recency, eviction callbacks and hit statistics', () {
      final evicted = <String>[];
      final cache = LruCache<String, int>(2, onEvict: (k, _) => evicted.add(k));
      cache.put('a', 1);
      cache.put('b', 2);
      expect(cache.get('a'), 1);
      cache.put('c', 3);
      expect(evicted, ['b']);
      expect(cache.get('b'), isNull);
      expect(cache.stats.hits, 1);
      expect(cache.stats.evictions, 1);
      expect(cache.stats.writes, 3);
      expect(cache.stats.lookups, 2);
      expect(cache.stats.hitRate, closeTo(0.5, 1e-9));
    });
  });

  group('CancellationToken', () {
    test('idempotent, unsubscribable, no listener retention', () {
      final token = CancellationToken();
      var first = 0;
      var second = 0;
      final sub = token.onCancel(() => first++);
      token.onCancel(() => second++);
      expect(token.listenerCount, 2);
      sub.cancel();
      expect(token.listenerCount, 1);
      token.cancel();
      token.cancel();
      expect(first, 0);
      expect(second, 1);
      expect(token.listenerCount, 0);
      var late = 0;
      token.onCancel(() => late++);
      expect(late, 1);
      token.dispose();
      expect(token.listenerCount, 0);
    });
  });

  group('LogStore', () {
    test('bounded, batched notifications and structured export', () {
      final scheduler = ManualScheduler();
      final store = LogStore(scheduler: scheduler, capacity: 5);
      final revisions = <int>[];
      store.revisions.listen(revisions.add);

      for (var i = 0; i < 50; i++) {
        store.info('source', 'line $i');
      }
      expect(store.length, 5);
      expect(store.hasPendingFlush, isTrue);
      expect(revisions, isEmpty,
          reason: 'no rebuild may happen inside the batching window');
      scheduler.advance();
      expect(revisions, [1]);
      expect(scheduler.schedules, 1,
          reason: 'one timer covers the entire batch');

      store.error('source', 'failure');
      scheduler.advance();
      expect(revisions, [1, 2]);
      expect(store[store.length - 1].level, LogLevel.error);
      expect(store.exportText(), contains('[ERROR  ]'));
      expect(store.exportText(), isNot(contains('line 0')));

      store.clear();
      expect(store.length, 0);
      expect(store.exportText(), isEmpty);
    });

    test('disabled store ignores writes after dispose', () async {
      final scheduler = ManualScheduler();
      final store = LogStore(scheduler: scheduler, capacity: 2);
      store.info('a', 'b');
      scheduler.advance();
      expect(store.length, 1);
      await store.dispose();
      store.info('a', 'c');
      expect(store.length, 1);
    });
  });
}
