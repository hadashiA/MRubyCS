using System.Text;
using BenchmarkDotNet.Attributes;

namespace MRubyCS.Benchmark;

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser]
public class SymbolBenchmark
{
    const int Iterations = 1000;

    // 5文字以下・英数字+_ → インラインパック対象 (presym と被らないものを使用)
    static readonly byte[][] ShortPackable = GenerateShort(Iterations, len: 5, packable: true);

    // 6文字以上 → 常に dict 経路
    static readonly byte[][] LongDict = GenerateLong(Iterations, len: 8);

    // 5文字以下だがパックテーブル外 (記号入り) → dict 経路
    static readonly byte[][] ShortNonPackable = GenerateShort(Iterations, len: 5, packable: false);

    SymbolTable freshTable = null!;
    SymbolTable warmTable = null!;
    Symbol[] inlineSymbols = null!;
    Symbol[] dictSymbols = null!;

    [GlobalSetup]
    public void Setup()
    {
        // NameOf 用に事前 intern 済みのテーブル
        warmTable = new SymbolTable();
        inlineSymbols = new Symbol[Iterations];
        dictSymbols = new Symbol[Iterations];
        for (var i = 0; i < Iterations; i++)
        {
            inlineSymbols[i] = warmTable.Intern(ShortPackable[i]);
            dictSymbols[i] = warmTable.Intern(LongDict[i]);
        }
        // NameOf キャッシュをウォームアップ
        for (var i = 0; i < Iterations; i++)
        {
            _ = warmTable.NameOf(inlineSymbols[i]);
        }
    }

    [IterationSetup(Targets = [nameof(Intern_Fresh_ShortPackable), nameof(Intern_Fresh_LongDict), nameof(Intern_Fresh_ShortNonPackable)])]
    public void IterationSetupFresh()
    {
        freshTable = new SymbolTable();
    }

    // ====== Intern: 既存シンボル (hot lookup path) ======

    [Benchmark]
    public ulong Intern_Existing_ShortPackable()
    {
        ulong sum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            sum += warmTable.Intern(ShortPackable[i]).Value;
        }
        return sum;
    }

    [Benchmark]
    public ulong Intern_Existing_LongDict()
    {
        ulong sum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            sum += warmTable.Intern(LongDict[i]).Value;
        }
        return sum;
    }

    // ====== Intern: 新規シンボル (insert/allocate path) ======

    [Benchmark]
    public ulong Intern_Fresh_ShortPackable()
    {
        ulong sum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            sum += freshTable.Intern(ShortPackable[i]).Value;
        }
        return sum;
    }

    [Benchmark]
    public ulong Intern_Fresh_LongDict()
    {
        ulong sum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            sum += freshTable.Intern(LongDict[i]).Value;
        }
        return sum;
    }

    [Benchmark]
    public ulong Intern_Fresh_ShortNonPackable()
    {
        ulong sum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            sum += freshTable.Intern(ShortNonPackable[i]).Value;
        }
        return sum;
    }

    // ====== NameOf ======

    [Benchmark]
    public int NameOf_Inline()
    {
        var sum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            sum += warmTable.NameOf(inlineSymbols[i]).Length;
        }
        return sum;
    }

    [Benchmark]
    public int NameOf_Dict()
    {
        var sum = 0;
        for (var i = 0; i < Iterations; i++)
        {
            sum += warmTable.NameOf(dictSymbols[i]).Length;
        }
        return sum;
    }

    // ---- helpers ----

    static byte[][] GenerateShort(int count, int len, bool packable)
    {
        // 英数字 + _ を回しながら衝突しない短い文字列を生成
        const string packableChars = "_abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        const string nonPackableChars = "!?*+-/<>="; // パックテーブル外
        var pool = packable ? packableChars : nonPackableChars;

        var result = new byte[count][];
        var sb = new StringBuilder(len);
        for (var i = 0; i < count; i++)
        {
            sb.Clear();
            var seed = i;
            for (var j = 0; j < len; j++)
            {
                sb.Append(pool[seed % pool.Length]);
                seed = seed / pool.Length + j * 7 + 13;
            }
            result[i] = Encoding.UTF8.GetBytes(sb.ToString());
        }
        return result;
    }

    static byte[][] GenerateLong(int count, int len)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_";
        var result = new byte[count][];
        var sb = new StringBuilder(len);
        for (var i = 0; i < count; i++)
        {
            sb.Clear();
            var seed = i;
            for (var j = 0; j < len; j++)
            {
                sb.Append(chars[seed % chars.Length]);
                seed = seed / chars.Length + j * 11 + 7;
            }
            result[i] = Encoding.UTF8.GetBytes(sb.ToString());
        }
        return result;
    }
}