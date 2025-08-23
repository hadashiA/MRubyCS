namespace MRubyCS.Serializer.Tests;

[MRubyObject]
partial class NestedFieldObject
{
    public int X { get; set; }
    public string[] Array { get; set; }
    public Dictionary<string, Struct1> Dict { get; set; }

    [MRubyMember("alias_of_y")]
    public int Y { get; set; }
}

[MRubyObject]
[method: MRubyConstructor]
partial class MRubyConstructorClass(int x, int y, string hoge)
{
    public int X { get; } = x;
    public int Y { get; } = y;
    public string Hoge { get; } = hoge;
}

[MRubyObject]
partial struct Struct1
{
    public long Id { get; set; }
}
