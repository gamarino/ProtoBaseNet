using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using System.Text;
using ProtoBaseNet;

// 🔬 Atributos clave:
// MemoryDiagnoser nos dirá cuánta memoria consume cada método. ¡Esencial!
[MemoryDiagnoser] 
public class ListBenchmark
{
    // [Params] permite ejecutar los benchmarks con diferentes valores de entrada.
    [Params(100, 1000, 10000)]
    public int N;

    private string _testString = "hello";

    [Benchmark(Baseline = true)]
    public void FrameworkList()
    {
        var result = new List<string>();
        for (int i = 0; i < N; i++)
        {
            result.Add(i.ToString());
        }
    }

    [Benchmark]
    public void DbListTest()
    {
        var os = new ObjectSpace(new MemoryStorage());
        using (var db = os.NewDatabase("Test"))
        using (var tx = db.NewTransaction())
        {
            var dbList = new DbList<string>(transaction: tx);
            for (int i = 0; i < N; i++)
            {
                dbList = dbList.AppendLast(i.ToString());
            }
            tx.SetRootObject("List", dbList);
            tx.Commit();
        }
    }
}

public class Program
{
    public static void Main(string[] args)
    {
        // 🚀 Esta es la línea que inicia el benchmark.
        BenchmarkRunner.Run<ListBenchmark>();
    }
}
