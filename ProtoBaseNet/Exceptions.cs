namespace ProtoBaseNet;

public class Exceptions
{
    public string Message { get; set; }
    
}

public class ProtoValidationException : Exception
{
    public ProtoValidationException(string message) : base(message) { }
}

public class ProtoCorruptionException : Exception
{
    public ProtoCorruptionException(string message) : base(message) { }
}
