namespace MRubyCS.Tests;

[TestFixture]
public class SymbolTest
{
    [Test]
    public void InlinePack()
    {
        var symbolTable = new SymbolTable();

        var sym = symbolTable.Intern("call"u8);
        var name = symbolTable.NameOf(sym);

        Assert.That(name.SequenceEqual("call"u8), Is.True);
        // "call" は presym なので presym と一致する
        Assert.That(symbolTable.Intern("call"u8), Is.EqualTo(Names.Call));
    }

    [Test]
    public void InlinePack_NonPresym_RoundTrip()
    {
        var symbolTable = new SymbolTable();

        // 5文字以下、英数+_ → インラインパック対象
        var sym = symbolTable.Intern("abc"u8);
        Assert.That(sym.Value, Is.GreaterThanOrEqualTo(1u << 24));
        Assert.That(symbolTable.NameOf(sym).SequenceEqual("abc"u8), Is.True);
        // Intern が同じ値を返す
        Assert.That(symbolTable.Intern("abc"u8), Is.EqualTo(sym));
    }

    [Test]
    public void InlinePack_BoundaryUnderscore()
    {
        var symbolTable = new SymbolTable();

        var sym = symbolTable.Intern("_"u8);
        Assert.That(sym.Value, Is.EqualTo(1u << 24));
        Assert.That(symbolTable.NameOf(sym).SequenceEqual("_"u8), Is.True);
    }

    [Test]
    public void InlinePack_FiveChars_Max()
    {
        var symbolTable = new SymbolTable();

        var sym = symbolTable.Intern("abcde"u8);
        Assert.That(symbolTable.NameOf(sym).SequenceEqual("abcde"u8), Is.True);
    }

    [Test]
    public void InlinePack_NotApplicable_Falls_Back_To_Dict()
    {
        var symbolTable = new SymbolTable();

        // 6文字 (PackLengthMax 超過)
        var sym = symbolTable.Intern("abcdef"u8);
        Assert.That(sym.Value, Is.LessThan(1u << 24));
        Assert.That(symbolTable.NameOf(sym).SequenceEqual("abcdef"u8), Is.True);

        // パックテーブル外文字 (記号)
        var sym2 = symbolTable.Intern("a-b"u8);
        Assert.That(sym2.Value, Is.LessThan(1u << 24));
        Assert.That(symbolTable.NameOf(sym2).SequenceEqual("a-b"u8), Is.True);
    }
}
