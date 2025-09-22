# ProtoBaseNet

ProtoBaseNet is a .NET library that provides a lightweight, transactional, and immutable object database. It's designed for scenarios where you need to manage complex object graphs with history tracking, all while maintaining a functional, immutable approach to data manipulation. This project is a C# port of a similar concept originally implemented in Python.

## Core Concepts

- **Atom**: The fundamental building block. Every object stored in the database is a subclass of `Atom`. Atoms are persisted through a JSON-based serialization mechanism.

- **Immutability**: All database collections (`DbList`, `DbSet`, `DbDictionary`) are immutable. Any modification (like adding or removing an item) returns a *new* instance of the collection, leaving the original untouched. This is achieved through structural sharing to optimize performance and memory usage.

- **Transactions**: All database operations are performed within a transaction (`ObjectTransaction`). Transactions provide an atomic unit of work and ensure that changes are saved correctly.

- **Storage**: The database uses a pluggable storage backend, abstracted by the `SharedStorage` class. A `MemoryStorage` implementation is provided for testing and simple use cases.

- **Collections**: ProtoBaseNet offers a rich set of immutable collections:
    - `DbList<T>`: An ordered list.
    - `DbSet<T>`: An unordered set of unique elements.
    - `DbDictionary<T>`: A key-value dictionary.
    - `DbCountedSet<T>`: A multiset that tracks the number of occurrences of each element.
    - `DbRepeatedKeysDictionary<T>`: A dictionary that can store multiple values for the same key.

## Features

- **Immutable Data Structures**: Collections are persistent and immutable, using structural sharing for efficiency.
- **Transactional Integrity**: Atomic commits and rollbacks for safe data manipulation.
- **Pluggable Storage**: `SharedStorage` abstraction allows for different backend implementations (in-memory is included).
- **Object-Oriented**: Store and retrieve complex object graphs.
- **History and Auditing**: The database maintains a history of root objects, allowing for time-travel queries (though not fully implemented yet).

## Getting Started

### Prerequisites

- .NET 9.0 or later

### Installation

This project is not yet available on NuGet. To use it, you'll need to clone the repository and reference the `ProtoBaseNet` project in your solution.

```bash
git clone https://github.com/your-username/ProtoBaseNet.git
```

## Usage Example

Here's a simple example of how to create a database, add some data, and read it back.

```csharp
using ProtoBaseNet;

// 1. Initialize the storage engine (in-memory for this example)
var storage = new MemoryStorage();
var objectSpace = new ObjectSpace(storage);

// 2. Create a new database
var db = objectSpace.NewDatabase("MyTestDb");

// 3. Start a transaction
using (var transaction = db.NewTransaction())
{
    // 4. Create an immutable list and set it as a root object
    var myList = new DbList<string>().Append("hello").Append("world");
    transaction.SetRootObject("my_list", myList);

    // 5. Commit the transaction
    transaction.Commit();
}

// 6. Read the data back in a new transaction
using (var transaction = db.NewTransaction())
{
    var myList = (DbList<string>)transaction.GetRootObject("my_list");
    foreach (var item in myList.AsIterable())
    {
        Console.WriteLine(item);
    }
}
```

## Documentation

For more detailed information about the architecture and API, please see the `Documentation` folder in this repository.

## Contributing

Contributions are welcome! Please feel free to submit a pull request or open an issue.

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.