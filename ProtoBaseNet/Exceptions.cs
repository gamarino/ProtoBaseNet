namespace ProtoBaseNet;

/// <summary>
/// A legacy placeholder container for exceptions.
/// </summary>
/// <remarks>This class is not used in typical flows. Prefer throwing concrete exception types.</remarks>
public class Exceptions
{
    /// <summary>
    /// Gets or sets a human-readable message describing the error condition.
    /// </summary>
    public string Message { get; set; }
}

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
