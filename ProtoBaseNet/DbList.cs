namespace ProtoBaseNet;

using System;
using System.Collections.Generic;
using System.Collections;

/// <summary>
/// An immutable, persistent list implemented as a balanced binary tree (AVL-like).
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
/// <remarks>
/// Key characteristics:
/// - Structural sharing: all mutating operations return a new <see cref="DbList{T}"/> instance,
///   reusing unchanged subtrees to minimize allocations.
/// - Balanced tree: rotations keep height logarithmic, enabling O(log N) positional
///   access (<see cref="GetAt"/>, <see cref="SetAt"/>, <see cref="InsertAt"/>, <see cref="RemoveAt"/>) and O(N) in-order iteration.
/// </remarks>
public class DbList<T> : DbCollection, IEnumerable<T>
{
    /// <summary>
    /// Gets a value indicating whether the list is empty.
    /// </summary>
    public bool Empty { get; init; } = true;

    /// <summary>
    /// Gets the value of the current node. Only valid if <see cref="Empty"/> is false.
    /// </summary>
    public T? Value { get; init; }

    /// <summary>
    /// Gets the height of the subtree rooted at this node.
    /// </summary>
    public int Height { get; init; }

    /// <summary>
    /// Gets the right child of the node.
    /// </summary>
    public DbList<T>? Next { get; init; }

    /// <summary>
    /// Gets the left child of the node.
    /// </summary>
    public DbList<T>? Previous { get; init; }

    /// <summary>
    /// Gets the total number of elements in the list.
    /// </summary>
    public new int Count { get; init; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbList{T}"/> class.
    /// This constructor is used to create nodes initially loaded with a List values.
    /// </summary>
    public DbList(List<T> initialValues)
    {
        // DbList from List
        var newList = new DbList<T>();
        foreach (var v in initialValues)
            newList = newList.InsertAt(0, v);
        Value = newList.Value;
        Empty = newList.Empty;
        Next = newList.Next;
        Previous = newList.Previous;
        Height = newList.Height;
        Count = newList.Count;
        Indexes = newList.Indexes;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DbList{T}"/> class.
    /// This constructor is used to create nodes in the tree.
    /// </summary>
    public DbList(
        T? value = default,
        bool empty = true,
        DbList<T>? next = null,
        DbList<T>? previous = null,
        Guid? stableId = null,
        DbDictionary<Index>? indexes = null,
        ObjectTransaction? transaction = null) : base(stableId: stableId, transaction: transaction)
    {
        if (previous is { Empty: true }) previous = null;
        if (next is { Empty: true }) next = null;

        Value = value;
        Next = next;
        Previous = previous;
        Indexes = indexes;

        var hasContent = (value is not null) || previous is not null || next is not null;
        Empty = hasContent ? false : empty;

        if (!Empty)
        {
            var count = 1;
            if (Previous is not null)
                count += Previous.Count;
            if (Next is not null)
                count += Next.Count;
            Count = count;
        }
        else
        {
            Count = 0;
        }

        if (!Empty)
        {
            var hPrev = Previous?.Height ?? 0;
            var hNext = Next?.Height ?? 0;
            Height = 1 + Math.Max(hPrev, hNext);
        }
        else
        {
            Height = 0;
        }
    }

    private DbList<T> With(
        T? value,
        bool empty,
        DbList<T>? previous,
        DbList<T>? next,
        DbDictionary<Index>? indexes = null)
    {
        return new DbList<T>(
            value: value,
            empty: empty,
            next: next,
            previous: previous, 
            indexes: indexes);
    }

    /// <summary>
    /// Gets the element at the specified index.
    /// </summary>
    /// <param name="offset">The zero-based index of the element to get. Negative offsets are counted from the end of the list.</param>
    /// <returns>The element at the specified index, or default(T) if the index is out of range.</returns>
    public T? GetAt(int offset)
    {
        if (Empty) return default;

        if (offset < 0) offset = Count + offset;
        if (offset < 0 || offset >= Count) return default;

        var node = this;
        while (node is not null)
        {
            var leftCount = node.Previous?.Count ?? 0;

            if (offset == leftCount)
                return node.Value;

            if (offset > leftCount)
            {
                node = node.Next!;
                offset -= leftCount + 1;
            }
            else
            {
                node = node.Previous!;
            }
        }

        return default;
    }

    private int BalanceFactor()
    {
        if (Empty) return 0;
        var hL = Previous?.Height ?? 0;
        var hR = Next?.Height ?? 0;
        return hR - hL;
    }

    private DbList<T> RightRotation()
    {
        if (Previous is null) return this;

        var newRight = new DbList<T>(
            value: Value,
            empty: false,
            next: Next,
            previous: Previous.Next);

        return new DbList<T>(
            value: Previous.Value,
            empty: false,
            next: newRight,
            previous: Previous.Previous);
    }

    private DbList<T> LeftRotation()
    {
        if (Next is null) return this;

        var newLeft = new DbList<T>(
            value: Value,
            empty: false,
            next: Next.Previous,
            previous: Previous);

        return new DbList<T>(
            value: Next.Value,
            empty: false,
            next: Next.Next,
            previous: newLeft);
    }

    private DbList<T> Rebalance()
    {
        var node = this;

        while (node.Previous is not null && !(node.Previous.BalanceFactor() is >= -1 and <= 1))
        {
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                next: node.Next,
                previous: node.Previous.Rebalance());
        }

        while (node.Next is not null && !(node.Next.BalanceFactor() is >= -1 and <= 1))
        {
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                next: node.Next.Rebalance(),
                previous: node.Previous);
        }

        var balance = node.BalanceFactor();

        if (balance < -1)
        {
            if (node.Previous is not null && node.Previous.BalanceFactor() > 0)
            {
                node = new DbList<T>(
                    value: node.Value,
                    empty: false,
                    next: node.Next,
                    previous: node.Previous.LeftRotation());
            }

            return node.RightRotation();
        }

        if (balance > 1)
        {
            if (node.Next is not null && node.Next.BalanceFactor() < 0)
            {
                node = new DbList<T>(
                    value: node.Value,
                    empty: false,
                    next: node.Next.RightRotation(),
                    previous: node.Previous);
            }

            return node.LeftRotation();
        }

        return node;
    }

    /// <summary>
    /// Returns a new list with the element at the specified index replaced by the new value.
    /// </summary>
    /// <param name="offset">The zero-based index of the element to replace.</param>
    /// <param name="value">The new value for the element.</param>
    /// <returns>A new list with the element replaced.</returns>
    /// <exception cref="IndexOutOfRangeException">Thrown if the offset is out of range.</exception>
    public DbList<T>? SetAt(int offset, T? value)
    {
        if (offset < 0) offset = Count + offset;

        if (Empty)
        {
            if (offset == 0)
                return new DbList<T>(value: value, empty: false);
            throw new IndexOutOfRangeException("Offset out of range");
        }

        if (offset < 0 || offset > Count)
            throw new IndexOutOfRangeException("Offset out of range");

        var leftCount = Previous?.Count ?? 0;
        var cmp = offset - leftCount;

        DbList<T> newNode;
        if (cmp > 0)
        {
            if (Next is not null)
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: Next.SetAt(offset - leftCount - 1, value),
                    previous: Previous, 
                    indexes: Indexes);
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: new DbList<T>(value: value, empty: false),
                    previous: Previous, 
                    indexes: Indexes);
            }
        }
        else if (cmp < 0)
        {
            if (Previous is not null)
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: Next,
                    previous: Previous.SetAt(offset, value));
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: Next,
                    previous: new DbList<T>(value: value, empty: false));
            }
        }
        else
        {
            newNode = new DbList<T>(
                value: value,
                empty: false,
                next: Next,
                previous: Previous);
        }

        var result = newNode.Rebalance();

        var newIndexes = Indexes; // Placeholder: integrate index updates here if needed.

        return new DbList<T>(
            value: result.Value,
            empty: result.Empty,
            next: result.Next,
            previous: result.Previous, 
            indexes: newIndexes);
    }

    /// <summary>
    /// Inserts an element into the list at the specified index.
    /// </summary>
    /// <param name="offset">The zero-based index at which the element should be inserted.</param>
    /// <param name="value">The element to insert.</param>
    /// <returns>A new list with the element inserted.</returns>
    public DbList<T> InsertAt(int offset, T? value)
    {
        if (offset < 0) offset = Count + offset;
        if (offset < 0) offset = 0;
        if (offset >= Count) offset = Count;

        DbList<T>? nodeToReplace = null;
        
        if (Empty)
            return new DbList<T>(value: value, empty: false);

        var leftCount = Previous?.Count ?? 0;
        var cmp = offset - leftCount;

        DbList<T> newNode;
        if (cmp > 0)
        {
            if (Next is not null)
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: Next.InsertAt(cmp - 1, value),
                    previous: Previous);
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: new DbList<T>(value: value, empty: false),
                    previous: Previous);
            }
        }
        else if (cmp < 0)
        {
            if (Previous is not null)
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: Next,
                    previous: Previous.InsertAt(offset, value));
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: Next,
                    previous: new DbList<T>(value: value, empty: false));
            }
        }
        else
        {
            nodeToReplace = this;
            newNode = new DbList<T>(
                value: value,
                empty: false,
                next: new DbList<T>(value: Value, empty: false, next: Next, previous: null));
        }

        var result = newNode.Rebalance();
        var newIndexes = Indexes; // Placeholder for index updates.
        if (nodeToReplace is not null)
            ObjectRemoveFromIndexes(nodeToReplace.Value);
        newIndexes = ObjectAddToIndexes(value);
        return new DbList<T>(
            value: result.Value,
            empty: result.Empty,
            next: result.Next,
            previous: result.Previous, 
            indexes: newIndexes);
    }

    /// <summary>
    /// Removes the element at the specified index.
    /// </summary>
    /// <param name="offset">The zero-based index of the element to remove.</param>
    /// <returns>A new list with the element removed.</returns>
    public DbList<T> RemoveAt(int offset)
    {
        if (offset < 0) offset = Count + offset;
        if (offset < 0 || offset >= Count) return this;

        if (Empty) return this;

        var leftCount = Previous?.Count ?? 0;
        var cmp = offset - leftCount;

        var currentValue = GetAt(offset);

        DbList<T> newNode;
        if (cmp > 0)
        {
            if (Next is not null)
            {
                var newNext = Next.RemoveAt(offset - leftCount - 1);
                if (newNext.Empty) newNext = null;
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: newNext,
                    previous: Previous);
            }
            else
            {
                return Previous ?? new DbList<T>();
            }
        }
        else if (cmp < 0)
        {
            if (Previous is not null)
            {
                var prevRemoved = Previous.RemoveAt(offset);
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    next: Next,
                    previous: prevRemoved.Empty ? null : prevRemoved);
            }
            else
            {
                return Next ?? new DbList<T>();
            }
        }
        else
        {
            if (Next is not null && !Next.Empty)
            {
                var firstValue = Next.GetAt(0);
                var newNext = Next.RemoveFirst();
                newNode = new DbList<T>(
                    value: firstValue,
                    empty: false,
                    next: newNext.Empty ? null : newNext,
                    previous: (Previous is not null && !Previous.Empty) ? Previous : null);
            }
            else if (Previous is not null && !Previous.Empty)
            {
                var lastValue = Previous.GetAt(-1);
                var newPrev = Previous.RemoveLast();
                newNode = new DbList<T>(
                    value: lastValue,
                    empty: false,
                    next: null,
                    previous: newPrev.Empty ? null : newPrev);
            }
            else
            {
                return new DbList<T>();
            }
        }

        var result = newNode.Rebalance();
        var newIndexes = Indexes; // Placeholder to remove currentValue from indexes if integrated.
        newIndexes = ObjectRemoveFromIndexes(currentValue);
        
        return new DbList<T>(
            value: result.Value,
            empty: result.Empty,
            next: result.Next,
            previous: result.Previous, 
            indexes: newIndexes);
    }

    /// <summary>
    /// Removes the first element from the list.
    /// </summary>
    /// <returns>A new list with the first element removed.</returns>
    public DbList<T> RemoveFirst()
    {
        if (Empty) return this;

        var currentValue = GetAt(0);

        var newIndexes = Indexes;
        newIndexes = ObjectRemoveFromIndexes(currentValue);
        
        if (Previous is not null && !Previous.Empty)
        {
            var prevRemoved = Previous.RemoveFirst();
            var newNode = new DbList<T>(
                value: Value,
                empty: false,
                next: Next,
                previous: prevRemoved.Empty ? null : prevRemoved);
            var result = newNode.Rebalance();
            return new DbList<T>(
                value: result.Value,
                empty: result.Empty,
                next: result.Next,
                previous: result.Previous, 
                indexes: newIndexes);
        }
        else
        {
            return Next ?? new DbList<T>(indexes: newIndexes);
        }
    }

    /// <summary>
    /// Removes the last element from the list.
    /// </summary>
    /// <returns>A new list with the last element removed.</returns>
    public DbList<T> RemoveLast()
    {
        if (Empty) return this;

        var currentValue = GetAt(-1);

        var newIndexes = ObjectRemoveFromIndexes(currentValue);
        
        if (Next is not null && !Next.Empty)
        {
            var nextRemoved = Next.RemoveLast();
            var newNode = new DbList<T>(
                value: Value,
                empty: false,
                next: nextRemoved.Empty ? null : nextRemoved,
                previous: Previous);
            var result = newNode.Rebalance();
            return new DbList<T>(
                value: result.Value,
                empty: result.Empty,
                next: result.Next,
                previous: result.Previous,
                indexes: newIndexes);
        }
        else
        {
            return Previous ?? new DbList<T>(indexes: newIndexes);
        }
    }

    /// <summary>
    /// Appends all elements of another list to the end of this list.
    /// </summary>
    /// <param name="items">The list of items to append.</param>
    /// <returns>A new list containing the elements of this list followed by the elements of the other list.</returns>
    public DbList<T> Extend(DbList<T>? items)
    {
        var result = this;
        if (items is null || items.Empty) return result;

        foreach (var it in items)
            result = result.AppendLast(it);

        return result;
    }

    /// <summary>
    /// Appends an item to the beginning of the list.
    /// </summary>
    /// <param name="item">The item to append.</param>
    /// <returns>A new list with the item appended to the beginning.</returns>
    public DbList<T> AppendFirst(T? item) => InsertAt(0, item);

    /// <summary>
    /// Appends an item to the end of the list.
    /// </summary>
    /// <param name="item">The item to append.</param>
    /// <returns>A new list with the item appended to the end.</returns>
    public DbList<T> AppendLast(T? item) => InsertAt(Count, item);

    /// <summary>
    /// Returns a new list containing the first <paramref name="upperLimit"/> elements.
    /// </summary>
    /// <param name="upperLimit">The number of elements to take from the beginning of the list.</param>
    /// <returns>A new list containing the specified number of elements from the beginning of the list.</returns>
    public DbList<T> Head(int upperLimit)
    {
        if (upperLimit < 0) upperLimit = Count + upperLimit;
        if (upperLimit < 0) upperLimit = 0;
        if (upperLimit >= Count) upperLimit = Count;

        if (upperLimit == 0) return new DbList<T>();
        if (upperLimit == Count) return this;

        var node = this;
        var offset = node.Previous?.Count ?? 0;
        var cmp = upperLimit - offset;

        if (cmp == 0)
        {
            return node.Previous ?? new DbList<T>();
        }
        else if (cmp > 0 && node.Next is not null)
        {
            var nextNode = node.Next.Head(cmp - 1);
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                next: nextNode.Empty ? null : nextNode,
                previous: node.Previous);
        }
        else if (cmp < 0 && node.Previous is not null)
        {
            node = node.Previous.Head(upperLimit);
        }
        else
        {
            return new DbList<T>();
        }

        return node.Rebalance();
    }

    /// <summary>
    /// Returns a new list containing the elements from the specified lower limit to the end of the list.
    /// </summary>
    /// <param name="lowerLimit">The zero-based index at which to begin the slice.</param>
    /// <returns>A new list containing the elements from the specified lower limit to the end.</returns>
    public DbList<T> Tail(int lowerLimit)
    {
        if (lowerLimit < 0) lowerLimit = Count + lowerLimit;
        if (lowerLimit < 0) lowerLimit = 0;
        if (lowerLimit >= Count) lowerLimit = Count;

        if (lowerLimit == Count) return new DbList<T>();
        if (lowerLimit == 0) return this;

        var node = this;
        var offset = node.Previous?.Count ?? 0;
        var cmp = lowerLimit - offset;

        if (cmp == 0)
        {
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                next: node.Next,
                previous: null);
        }
        else if (cmp > 0 && node.Next is not null)
        {
            node = node.Next.Tail(lowerLimit - offset - 1);
        }
        else if (cmp < 0 && node.Previous is not null)
        {
            var prevNode = node.Previous.Tail(lowerLimit);
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                next: node.Next,
                previous: prevNode.Empty ? null : prevNode);
        }
        else
        {
            return new DbList<T>();
        }

        return node.Rebalance();
    }

    /// <summary>
    /// Returns a slice of the list.
    /// </summary>
    /// <param name="fromOffset">The zero-based index at which to begin the slice.</param>
    /// <param name="toOffset">The zero-based index before which to end the slice.</param>
    /// <returns>A new list that represents the specified slice of the original list.</returns>
    public DbList<T> Slice(int fromOffset, int toOffset)
    {
        if (fromOffset < 0) fromOffset = Count + fromOffset;
        if (fromOffset < 0) fromOffset = 0;
        if (fromOffset >= Count) fromOffset = Count;

        if (toOffset < 0) toOffset = Count + toOffset;
        if (toOffset < 0) toOffset = 0;
        if (toOffset >= Count) toOffset = Count;

        if (fromOffset > toOffset)
        {
            return new DbList<T>(
                value: default,
                empty: true,
                next: null,
                previous: null);
        }

        return Tail(fromOffset).Head(toOffset - fromOffset);
    }

    public void Save()
    {
        if (AtomPointer is null)
        {
            if (!Empty)
            {
                if (Previous is not null)
                    Previous.Save();
                if (Next is not null)
                    Next.Save();
                if (Value is not null && Value is Atom valueAtom)
                    valueAtom.Save();
            }            
            base.Save();
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        if (Empty) yield break;
        if (Previous is not null) 
            foreach (var it in Previous)
                yield return it;
        yield return Value;
        if (Next is not null) 
            foreach (var it in Next)
                yield return it;
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
