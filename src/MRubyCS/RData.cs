namespace MRubyCS;

public class RData(object data) : RObject(MRubyVType.CSharpData, null!)
{
    public object Data => data;
}
