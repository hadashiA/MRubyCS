namespace MRubyCS;

public class RData : RObject
{
    public object? Data { get; set; }

    internal RData() : base(MRubyVType.CSharpData, null!)
    {
    }
}
