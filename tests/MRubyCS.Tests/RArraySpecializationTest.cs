namespace MRubyCS.Tests;

[TestFixture]
public class RArraySpecializationTest
{
    static MRubyState NewState() => MRubyState.Create();

    static MRubyValue[] Fixnums(params long[] xs)
    {
        var result = new MRubyValue[xs.Length];
        for (var i = 0; i < xs.Length; i++) result[i] = new MRubyValue(xs[i]);
        return result;
    }

    static MRubyValue[] Floats(params double[] xs)
    {
        var result = new MRubyValue[xs.Length];
        for (var i = 0; i < xs.Length; i++) result[i] = new MRubyValue(xs[i]);
        return result;
    }

    [Test]
    public void Promotes_Fixnum_On_Construct()
    {
        var state = NewState();
        var arr = state.NewArray(Fixnums(1, 2, 3, 4, 5));
        Assert.That(arr.backend, Is.TypeOf<RArrayFixnumBackend>());
        for (var i = 0; i < 5; i++)
        {
            Assert.That(arr[i].FixnumValue, Is.EqualTo(i + 1));
        }
    }

    [Test]
    public void Promotes_Float_On_Construct()
    {
        var state = NewState();
        var arr = state.NewArray(Floats(1.5, 2.5, 3.5));
        Assert.That(arr.backend, Is.TypeOf<RArrayFloatBackend>());
        Assert.That(arr[0].FloatValue, Is.EqualTo(1.5));
        Assert.That(arr[1].FloatValue, Is.EqualTo(2.5));
        Assert.That(arr[2].FloatValue, Is.EqualTo(3.5));
    }

    [Test]
    public void Stays_Generic_For_Mixed()
    {
        var state = NewState();
        var sym = state.Intern("hello"u8);
        var values = new[] { new MRubyValue(1L), new MRubyValue(sym) };
        var arr = state.NewArray(values);
        Assert.That(arr.backend, Is.TypeOf<RArrayObjectBackend>());
    }

    [Test]
    public void Empty_Construct_Is_Generic()
    {
        var state = NewState();
        var arr = state.NewArray(System.ReadOnlySpan<MRubyValue>.Empty);
        Assert.That(arr.backend, Is.TypeOf<RArrayObjectBackend>());
    }

    [Test]
    public void Push_Same_Type_Stays_Specialized()
    {
        var state = NewState();
        var arr = state.NewArray(Fixnums(1, 2, 3));
        arr.Push(new MRubyValue(4L));
        Assert.That(arr.backend, Is.TypeOf<RArrayFixnumBackend>());
        Assert.That(arr[3].FixnumValue, Is.EqualTo(4));
    }

    [Test]
    public void Push_Different_Type_Demotes()
    {
        var state = NewState();
        var sym = state.Intern("x"u8);
        var arr = state.NewArray(Fixnums(1, 2, 3));
        arr.Push(new MRubyValue(sym));
        Assert.That(arr.backend, Is.TypeOf<RArrayObjectBackend>());
        Assert.That(arr[0].FixnumValue, Is.EqualTo(1));
        Assert.That(arr[1].FixnumValue, Is.EqualTo(2));
        Assert.That(arr[2].FixnumValue, Is.EqualTo(3));
        Assert.That(arr[3].SymbolValue, Is.EqualTo(sym));
    }

    [Test]
    public void IndexerSet_Mismatch_Demotes_And_Preserves_Prior()
    {
        var state = NewState();
        var sym = state.Intern("z"u8);
        var arr = state.NewArray(Fixnums(10, 20, 30));
        arr[1] = new MRubyValue(sym);
        Assert.That(arr.backend, Is.TypeOf<RArrayObjectBackend>());
        Assert.That(arr[0].FixnumValue, Is.EqualTo(10));
        Assert.That(arr[1].SymbolValue, Is.EqualTo(sym));
        Assert.That(arr[2].FixnumValue, Is.EqualTo(30));
    }

    [Test]
    public void AsSpan_Demotes_And_Returns_Live_Span()
    {
        var state = NewState();
        var arr = state.NewArray(Fixnums(1, 2, 3));
        Assert.That(arr.backend, Is.TypeOf<RArrayFixnumBackend>());

        var span = arr.AsSpan();
        Assert.That(arr.backend, Is.TypeOf<RArrayObjectBackend>());

        // Mutating through the span must reflect back through the indexer.
        span[0] = MRubyValue.Nil;
        Assert.That(arr[0].IsNil, Is.True);
        Assert.That(arr[1].FixnumValue, Is.EqualTo(2));
    }

    [Test]
    public void Subsequence_Of_Specialized_Stays_Specialized()
    {
        var state = NewState();
        var arr = state.NewArray(Fixnums(10, 20, 30, 40, 50));
        var sub = arr.SubSequence(1, 3);
        Assert.That(sub.backend, Is.TypeOf<RArrayFixnumBackend>());
        Assert.That(sub.Length, Is.EqualTo(3));
        Assert.That(sub[0].FixnumValue, Is.EqualTo(20));
        Assert.That(sub[1].FixnumValue, Is.EqualTo(30));
        Assert.That(sub[2].FixnumValue, Is.EqualTo(40));
    }

    [Test]
    public void CoW_Child_Demote_Does_Not_Affect_Parent()
    {
        var state = NewState();
        var sym = state.Intern("y"u8);
        var parent = state.NewArray(Fixnums(1, 2, 3, 4));
        var child = parent.SubSequence(0, 4);

        // Mutate the child with a non-fixnum -> child demotes.
        child[2] = new MRubyValue(sym);

        Assert.That(child.backend, Is.TypeOf<RArrayObjectBackend>());
        Assert.That(parent.backend, Is.TypeOf<RArrayFixnumBackend>());
        Assert.That(parent[2].FixnumValue, Is.EqualTo(3));
        Assert.That(child[2].SymbolValue, Is.EqualTo(sym));
    }

    [Test]
    public void Concat_Same_Mode_Preserves_Mode()
    {
        var state = NewState();
        var a = state.NewArray(Fixnums(1, 2));
        var b = state.NewArray(Fixnums(3, 4));
        a.Concat(b);
        Assert.That(a.backend, Is.TypeOf<RArrayFixnumBackend>());
        Assert.That(a.Length, Is.EqualTo(4));
        Assert.That(a[0].FixnumValue, Is.EqualTo(1));
        Assert.That(a[3].FixnumValue, Is.EqualTo(4));
    }

    [Test]
    public void Concat_Mixed_Modes_Demotes()
    {
        var state = NewState();
        var a = state.NewArray(Fixnums(1, 2));
        var b = state.NewArray(Floats(3.0, 4.0));
        a.Concat(b);
        Assert.That(a.backend, Is.TypeOf<RArrayObjectBackend>());
        Assert.That(a.Length, Is.EqualTo(4));
        Assert.That(a[0].FixnumValue, Is.EqualTo(1));
        Assert.That(a[2].FloatValue, Is.EqualTo(3.0));
        Assert.That(a[3].FloatValue, Is.EqualTo(4.0));
    }

    [Test]
    public void Concat_Self_Aliasing()
    {
        var state = NewState();
        var a = state.NewArray(Fixnums(1, 2, 3));
        a.Concat(a);
        Assert.That(a.Length, Is.EqualTo(6));
        Assert.That(a[0].FixnumValue, Is.EqualTo(1));
        Assert.That(a[1].FixnumValue, Is.EqualTo(2));
        Assert.That(a[2].FixnumValue, Is.EqualTo(3));
        Assert.That(a[3].FixnumValue, Is.EqualTo(1));
        Assert.That(a[4].FixnumValue, Is.EqualTo(2));
        Assert.That(a[5].FixnumValue, Is.EqualTo(3));
    }

    [Test]
    public void Float_RoundTrip_Special_Values()
    {
        var state = NewState();
        var values = Floats(0.0, -0.0, double.Epsilon, 1.5, -1.5, 1e10);
        var arr = state.NewArray(values);
        Assert.That(arr.backend, Is.TypeOf<RArrayFloatBackend>());
        for (var i = 0; i < values.Length; i++)
        {
            Assert.That(arr[i].FloatValue, Is.EqualTo(values[i].FloatValue));
        }
    }

    [Test]
    public void Negative_Fixnum_RoundTrip()
    {
        var state = NewState();
        var arr = state.NewArray(Fixnums(-1, -100, long.MinValue >> 1));
        Assert.That(arr.backend, Is.TypeOf<RArrayFixnumBackend>());
        Assert.That(arr[0].FixnumValue, Is.EqualTo(-1));
        Assert.That(arr[1].FixnumValue, Is.EqualTo(-100));
        Assert.That(arr[2].FixnumValue, Is.EqualTo(long.MinValue >> 1));
    }

    [Test]
    public void Push_Onto_Empty_Generic_Does_Not_Promote()
    {
        var state = NewState();
        var arr = state.NewArray(0);
        arr.Push(new MRubyValue(1L));
        Assert.That(arr.backend, Is.TypeOf<RArrayObjectBackend>());
    }
}
