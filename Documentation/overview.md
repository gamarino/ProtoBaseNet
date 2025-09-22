# ProtoBaseNet Documentation

## Introduction

ProtoBaseNet is a .NET library that provides a lightweight, transactional, and immutable object database. It is designed for applications that need to manage and query complex, evolving object graphs while maintaining a complete history of changes. The design emphasizes immutability and functional programming principles, providing a predictable and robust way to handle application state.

This document provides a detailed overview of the core components and concepts of ProtoBaseNet.

## Core Components

The architecture of ProtoBaseNet revolves around a few key components:

### Atom

The `Atom` class is the abstract base for all objects that can be persisted in the database. It provides the fundamental mechanisms for:

- **Identity**: Each atom, once saved, is assigned an `AtomPointer` that uniquely identifies it in the storage backend.
- **Serialization**: Atoms are serialized to and from JSON. The base `Atom` class handles the logic for converting object properties to a JSON representation and back.
- **Lazy Loading**: Atom data is loaded from storage on-demand the first time it is accessed.

### ObjectSpace

The `ObjectSpace` is the top-level container for the entire database. It manages:

- **Storage**: It is initialized with a `SharedStorage` instance that handles the physical persistence of data.
- **Database Management**: It provides methods for creating, opening, renaming, and deleting databases (`Database` instances).
- **Root History**: It maintains the history of all root object changes, which is the basis for the database's auditing and time-travel capabilities.

### Database

A `Database` represents a named, isolated object graph within an `ObjectSpace`. It is the primary entry point for interacting with your data. Its main responsibility is to manage the root of your object graph and to create transactions.

### ObjectTransaction

All interactions with the database must occur within an `ObjectTransaction`. The transaction provides:

- **ACID-like Properties**: Transactions are atomic. Changes are either committed as a whole or aborted.
- **Scope for Operations**: A transaction holds the context for all read and write operations.
- **Root Object Management**: You can get and set named root objects within a transaction. These root objects act as the entry points to your object graph.

## Storage

### SharedStorage

`SharedStorage` is an abstract class that defines the contract for all storage backends. It provides a simple, asynchronous API for pushing and retrieving atoms and raw byte arrays.

### MemoryStorage

`MemoryStorage` is an in-memory implementation of `SharedStorage`. It is ideal for testing, temporary databases, or single-process applications where durability is not a concern. All data is lost when the process exits.

## Immutable Collections

One of the core features of ProtoBaseNet is its set of immutable collections. When you perform a modification on one of these collections, you get back a new collection instance representing the new state. The original collection remains unchanged. This is implemented efficiently using structural sharing.

### DbList<T>

An immutable, ordered list. It is implemented as a balanced binary tree (AVL-like), which provides O(log N) performance for insertions, deletions, and random access.

### DbSet<T>

An immutable, unordered set of unique elements. It uses a `DbDictionary` internally to store the elements, with a stable hash of the element as the key.

### DbDictionary<T>

An immutable, ordered dictionary. It is implemented as a sorted list with binary search, providing O(log N) for lookups, insertions, and deletions. Keys are ordered deterministically across different types.

### DbCountedSet<T>

A multiset (or bag) that stores elements and their counts. It allows you to add multiple instances of the same element and query how many times each element appears.

### DbRepeatedKeysDictionary<T>

A dictionary that allows multiple values to be associated with the same key. It uses a `DbCountedSet` for each key to store the values.

## Serialization

ProtoBaseNet uses JSON as its serialization format. The `Atom` class contains the logic for converting its fields into a JSON object and for reconstructing the object from JSON. It handles references to other atoms by storing `AtomPointers`.

## Future Development

- **File-based Storage**: A durable, file-based `SharedStorage` implementation is a high-priority next step.
- **Indexing**: While hooks for indexing exist in the collection classes, a full implementation of secondary indexes is needed to support efficient queries on large datasets.
- **Query Language**: A LINQ provider or a custom query language would make it easier to query the database.
- **Time-Travel API**: Exposing a public API to query the database at a specific point in time.
