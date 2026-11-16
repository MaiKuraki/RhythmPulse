/// Every fallible operation in this package returns a structured [Outcome].
///
/// There is no implicit fallback, no sentinel string and no log-scraping:
/// callers must either consume [Ok.value] or handle [Err.failure].
enum FailureKind {
  invalidInput,
  binaryUnavailable,
  probeFailed,
  encoderUnavailable,
  processFailed,
  timeout,
  cancelled,
  io,
  unsupported,
  busy,
  internal,
}

final class Failure {
  final FailureKind kind;
  final String message;
  final Object? cause;

  const Failure(this.kind, this.message, {this.cause});

  @override
  String toString() =>
      'Failure(${kind.name}): $message${cause == null ? '' : ' [$cause]'}';
}

sealed class Outcome<T> {
  const Outcome();

  bool get isOk => this is Ok<T>;
  bool get isErr => this is Err<T>;

  T? get valueOrNull => switch (this) {
        Ok<T>(:final value) => value,
        Err<T>() => null,
      };

  Failure? get failureOrNull => switch (this) {
        Ok<T>() => null,
        Err<T>(:final failure) => failure,
      };

  R fold<R>(R Function(T value) onOk, R Function(Failure failure) onErr) =>
      switch (this) {
        Ok<T>(:final value) => onOk(value),
        Err<T>(:final failure) => onErr(failure),
      };

  Outcome<R> map<R>(R Function(T value) transform) => switch (this) {
        Ok<T>(:final value) => Ok(transform(value)),
        Err<T>(:final failure) => Err(failure),
      };
}

final class Ok<T> extends Outcome<T> {
  final T value;
  const Ok(this.value);

  @override
  String toString() => 'Ok($value)';
}

final class Err<T> extends Outcome<T> {
  final Failure failure;
  const Err(this.failure);

  @override
  String toString() => 'Err($failure)';
}
