namespace ProtoBaseNet;

/// <summary>
/// Represents a node in a linked list used to handle hash collisions in DbCountedSet.
/// Each node stores an object and a reference to the next node in the chain.
/// </summary>
/// <typeparam name="T">The type of the object being stored.</typeparam>
internal class HashCollisionCounter<T>
{
    public T Value { get; }
    public int Count { get; }
    public HashCollisionCounter<T>? Next { get; }

    public HashCollisionCounter(T value, int count, HashCollisionCounter<T>? next = null)
    {
        Value = value;
        Count = count;
        Next = next;
    }
}