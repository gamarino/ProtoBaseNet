namespace ProtoBaseNet;

using System;
using System.Collections.Generic;

// Persistent, immutable list implemented as an AVL-like binary tree.
// Key characteristics:
// - Structural sharing: all mutating operations return a new DbList<T> instance,
//   reusing unchanged subtrees to minimize allocations.
// - Balanced tree: rotations keep height logarithmic, enabling O(log N) positional
//   access (GetAt/SetAt/InsertAt/RemoveAt) and O(N) in-order iteration.
// - Optional index container is plumbed through but not actively maintained;
//   index updates can be connected where placeholders are indicated.
public class DbList<T> : DbCollection
{
    // Empty flag: true for a sentinel/empty node; false for a concrete node with Value.
    public bool Empty { get; init; } = true;

    // Node payload when not empty.
    public T? Value { get; init; }

    // Cached height of the subtree rooted at this node (0 for Empty).
    public int Height { get; init; }

    // Right child.
    public DbList<T>? Next { get; init; }

    // Left child.
    public DbList<T>? Previous { get; init; }

    // Optional: container of indexes (placeholder to mirror the reference design).
    private DbDictionary<object>? _indexes;
    public DbDictionary<object>? Indexes => _indexes;

    // Total number of elements in the subtree (0 for Empty).
    public int Count { get; init; }

    // Constructs a node, normalizing empty children to null and computing derived fields (Count, Height, Empty).
    public DbList(
        T? value = default,
        bool empty = true,
        DbList<T>? next = null,
        DbList<T>? previous = null,
        DbDictionary<object>? indexes = null)
    {
        // Normalize "empty" children to null to simplify tree logic.
        if (previous is { Empty: true }) previous = null;
        if (next is { Empty: true }) next = null;

        Value = value;
        Next = next;
        Previous = previous;

        var hasContent = (value is not null) || previous is not null || next is not null;
        Empty = hasContent ? false : empty;

        _indexes = indexes;

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

    // Creates a new node with the provided properties while carrying forward indexes by default.
    private DbList<T> With(
        T? value,
        bool empty,
        DbList<T>? previous,
        DbList<T>? next,
        DbDictionary<object>? indexes = null)
    {
        return new DbList<T>(
            value: value,
            empty: empty,
            previous: previous,
            next: next,
            indexes: indexes ?? _indexes
        );
    }

    // In-order enumeration of all values.
    public IEnumerable<T?> AsIterable()
    {
        foreach (var v in Scan(this))
            yield return v;

        static IEnumerable<T?> Scan(DbList<T> node)
        {
            if (node.Previous is not null)
            {
                foreach (var x in Scan(node.Previous))
                    yield return x;
            }
            if (!node.Empty)
                yield return node.Value;
            if (node.Next is not null)
            {
                foreach (var x in Scan(node.Next))
                    yield return x;
            }
        }
    }

    // Returns the value at an index; supports negative offsets (from the end).
    // O(log N) due to subtree counts cached at each node.
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

    // Balance factor = height(right) - height(left). Used to decide rotations.
    private int BalanceFactor()
    {
        if (Empty) return 0;
        var hL = Previous?.Height ?? 0;
        var hR = Next?.Height ?? 0;
        return hR - hL;
    }

    // Right rotation around this node. Returns the new subtree root.
    private DbList<T> RightRotation()
    {
        if (Previous is null) return this;

        var newRight = new DbList<T>(
            value: Value,
            empty: false,
            previous: Previous.Next,
            next: Next,
            indexes: _indexes
        );

        return new DbList<T>(
            value: Previous.Value,
            empty: false,
            previous: Previous.Previous,
            next: newRight,
            indexes: _indexes
        );
    }

    // Left rotation around this node. Returns the new subtree root.
    private DbList<T> LeftRotation()
    {
        if (Next is null) return this;

        var newLeft = new DbList<T>(
            value: Value,
            empty: false,
            previous: Previous,
            next: Next.Previous,
            indexes: _indexes
        );

        return new DbList<T>(
            value: Next.Value,
            empty: false,
            previous: newLeft,
            next: Next.Next,
            indexes: _indexes
        );
    }

    // Rebalances the subtree by recursively rebalancing heavy children and
    // applying single/double rotations based on the local balance factor.
    private DbList<T> Rebalance()
    {
        var node = this;

        while (node.Previous is not null && !(node.Previous.BalanceFactor() is >= -1 and <= 1))
        {
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                previous: node.Previous.Rebalance(),
                next: node.Next,
                indexes: _indexes
            );
        }

        while (node.Next is not null && !(node.Next.BalanceFactor() is >= -1 and <= 1))
        {
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                previous: node.Previous,
                next: node.Next.Rebalance(),
                indexes: _indexes
            );
        }

        var balance = node.BalanceFactor();

        if (balance < -1)
        {
            if (node.Previous is not null && node.Previous.BalanceFactor() > 0)
            {
                node = new DbList<T>(
                    value: node.Value,
                    empty: false,
                    previous: node.Previous.LeftRotation(),
                    next: node.Next,
                    indexes: _indexes
                );
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
                    previous: node.Previous,
                    next: node.Next.RightRotation(),
                    indexes: _indexes
                );
            }
            return node.LeftRotation();
        }

        return node;
    }

    // Sets the value at an index or appends at Count if offset equals Count.
    // Returns a new rebalanced list.
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
                    previous: Previous,
                    next: Next.SetAt(offset - leftCount - 1, value),
                    indexes: _indexes
                );
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    previous: Previous,
                    next: new DbList<T>(value: value, empty: false, indexes: _indexes),
                    indexes: _indexes
                );
            }
        }
        else if (cmp < 0)
        {
            if (Previous is not null)
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    previous: Previous.SetAt(offset, value),
                    next: Next,
                    indexes: _indexes
                );
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    previous: new DbList<T>(value: value, empty: false, indexes: _indexes),
                    next: Next,
                    indexes: _indexes
                );
            }
        }
        else
        {
            newNode = new DbList<T>(
                value: value,
                empty: false,
                previous: Previous,
                next: Next,
                indexes: _indexes
            );
        }

        var result = newNode.Rebalance();

        var newIndexes = _indexes; // Placeholder: integrate index updates here if needed.

        return new DbList<T>(
            value: result.Value,
            empty: result.Empty,
            previous: result.Previous,
            next: result.Next,
            indexes: newIndexes
        );
    }

    // Inserts a value at a given position, shifting subsequent items right.
    // Supports negative offsets; out-of-range values are clamped to [0, Count].
    public DbList<T> InsertAt(int offset, T? value)
    {
        if (offset < 0) offset = Count + offset;
        if (offset < 0) offset = 0;
        if (offset >= Count) offset = Count;

        if (Empty)
            return new DbList<T>(value: value, empty: false, indexes: _indexes);

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
                    previous: Previous,
                    next: Next.InsertAt(cmp - 1, value),
                    indexes: _indexes
                );
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    previous: Previous,
                    next: new DbList<T>(value: value, empty: false, indexes: _indexes),
                    indexes: _indexes
                );
            }
        }
        else if (cmp < 0)
        {
            if (Previous is not null)
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    previous: Previous.InsertAt(offset, value),
                    next: Next,
                    indexes: _indexes
                );
            }
            else
            {
                newNode = new DbList<T>(
                    value: Value,
                    empty: false,
                    previous: new DbList<T>(value: value, empty: false, indexes: _indexes),
                    next: Next,
                    indexes: _indexes
                );
            }
        }
        else
        {
            // Insert before the current value by splitting the node.
            newNode = new DbList<T>(
                value: value,
                empty: false,
                previous: Previous,
                next: new DbList<T>(value: Value, empty: false, previous: null, next: Next, indexes: _indexes),
                indexes: _indexes
            );
        }

        var result = newNode.Rebalance();
        var newIndexes = _indexes; // Placeholder for index updates.

        return new DbList<T>(
            value: result.Value,
            empty: result.Empty,
            previous: result.Previous,
            next: result.Next,
            indexes: newIndexes
        );
    }

    // Removes the value at a given index and returns a rebalanced list.
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
                    previous: Previous,
                    next: newNext,
                    indexes: _indexes
                );
            }
            else
            {
                return Previous ?? new DbList<T>(indexes: _indexes);
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
                    previous: prevRemoved.Empty ? null : prevRemoved,
                    next: Next,
                    indexes: _indexes
                );
            }
            else
            {
                return Next ?? new DbList<T>(indexes: _indexes);
            }
        }
        else
        {
            // Remove this node by promoting successor or predecessor.
            if (Next is not null && !Next.Empty)
            {
                var firstValue = Next.GetAt(0);
                var newNext = Next.RemoveFirst();
                newNode = new DbList<T>(
                    value: firstValue,
                    empty: false,
                    previous: (Previous is not null && !Previous.Empty) ? Previous : null,
                    next: newNext.Empty ? null : newNext,
                    indexes: _indexes
                );
            }
            else if (Previous is not null && !Previous.Empty)
            {
                var lastValue = Previous.GetAt(-1);
                var newPrev = Previous.RemoveLast();
                newNode = new DbList<T>(
                    value: lastValue,
                    empty: false,
                    previous: newPrev.Empty ? null : newPrev,
                    next: null,
                    indexes: _indexes
                );
            }
            else
            {
                // Leaf removal yields an empty list.
                return new DbList<T>(indexes: _indexes);
            }
        }

        var result = newNode.Rebalance();
        var newIndexes = _indexes; // Placeholder to remove currentValue from indexes if integrated.

        return new DbList<T>(
            value: result.Value,
            empty: result.Empty,
            previous: result.Previous,
            next: result.Next,
            indexes: newIndexes
        );
    }

    // Removes the first element and returns the updated list.
    public DbList<T> RemoveFirst()
    {
        if (Empty) return this;

        var currentValue = GetAt(0);

        if (Previous is not null && !Previous.Empty)
        {
            var prevRemoved = Previous.RemoveFirst();
            var newNode = new DbList<T>(
                value: Value,
                empty: false,
                previous: prevRemoved.Empty ? null : prevRemoved,
                next: Next,
                indexes: _indexes
            );
            var result = newNode.Rebalance();
            var newIndexes = _indexes; // Placeholder
            return new DbList<T>(
                value: result.Value,
                empty: result.Empty,
                previous: result.Previous,
                next: result.Next,
                indexes: newIndexes
            );
        }
        else
        {
            return Next ?? new DbList<T>(indexes: _indexes);
        }
    }

    // Removes the last element and returns the updated list.
    public DbList<T> RemoveLast()
    {
        if (Empty) return this;

        var currentValue = GetAt(-1);

        if (Next is not null && !Next.Empty)
        {
            var nextRemoved = Next.RemoveLast();
            var newNode = new DbList<T>(
                value: Value,
                empty: false,
                previous: Previous,
                next: nextRemoved.Empty ? null : nextRemoved,
                indexes: _indexes
            );
            var result = newNode.Rebalance();
            var newIndexes = _indexes; // Placeholder
            return new DbList<T>(
                value: result.Value,
                empty: result.Empty,
                previous: result.Previous,
                next: result.Next,
                indexes: newIndexes
            );
        }
        else
        {
            return Previous ?? new DbList<T>(indexes: _indexes);
        }
    }

    // Appends all elements of another list (in-order) to the end of this list.
    public DbList<T> Extend(DbList<T>? items)
    {
        var result = this;
        if (items is null || items.Empty) return result;

        foreach (var it in items.AsIterable())
            result = result.InsertAt(result.Count, it);

        return result;
    }

    // Convenience appenders.
    public DbList<T> AppendFirst(T? item) => InsertAt(0, item);
    public DbList<T> AppendLast(T? item) => InsertAt(Count, item);

    // Returns the prefix of the list up to upperLimit (exclusive).
    public DbList<T> Head(int upperLimit)
    {
        if (upperLimit < 0) upperLimit = Count + upperLimit;
        if (upperLimit < 0) upperLimit = 0;
        if (upperLimit >= Count) upperLimit = Count;

        if (upperLimit == 0) return new DbList<T>(indexes: _indexes);
        if (upperLimit == Count) return this;

        var node = this;
        var offset = node.Previous?.Count ?? 0;
        var cmp = upperLimit - offset;

        if (cmp == 0)
        {
            return node.Previous ?? new DbList<T>(indexes: _indexes);
        }
        else if (cmp > 0 && node.Next is not null)
        {
            var nextNode = node.Next.Head(cmp - 1);
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                previous: node.Previous,
                next: nextNode.Empty ? null : nextNode,
                indexes: _indexes
            );
        }
        else if (cmp < 0 && node.Previous is not null)
        {
            node = node.Previous.Head(upperLimit);
        }
        else
        {
            return new DbList<T>(indexes: _indexes);
        }

        return node.Rebalance();
    }

    // Returns the suffix of the list starting at lowerLimit (inclusive).
    public DbList<T> Tail(int lowerLimit)
    {
        if (lowerLimit < 0) lowerLimit = Count + lowerLimit;
        if (lowerLimit < 0) lowerLimit = 0;
        if (lowerLimit >= Count) lowerLimit = Count;

        if (lowerLimit == Count) return new DbList<T>(indexes: _indexes);
        if (lowerLimit == 0) return this;

        var node = this;
        var offset = node.Previous?.Count ?? 0;
        var cmp = lowerLimit - offset;

        if (cmp == 0)
        {
            node = new DbList<T>(
                value: node.Value,
                empty: false,
                previous: null,
                next: node.Next,
                indexes: _indexes
            );
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
                previous: prevNode.Empty ? null : prevNode,
                next: node.Next,
                indexes: _indexes
            );
        }
        else
        {
            return new DbList<T>(indexes: _indexes);
        }

        return node.Rebalance();
    }

    // Returns a sublist in [fromOffset, toOffset) (start inclusive, end exclusive).
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
                previous: null,
                next: null,
                indexes: _indexes
            );
        }

        return Tail(fromOffset).Head(toOffset - fromOffset);
    }

    // Minimal placeholder hooks for index integration, mirroring the reference template.
    // Connect these to a real indexing infrastructure if/when available.
    private DbDictionary<object>? Add2Indexes(T? value) => _indexes;
    private DbDictionary<object>? RemoveFromIndexes(T? value) => _indexes;
}