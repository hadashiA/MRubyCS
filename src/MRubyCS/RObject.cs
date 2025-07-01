namespace MRubyCS;

public class RObject : RBasic
{
    internal VariableTable InstanceVariables { get; set; } = new();

    internal RObject(MRubyVType vType, RClass klass) : base(vType, klass)
    {
    }

    public static implicit operator MRubyValue(RObject obj) => MRubyValue.From(obj);

    /// <summary>
    /// Create a copy of the object (equivalent to `init_copy`)
    /// </summary>
    /// <remarks>
    ///
    /// Because of the ruby specification, overrideable processes are implemented with `initialize_copy`.
    /// </remarks>
    internal virtual RObject Clone()
    {
        var clone = new RObject(VType, Class);
        InstanceVariables.CopyTo(clone.InstanceVariables);
        return clone;
    }
}

