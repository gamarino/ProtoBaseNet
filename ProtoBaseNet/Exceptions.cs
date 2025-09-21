namespace ProtoBaseNet;

// Legacy placeholder container (unused in typical flows).
// Prefer throwing concrete exception types below rather than using this wrapper.
public class Exceptions
{
    // Human-readable message describing the error condition.
    public string Message { get; set; }
    
}

// Thrown when input data, state preconditions, or API contracts are violated.
// Typical use cases:
// - Invalid arguments or missing required fields
// - Illegal state transitions (e.g., operations on a closed resource)
// - Business rule validation failures
public class ProtoValidationException : Exception
{
    public ProtoValidationException(string message) : base(message) { }
}

// Thrown when storage or in-memory structures exhibit signs of inconsistency
// or corruption that cannot be recovered safely.
// Typical use cases:
// - Broken pointers or unexpected missing data during load
// - Invariants violated in persistent structures
// - Serialization/deserialization mismatches that indicate data damage
public class ProtoCorruptionException : Exception
{
    public ProtoCorruptionException(string message) : base(message) { }
}
