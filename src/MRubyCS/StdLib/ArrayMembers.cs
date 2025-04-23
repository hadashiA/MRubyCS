using System;
using System.Collections.Generic;

namespace MRubyCS.StdLib;

static class ArrayMembers
{
    public static MRubyMethod Create = new((state, self) =>
    {
        var args = state.GetRestArg(0);
        var array = state.NewArray(args);
        array.Class = self.As<RClass>();
        return MRubyValue.From(array);
    });

    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod OpAref = new((state, self) =>
    {
        var array = self.As<RArray>();
        var argc = state.GetArgumentCount();
        // if (argc )

        var index = state.GetArg(0);
        switch (argc)
        {
            case 1:
                switch (index.VType)
                {
                    case MRubyVType.Range:
                        if (index.As<RRange>()
                                .Calculate(
                                    array.Length,
                                    true,
                                    out var calculatedIndex,
                                    out var calculatedLength) != RangeCalculateResult.TypeMismatch)
                        {
                            return MRubyValue.From(array.SubSequence(calculatedIndex, calculatedLength));
                        }
                        return MRubyValue.Nil;
                    case MRubyVType.Float:
                        return array[(int)index.FloatValue];
                    default:
                        return array[(int)state.ToInteger(index)];
                }
            case 2:
                var i = (int)state.ToInteger(index);
                var length = state.GetArgAsInteger(1);
                if (i < 0) i += array.Length;
                if (i < 0 || array.Length < i) return MRubyValue.Nil;
                if (length < 0) return MRubyValue.Nil;
                if (array.Length == i) return MRubyValue.From(state.NewArray(0));
                if (length > array.Length - i) length = array.Length - i;
                return MRubyValue.From(array.SubSequence(i, (int)length));
            default:
                state.RaiseArgumentNumberError(argc, 1, 2);
                return default;
        }
    });

    public static MRubyMethod OpAset = new((state, self) =>
    {
        var array = self.As<RArray>();
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case 2:
                var key = state.GetArg(0);
                var val = state.GetArg(1);
                if (key.Object is RRange range)
                {
                    switch (range.Calculate(array.Length, false, out var calculatedIndex, out var calculatedLength))
                    {
                        case RangeCalculateResult.TypeMismatch:
                            array[(int)state.ToInteger(key)] = val;
                            break;
                        case RangeCalculateResult.Ok:
                            state.SpliceArray(array, calculatedIndex, calculatedLength, val);
                            break;
                        case RangeCalculateResult.Out:
                            state.Raise(Names.RangeError, state.NewString($"`{state.Stringify(key)}` out of range"));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
                else
                {
                    array[(int)state.ToInteger(key)] = val;
                }
                return val;
            case 3:
                // a[n,m] = v
                var n = state.GetArgAsInteger(0);
                var m = state.GetArgAsInteger(1);
                var v = state.GetArg(2);
                state.SpliceArray(array, (int)n, (int)m, v);
                return v;
            default:
                state.RaiseArgumentNumberError(argc, 2, 3);
                return default;
        }
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Initialize = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg0 = state.GetArg(0);
        var arg1 = state.GetArg(1);
        var block = state.GetBlockArg();

        if (arg0.Object is RArray src && arg1.IsNil && block.IsNil)
        {
            src.CopyTo(array);
            return self;
        }

        var size = state.ToInteger(arg0);
        array.EnsureModifiable((int)size, true);
        var span = array.AsSpan();
        for (var i = 0; i < size; i++)
        {
            if (block.Object is RProc proc)
            {
                var procSelf = state.GetProcSelf(proc, out var targetClass);
                span[i] = state.YieldWithClass(targetClass, procSelf, [MRubyValue.From(i)], proc);
            }
            else
            {
                span[i] = arg1;
            }
        }
        return self;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Push = new((state, self) =>
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        var args = state.GetRestArg(0);

        var start = array.Length;
        array.EnsureModifiable(start + args.Length, true);

        var span = array.AsSpan(start, args.Length);
        foreach (var t in args)
        {
            span[0] = t;
        }
        return self;
    });

    [MRubyMethod]
    public static MRubyMethod Pop = new((state, self) =>
    {
        var array = self.As<RArray>();
        state.EnsureNotFrozen(array);

        array.TryPop(out var result);
        return result;
    });

    [MRubyMethod]
    public static MRubyMethod Plus = new((state, self) =>
    {
        var a1 = self.As<RArray>();
        var a2 = state.GetArgAsArray(0);

        var result = state.NewArray(a1.Length + a2.Length);
        result.EnsureModifiable(a1.Length + a2.Length, true);

        a1.AsSpan().CopyTo(result.AsSpan());
        a2.AsSpan().CopyTo(result.AsSpan(a1.Length));
        return MRubyValue.From(result);
    });

    [MRubyMethod]
    public static MRubyMethod Size = new((state, self) =>
    {
        var array = self.As<RArray>();
        return MRubyValue.From(array.Length);
    });

    [MRubyMethod]
    public static MRubyMethod Empty = new((state, self) =>
    {
        var array = self.As<RArray>();
        return MRubyValue.From(array.Length <= 0);
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod First = new((state, self) =>
    {
        var array = self.As<RArray>();
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case <= 0:
                return array.Length <= 0 ? MRubyValue.Nil : array[0];
            case > 1:
                state.RaiseArgumentNumberError(argc, 0, 1);
                break;
        }

        var size = state.GetArgAsInteger(0);
        if (size < 0)
        {
            state.Raise(Names.ArgumentError, "nagative array size"u8);
        }

        var subSequence = array.SubSequence(0, (int)size);
        return MRubyValue.From(subSequence);
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Last = new((state, self) =>
    {
        var array = self.As<RArray>();
        var argc = state.GetArgumentCount();
        switch (argc)
        {
            case <= 0:
                return array.Length <= 0 ? MRubyValue.Nil : array[^1];
            case > 1:
                state.RaiseArgumentNumberError(argc, 0, 1);
                break;
        }

        var size = state.GetArgAsInteger(0);
        if (size < 0)
        {
            state.Raise(Names.ArgumentError, "nagative array size"u8);
        }
        var subSequence = array.SubSequence(array.Length - (int)size, (int)size);
        return MRubyValue.From(subSequence);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);
        if (arg.Object is not RArray other ||
            array.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (array == other)
        {
            return MRubyValue.True;
        }

        var span1 = array.AsSpan();
        var span2 = other.AsSpan();
        for (var i = 0; i < span1.Length; i++)
        {
            var elementEquals = state.Send(span1[i], Names.OpEq, span2[i]);
            if (elementEquals.Falsy)
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eql = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);
        if (arg.Object is not RArray other ||
            array.Length != other.Length)
        {
            return MRubyValue.False;
        }

        if (array == other)
        {
            return MRubyValue.True;
        }

        var span1 = array.AsSpan();
        var span2 = other.AsSpan();
        for (var i = 0; i < span1.Length; i++)
        {
            var elementEquals = state.Send(span1[i], Names.QEql, span2[i]);
            if (elementEquals.Falsy)
            {
                return MRubyValue.False;
            }
        }
        return MRubyValue.True;
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpAdd = new((state, self) =>
    {
        var array = self.As<RArray>();
        var other = state.GetArg(0);
        state.EnsureValueType(other, MRubyVType.Array);

        var otherArray = other.As<RArray>();

        var newLength = array.Length + otherArray.Length;
        var newArray = state.NewArray(newLength);
        newArray.EnsureModifiable(newLength, true);

        var span = newArray.AsSpan();
        array.AsSpan().CopyTo(span);
        otherArray.AsSpan().CopyTo(span[array.Length..]);
        return MRubyValue.From(newArray);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Times = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);

        if (arg.Object is RString separator)
        {
            return MRubyValue.From(JoinArray(state, array, separator, new Stack<RArray>()));
        }

        var times = state.ToInteger(arg);
        if (times == 0)
        {
            return MRubyValue.From(state.NewArray());
        }
        if (times < 0)
        {
            state.Raise(Names.ArgumentError, "nagative argument"u8);
        }
        else if (Array.MaxLength / times < array.Length)
        {
            state.Raise(Names.ArgumentError, "array size too big"u8);
        }

        var source = array.AsSpan();
        var newLength = array.Length * (int)times;
        var result = state.NewArray(newLength);
        result.EnsureModifiable(newLength, true);
        for (var i = 0; i < times; i++)
        {
            source.CopyTo(result.AsSpan(array.Length * i));
        }
        return MRubyValue.From(result);
    });

    public static MRubyMethod ReverseBang = new((state, self) =>
    {
        var array = self.As<RArray>();
        var span = array.AsSpan();

        var left = 0;
        var right = span.Length - 1;
        while (left < right)
        {
            (span[left], span[right]) = (span[right], span[left]);
            left++;
            right--;
        }
        return self;
    });

    public static MRubyMethod DeleteAt = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);
        var index = state.ToInteger(arg);
        return array.DeleteAt((int)index);
    });

    public static MRubyMethod ToS = new((state, self) =>
    {
        var array = self.As<RArray>();
        var result = state.NewString("["u8);
        if (state.IsRecursiveCalling(array, Names.Inspect))
        {
            result.Concat("...]"u8);
        }
        else
        {
            var first = true;
            foreach (var x in array.AsSpan())
            {
                if (!first)
                {
                    result.Concat(", "u8);
                }
                first = false;

                var value = state.Stringify(state.Send(x, Names.Inspect));
                result.Concat(value);
            }
            result.Concat("]"u8);
        }
        return MRubyValue.From(result);
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Index = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);
        var span = array.AsSpan();
        for (var i = 0; i < span.Length; i++)
        {
            if (state.ValueEquals(span[i], arg))
            {
                return MRubyValue.From(i);
            }
        }
        return MRubyValue.Nil;
    });

    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Join = new((state, self) =>
    {
        RString? separator = null;
        if (state.TryGetArg(0, out var arg0))
        {
            state.EnsureValueType(arg0, MRubyVType.String);
            separator = arg0.As<RString>();
        }

        var array = self.As<RArray>();
        var result = JoinArray(state, array, separator, new Stack<RArray>());
        return MRubyValue.From(result);
    });

    public static MRubyMethod Clear = new((state, self) =>
    {
        self.As<RArray>().Clear();
        return self;
    });

    [MRubyMethod(RestArguments = true)]
    public static MRubyMethod Concat = new((state, self) =>
    {
        var array = self.As<RArray>();
        var args = state.GetRestArg(0);
        foreach (var arg in args)
        {
            state.EnsureValueType(arg, MRubyVType.Array);
        }
        foreach (var arg in args)
        {
            array.Concat(arg.As<RArray>());
        }
        return self;
    });

    static int AsIndex(MRubyState state, MRubyValue index)
    {
        if (index.IsInteger)
        {
            return (int)index.IntegerValue;
        }
        return (int)state.GetArgAsInteger(0);
    }

    static RString JoinArray(MRubyState state, RArray array, RString separator, Stack<RArray> stack)
    {
        var span = array.AsSpan();

        // check recursive
        foreach (var x in stack)
        {
            if (x == array)
            {
                state.Raise(Names.ArgumentError, "recursive array join"u8);
            }
        }

        stack.Push(array);

        var result = state.NewString(array.Length * 2);
        var first = true;
        foreach (var x in span)
        {
            if (!first && separator != null)
            {
                result.Concat(separator);
            }
            first = false;

            if (x.Object is RString str)
            {
                result.Concat(str);
            }
            else if (x.Object is RArray nested)
            {
                var joinedValue = JoinArray(state, nested, separator, stack);
                result.Concat(joinedValue);
            }
            else
            {
                result.Concat(state.Stringify(x));
            }
        }

        stack.Pop();
        return result;
    }

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod InternalEq = new((state, self) =>
    {
        var array = self.As<RArray>();
        var arg = state.GetArg(0);
        if (self == arg)
        {
            return MRubyValue.True;
        }
        if (arg.VType != MRubyVType.Array)
        {
            return MRubyValue.False;
        }

        if (arg.Object is RArray other && other.Length != array.Length)
        {
            return MRubyValue.False;
        }
        return arg;
    });


    // internal method to convert multi-value to single value
    public static MRubyMethod InternalSValue = new((state, self) =>
    {
        var array = self.As<RArray>();
        return array.Length switch
        {
            0 => MRubyValue.Nil,
            1 => array[0],
            _ => self
        };
    });
}
