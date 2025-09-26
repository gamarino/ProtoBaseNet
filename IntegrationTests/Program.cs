using ProtoBaseNet;
using System;
using System.IO;
using System.Linq;

public class IntegrationTests
{
    private const string FilePath = "integration_test.db";

    public static void Main(string[] args)
    {
        Console.WriteLine("Running integration tests...");
        TestFileStorage().Wait();
        Console.WriteLine("Integration tests passed!");
    }

    public static async Task TestFileStorage()
    {
        // 1. Create a FileStorage instance
        var storage = new FileStorage(FilePath);
        var objectSpace = new ObjectSpace(storage);

        // 2. Create a new database
        var db = objectSpace.NewDatabase("MyTestDb");

        // 3. Start a transaction
        using (var transaction = db.NewTransaction())
        {
            // 4. Create an immutable list and set it as a root object
            var myList = new DbList<string>();
            myList = myList.AppendLast("hello");
            myList = myList.AppendLast("world");
            transaction.SetRootObject("my_list", myList);

            // 5. Commit the transaction
            transaction.Commit();
        }

        // 6. Close the ObjectSpace
        objectSpace.Close();

        // 7. Reopen the ObjectSpace
        storage = new FileStorage(FilePath);
        objectSpace = new ObjectSpace(storage);
        db = objectSpace.OpenDatabase("MyTestDb");

        // 8. Read the data back in a new transaction
        using (var transaction = db.NewTransaction())
        {
            var myList = (DbList<string>)transaction.GetRootObject("my_list")!;
            var items = myList.ToList();
            if (items.Count != 2 || items[0] != "hello" || items[1] != "world")
            {
                throw new Exception("Data verification failed");
            }
        }

        // Clean up
        if (File.Exists(FilePath))
        {
            File.Delete(FilePath);
        }
        if (File.Exists($"{FilePath}.root"))
        {
            File.Delete($"{FilePath}.root");
        }
    }
}