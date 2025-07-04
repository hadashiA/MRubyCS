using System;
using System.Buffers;
using MRubyCS.Internals;
using Utf8StringInterpolation;

namespace MRubyCS;

public class MRubyLongJumpException(string message) : Exception(message);

public class MRubyBreakException(MRubyState state, RBreak breakObject)
    : MRubyLongJumpException("break")
{
    public MRubyState State => state;
    public RBreak BreakObject => breakObject;
}

public class MRubyRaiseException(
    string message,
    MRubyState state,
    RException exceptionObject,
    int callDepth)
    : MRubyLongJumpException(message)
{
    public MRubyState State { get; } = state;
    public RException ExceptionObject { get; } = exceptionObject;
    public int CallDepth { get; } = callDepth;

    public MRubyRaiseException(
        MRubyState state,
        RException exceptionObject,
        int callDepth)
        : this(exceptionObject.Message?.ToString() ?? "exception raised", state, exceptionObject, callDepth)
    {
    }
}

partial class MRubyState
{
    public void Raise(RException ex)
    {
        var typeName = NameOf(ex.Class);

        var message = ex.Message?.Length >  0
            ? $"{ex.Message} ({typeName})"
            : typeName.ToString();

        Exception = new MRubyRaiseException(message, this, ex, Context.CallDepth);
        throw Exception;
    }

    public void Raise(RClass exceptionClass, RString message)
    {
        var backtrace = Backtrace.Capture(Context);
        var ex = new RException(message, exceptionClass)
        {
            Backtrace = backtrace
        };
        Raise(ex);
    }

    public void Raise(RClass exceptionClass, ReadOnlySpan<byte> message)
    {
        Raise(exceptionClass, NewString(message));
    }

    public void Raise(RClass exceptionClass, ref Utf8StringWriter<ArrayBufferWriter<byte>> format)
    {
        format.Flush();
        Raise(exceptionClass, NewString(format.GetBufferWriter().WrittenSpan));
    }

    public void Raise(Symbol errorType, RString message)
    {
        Raise(GetExceptionClass(errorType), message);
    }

    public void Raise(Symbol errorType, ReadOnlySpan<byte> message)
    {
        Raise(GetExceptionClass(errorType), NewString(message));
    }

    public void Raise(Symbol errorType, ref Utf8StringWriter<ArrayBufferWriter<byte>> format)
    {
        format.Flush();
        Raise(GetExceptionClass(errorType), NewString(format.GetBufferWriter().WrittenSpan));
    }

    public void RaiseArgumentNumberError(int argc, int expected)
    {
        Raise(Names.ArgumentError, $"wrong number of arguments (given {argc}, expected {expected})");
    }

    public void RaiseArgumentNumberError(int argc, int min, int max)
    {
        RString message;
        if (min == max)
        {
            message = NewString($"wrong number of arguments (given {argc}, expected {min})");
        }
        else if (max < 0)
        {
            message = NewString($"wrong number of arguments (given {argc}, expected {min}+)");
        }
        else
        {
            message = NewString($"wrong number of arguments (given {argc}, expected {min}..{max})");
        }

        Raise(Names.ArgumentError, message);
    }

    public RClass GetExceptionClass(Symbol name)
    {
        if (!TryGetConst(name, out var value) || value.VType != MRubyVType.Class)
        {
            Raise(ExceptionClass, "exception corrupted"u8);
        }

        var exceptionClass = value.As<RClass>();
        if (!exceptionClass.Is(ExceptionClass))
        {
            Raise(ExceptionClass, "non-exception raised"u8);
        }
        return exceptionClass;
    }

    internal void RaiseConstMissing(RClass mod, Symbol name)
    {
        if (mod.GetRealClass() != ObjectClass)
        {
            RaiseNameError(name, NewString($"uninitialized constant {NameOf(mod)}::{NameOf(name)}"));
        }
        else
        {
            RaiseNameError(name, NewString($"uninitialized constant {NameOf(name)}"));
        }
    }

    internal void EnsureArgumentCount(int expected)
    {
        var argc = GetArgumentCount();
        if (expected != argc)
        {
            RaiseArgumentNumberError(argc, expected);
        }
    }

    public void EnsureArgumentCount(int min, int max)
    {
        var argc = GetArgumentCount();
        if (argc < min || argc > max)
        {
            RaiseArgumentNumberError(argc, min, max);
        }
    }

    public void EnsureBlockGiven(MRubyValue block)
    {
        if (block.IsNil)
        {
            Raise(Names.ArgumentError, "no block given"u8);
        }
        if (!block.IsProc)
        {
            Raise(Names.TypeError, "not a block"u8);
        }
    }

    public void EnsureNotFrozen(MRubyValue value)
    {
        if (value.IsImmediate || value.Object.IsFrozen)
        {
            RaiseFrozenError(value);
        }
    }

    public void EnsureNotFrozen(RObject o)
    {
        if (o.IsFrozen)
        {
            RaiseFrozenError(MRubyValue.From(o));
        }
    }

    internal void RaiseMethodMissing(Symbol methodId, MRubyValue self, MRubyValue args)
    {
        var exceptionClass = GetExceptionClass(Names.NoMethodError);
        var ex = new RException(NewString($"undefined method {NameOf(methodId)} for {ClassNameOf(self)}"), exceptionClass);
        ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(methodId));
        ex.InstanceVariables.Set(Names.ArgsVariable, args);
        Raise(ex);
    }

    internal void RaiseNameError(Symbol name, RString message)
    {
        var ex = new RException(message, GetExceptionClass(Names.NameError));
        ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(name));
        Raise(ex);
    }

    internal void EnsureConstName(Symbol constName)
    {
        if (!NamingRule.IsConstName(NameOf(constName)))
        {
            var ex = new RException(
                NewString($"wrong constant name {NameOf(constName)}"),
                GetExceptionClass(Names.NameError));

            ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(constName));
            Raise(ex);
        }
    }

    internal void EnsureInstanceVariableName(Symbol instanceVariableName)
    {
        if (!NamingRule.IsInstanceVariableName(NameOf(instanceVariableName)))
        {
            var ex = new RException(
                NewString($"'{NameOf(instanceVariableName)}' is not allowed as an instance variable name."),
                GetExceptionClass(Names.NameError));

            ex.InstanceVariables.Set(Names.NameVariable, MRubyValue.From(instanceVariableName));
            Raise(ex);
        }
    }

    internal void EnsureFloatValue(double value)
    {
        if (double.IsNaN(value))
        {
            Raise(Names.FloatDomainError, "NaN"u8);
        }
        if (double.IsPositiveInfinity(value))
        {
            Raise(Names.FloatDomainError, "Infinity"u8);
        }
        if (double.IsNegativeInfinity(value))
        {
            Raise(Names.FloatDomainError, "-Infinity"u8);
        }
    }

    internal void RaiseFrozenError(MRubyValue v)
    {
        Raise(Names.FrozenError, Utf8String.Format($"can't modify frozen {Stringify(v)}"));
    }

    public void EnsureValueIsConst(MRubyValue value)
    {
        if (value.VType is not (MRubyVType.Class or MRubyVType.Module or MRubyVType.SClass))
        {
            Raise(Names.TypeError, "constant is non class/module"u8);
        }
    }

    public void EnsureValueIsBlock(MRubyValue value)
    {
        if (!value.IsProc)
        {
            Raise(Names.TypeError, "not a block"u8);
        }
    }

    public void EnsureClassOrModule(MRubyValue value)
    {
        if (!value.IsClass)
        {
            Raise(Names.TypeError, $"{Stringify(value)} is not a class/module");
        }
    }

    public void EnsureInheritable(RClass c)
    {
        if (c.VType != MRubyVType.Class)
        {
            Raise(Names.TypeError, $"superclass must be a Class ({NameOf(c)} given)");
        }
        if (c.VType != MRubyVType.SClass)
        {
            Raise(Names.TypeError, "can't make subclass of singleton class"u8);
        }
        if (c == ClassClass)
        {
            Raise(Names.TypeError, "can't make subclass of Class"u8);
        }
    }

    public void EnsureValueType(MRubyValue value, MRubyVType expectedType)
    {
        if (value.VType == expectedType) return;

        RString actualValueName;
        if (value.IsNil)
        {
            actualValueName = NewString("nil"u8);
        }
        else if (value.IsInteger)
        {
            actualValueName = NewString("Integer"u8);
        }
        else if (value.IsSymbol)
        {
            actualValueName = NewString("Symbol"u8);
        }
        else if (value.IsImmediate)
        {
            actualValueName = Stringify(value);
        }
        else
        {
            actualValueName = NameOf(ClassOf(value));
        }
        Raise(Names.TypeError, $"wrong argument type {actualValueName} (expected {expectedType})");
    }


}
