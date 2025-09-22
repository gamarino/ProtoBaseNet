namespace ProtoBaseNet;

/// <summary>
/// The exception that is thrown when input data, state preconditions, or API contracts are violated.
/// </summary>
/// <remarks>
/// Typical use cases:
/// - Invalid arguments or missing required fields.
/// - Illegal state transitions (e.g., operations on a closed resource).
/// - Business rule validation failures.
/// </remarks>
public class ProtoValidationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoValidationException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ProtoValidationException(string message) : base(message) { }
}

/// <summary>
/// The exception that is thrown when storage or in-memory structures exhibit signs of inconsistency or corruption.
/// </summary>
/// <remarks>
/// Typical use cases:
/// - Broken pointers or unexpected missing data during load.
/// - Invariants violated in persistent structures.
/// - Serialization/deserialization mismatches that indicate data damage.
/// </remarks>
public class ProtoCorruptionException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoCorruptionException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ProtoCorruptionException(string message) : base(message) { }
}

/// <summary>
/// The exception that is thrown when the same root is updated by simoultaneous transactions
/// </summary>
/// <remarks>
/// Typical use cases:
/// - Root value is updated by multiple transactions and there is no possibility to resolve the conflict.
/// </remarks>
public class ProtoDbConcurrencyException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProtoDbConcurrencyException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ProtoDbConcurrencyException(string message) : base(message) { }
}
