namespace ProtoBaseNet;

public class DbLiteral : Atom
{
    public string Value { get; set; }

    public DbLiteral(string value)
    {
        Value = value;
    }
}