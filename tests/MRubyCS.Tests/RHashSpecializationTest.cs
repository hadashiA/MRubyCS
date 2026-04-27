namespace MRubyCS.Tests;

[TestFixture]
public class RHashSpecializationTest
{
    static MRubyState NewState() => MRubyState.Create();

    [Test]
    public void Default_Backend_Is_SymbolKeyed()
    {
        var state = NewState();
        var h = state.NewHash(4);
        Assert.That(h.backend, Is.TypeOf<RHashSymbolKeyedBackend>());
    }

    [Test]
    public void All_Symbol_Keys_Stay_Specialized()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym1 = state.Intern("foo"u8);
        var sym2 = state.Intern("bar"u8);
        h[new MRubyValue(sym1)] = new MRubyValue(1L);
        h[new MRubyValue(sym2)] = new MRubyValue(2L);

        Assert.That(h.backend, Is.TypeOf<RHashSymbolKeyedBackend>());
        Assert.That(h[new MRubyValue(sym1)].FixnumValue, Is.EqualTo(1));
        Assert.That(h[new MRubyValue(sym2)].FixnumValue, Is.EqualTo(2));
    }

    [Test]
    public void Mixed_Keys_Demote()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym = state.Intern("foo"u8);
        h[new MRubyValue(sym)] = new MRubyValue(1L);
        Assert.That(h.backend, Is.TypeOf<RHashSymbolKeyedBackend>());

        h[new MRubyValue(42L)] = new MRubyValue(99L);
        Assert.That(h.backend, Is.TypeOf<RHashGenericBackend>());

        Assert.That(h[new MRubyValue(sym)].FixnumValue, Is.EqualTo(1));
        Assert.That(h[new MRubyValue(42L)].FixnumValue, Is.EqualTo(99));
        Assert.That(h.Length, Is.EqualTo(2));
    }

    [Test]
    public void TryGetValue_NonSymbol_Key_In_SymbolKeyed_Returns_False()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym = state.Intern("foo"u8);
        h[new MRubyValue(sym)] = new MRubyValue(1L);

        Assert.That(h.TryGetValue(new MRubyValue(42L), out _), Is.False);
        Assert.That(h.backend, Is.TypeOf<RHashSymbolKeyedBackend>());
    }

    [Test]
    public void Keys_Materializes_Cache_For_SymbolKeyed()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym1 = state.Intern("foo"u8);
        var sym2 = state.Intern("bar"u8);
        h[new MRubyValue(sym1)] = new MRubyValue(1L);
        h[new MRubyValue(sym2)] = new MRubyValue(2L);

        var keys = h.Keys;
        Assert.That(keys.Length, Is.EqualTo(2));
        Assert.That(keys[0].SymbolValue, Is.EqualTo(sym1));
        Assert.That(keys[1].SymbolValue, Is.EqualTo(sym2));
    }

    [Test]
    public void Keys_Cache_Invalidates_On_Insert()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym1 = state.Intern("foo"u8);
        var sym2 = state.Intern("bar"u8);
        var sym3 = state.Intern("baz"u8);
        h[new MRubyValue(sym1)] = new MRubyValue(1L);
        h[new MRubyValue(sym2)] = new MRubyValue(2L);

        var firstView = h.Keys;
        Assert.That(firstView.Length, Is.EqualTo(2));

        h[new MRubyValue(sym3)] = new MRubyValue(3L);
        var secondView = h.Keys;
        Assert.That(secondView.Length, Is.EqualTo(3));
    }

    [Test]
    public void TryDelete_Reindexes_Specialized()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym1 = state.Intern("a"u8);
        var sym2 = state.Intern("b"u8);
        var sym3 = state.Intern("c"u8);
        h[new MRubyValue(sym1)] = new MRubyValue(1L);
        h[new MRubyValue(sym2)] = new MRubyValue(2L);
        h[new MRubyValue(sym3)] = new MRubyValue(3L);

        Assert.That(h.TryDelete(new MRubyValue(sym2), out var deleted), Is.True);
        Assert.That(deleted.FixnumValue, Is.EqualTo(2));
        Assert.That(h.Length, Is.EqualTo(2));
        Assert.That(h[new MRubyValue(sym1)].FixnumValue, Is.EqualTo(1));
        Assert.That(h[new MRubyValue(sym3)].FixnumValue, Is.EqualTo(3));
        Assert.That(h.backend, Is.TypeOf<RHashSymbolKeyedBackend>());
    }

    [Test]
    public void Iteration_Order_Matches_Insertion_Specialized()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym1 = state.Intern("alpha"u8);
        var sym2 = state.Intern("beta"u8);
        var sym3 = state.Intern("gamma"u8);
        h[new MRubyValue(sym1)] = new MRubyValue(10L);
        h[new MRubyValue(sym2)] = new MRubyValue(20L);
        h[new MRubyValue(sym3)] = new MRubyValue(30L);

        var seen = new List<(Symbol, long)>();
        foreach (var kv in h)
        {
            seen.Add((kv.Key.SymbolValue, kv.Value.FixnumValue));
        }
        Assert.That(seen, Has.Count.EqualTo(3));
        Assert.That(seen[0], Is.EqualTo((sym1, 10L)));
        Assert.That(seen[1], Is.EqualTo((sym2, 20L)));
        Assert.That(seen[2], Is.EqualTo((sym3, 30L)));
    }

    [Test]
    public void Add_Throws_On_Duplicate_Specialized()
    {
        var state = NewState();
        var h = state.NewHash(4);
        var sym = state.Intern("dup"u8);
        h.Add(new MRubyValue(sym), new MRubyValue(1L));
        Assert.Throws<InvalidOperationException>(() =>
            h.Add(new MRubyValue(sym), new MRubyValue(2L)));
    }

    [Test]
    public void Merge_Preserves_Mode_When_All_Symbols()
    {
        var state = NewState();
        var a = state.NewHash(4);
        var b = state.NewHash(4);
        var s1 = state.Intern("k1"u8);
        var s2 = state.Intern("k2"u8);
        a[new MRubyValue(s1)] = new MRubyValue(1L);
        b[new MRubyValue(s2)] = new MRubyValue(2L);

        a.Merge(b);
        Assert.That(a.backend, Is.TypeOf<RHashSymbolKeyedBackend>());
        Assert.That(a.Length, Is.EqualTo(2));
        Assert.That(a[new MRubyValue(s1)].FixnumValue, Is.EqualTo(1));
        Assert.That(a[new MRubyValue(s2)].FixnumValue, Is.EqualTo(2));
    }

    [Test]
    public void Equivalent_Generic_And_SymbolKeyed_Behave_The_Same()
    {
        var state = NewState();
        var generic = state.NewHash(4);
        var symKeyed = state.NewHash(4);
        var s1 = state.Intern("foo"u8);
        var s2 = state.Intern("bar"u8);

        // Force generic into Generic backend by inserting a non-symbol first.
        generic[new MRubyValue(7L)] = new MRubyValue(0L);
        generic.TryDelete(new MRubyValue(7L), out _);
        Assert.That(generic.backend, Is.TypeOf<RHashGenericBackend>());

        generic[new MRubyValue(s1)] = new MRubyValue(1L);
        generic[new MRubyValue(s2)] = new MRubyValue(2L);

        symKeyed[new MRubyValue(s1)] = new MRubyValue(1L);
        symKeyed[new MRubyValue(s2)] = new MRubyValue(2L);

        Assert.That(generic.backend, Is.TypeOf<RHashGenericBackend>());
        Assert.That(symKeyed.backend, Is.TypeOf<RHashSymbolKeyedBackend>());
        Assert.That(generic.Length, Is.EqualTo(symKeyed.Length));
        Assert.That(generic[new MRubyValue(s1)], Is.EqualTo(symKeyed[new MRubyValue(s1)]));
        Assert.That(generic[new MRubyValue(s2)], Is.EqualTo(symKeyed[new MRubyValue(s2)]));
    }
}
