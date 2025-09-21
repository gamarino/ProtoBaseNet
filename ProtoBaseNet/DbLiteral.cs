namespace ProtoBaseNet;

// Atom representing an interned/deduplicated string value.
// Typical usage: store strings as literals so other atoms can reference them
// by pointer, improving storage efficiency and enabling stable identity.
public class DbLiteral : Atom
{
    // The underlying string payload of this literal.
    public string Value { get; set; }

    // Constructs a literal with the provided string value.
    // The transaction/pointer lifecycle is managed by the Atom base when saved/loaded.
    public DbLiteral(string value)
    {
        Value = value;
    }
}