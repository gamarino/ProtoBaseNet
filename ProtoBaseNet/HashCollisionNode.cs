
namespace ProtoBaseNet;

/// <summary>
/// Represents a node in a linked list used to handle hash collisions in DbSet.
/// Each node stores an object and a reference to the next node in the chain.
/// </summary>
/// <typeparam name="T">The type of the object being stored.</typeparam>
internal class HashCollisionNode<T>
{
    public T Value { get; }
    public HashCollisionNode<T>? Next { get; }

    public HashCollisionNode(T value, HashCollisionNode<T>? next = null)
    {
        Value = value;
        Next = next;
    }
}
