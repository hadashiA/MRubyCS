namespace MRubyCS;

public class RData : RObject
{
    public RData(object data) : base(MRubyVType.CSharpData, null!)
    {
        Data = data;
    }

    public RData(RClass c, object data) : base(MRubyVType.CSharpData, c)
    {
        Data = data;
    }

    public RData(RClass c) : base(MRubyVType.CSharpData, c)
    {
    }

    public object? Data { get; set; }
}
