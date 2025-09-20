using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using MRubyCS.Internals;
using MRubyCS.StdLib;
using Utf8StringInterpolation;

namespace MRubyCS;

public partial class MRubyState
{
    public static MRubyState Create(Action<MRubyState> configure)
    {
        var state = Create();
        configure(state);
        return state;
    }

    public static MRubyState Create()
    {
        var state = new MRubyState();
        state.InitClass();
        state.InitObject();
        state.InitKernel();
        state.InitSymbol();
        state.InitString();
        state.InitProc();
        state.InitException();
        state.InitNumeric();
        state.InitArray();
        state.InitHash();
        state.InitRange();
        state.InitEnumerable();
        state.InitFiber();
        state.InitMrbLib();
        state.InitObjectExt();
        return state;
    }

    public RClass BasicObjectClass { get; private set; } = default!;
    public RClass ObjectClass { get; private set; } = default!;
    public RClass ClassClass { get; private set; } = default!;
    public RClass ModuleClass { get; private set; } = default!;
    public RClass ProcClass { get; private set; } = default!;
    public RClass StringClass { get; private set; } = default!;
    public RClass ArrayClass { get; private set; } = default!;
    public RClass HashClass { get; private set; } = default!;
    public RClass RangeClass { get; private set; } = default!;
    public RClass FloatClass { get; private set; } = default!;
    public RClass IntegerClass { get; private set; } = default!;
    public RClass TrueClass { get; private set; } = default!;
    public RClass FalseClass { get; private set; } = default!;
    public RClass NilClass { get; private set; } = default!;
    public RClass SymbolClass { get; private set; } = default!;
    public RClass FiberClass { get; private set; } = default!;
    public RClass KernelModule { get; private set; } = default!;
    public RClass ExceptionClass { get; private set; } = default!;
    public RClass StandardErrorClass { get; private set; } = default!;

    public RObject TopSelf { get; private set; } = default!;
    public MRubyLongJumpException? Exception { get; private set; }

    public MRubyValueEqualityComparer ValueEqualityComparer { get; }
    public MRubyValueHashKeyEqualityComparer HashKeyEqualityComparer { get; }

    internal MRubyContext Context { get; private set; }
    internal MRubyContext ContextRoot { get; }= new();

    public RiteParser RiteParser => riteParser ??= new RiteParser(this);

    readonly SymbolTable symbolTable = new();
    readonly VariableTable globalVariables = new();

    RiteParser? riteParser;

    // TODO:
    // readonly (RClass, MRubyMethod)[] methodCacheEntries = new (RClass, MRubyMethod)[MethodCacheSize];

    MRubyState()
    {
        Context = ContextRoot;
        ValueEqualityComparer = new MRubyValueEqualityComparer(this);
        HashKeyEqualityComparer = new MRubyValueHashKeyEqualityComparer(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(ReadOnlySpan<byte> name) => symbolTable.Intern(name);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(RString name) => symbolTable.Intern(name.AsSpan());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Symbol Intern(ref Utf8StringWriter<ArrayBufferWriter<byte>> format)
    {
        format.Flush();
        return symbolTable.Intern(format.GetBufferWriter().WrittenSpan);
    }

    public Symbol Intern(string name)
    {
        return symbolTable.Intern(name);
    }

    void InitClass()
    {
        BasicObjectClass = new RClass(default!)
        {
            InstanceVType = MRubyVType.Undef,
            Super = default!, // sentinel. only for BasicObject
        };
        ObjectClass = new RClass(default!)
        {
            InstanceVType = MRubyVType.Object,
            Super = BasicObjectClass,
        };
        ModuleClass = new RClass(default!)
        {
            InstanceVType = MRubyVType.Module,
            Super = ObjectClass,
        };
        ClassClass = new RClass(default!)   // sentinel. only for ClassClass
        {
            InstanceVType = MRubyVType.Class,
            Super = ModuleClass,
        };

        BasicObjectClass.Class = ClassClass;
        ObjectClass.Class = ClassClass;
        ModuleClass.Class = ClassClass;
        ClassClass.Class = ClassClass;

        // Prepare singleton class
        PrepareSingletonClass(BasicObjectClass);
        PrepareSingletonClass(ObjectClass);
        PrepareSingletonClass(ModuleClass);
        PrepareSingletonClass(ClassClass);

        // name basic classes
        DefineConst(Names.BasicObjectClass, BasicObjectClass);
        DefineConst(Names.ObjectClass, ObjectClass);
        DefineConst(Names.ModuleClass, ModuleClass);
        DefineConst(Names.ClassClass, ClassClass);

        BasicObjectClass.InstanceVariables.Set(Names.ClassNameKey, NewString("BasicObject"u8));
        ObjectClass.InstanceVariables.Set(Names.ClassNameKey, NewString("Object"u8));
        ModuleClass.InstanceVariables.Set(Names.ClassNameKey, NewString("Module"u8));
        ClassClass.InstanceVariables.Set(Names.ClassNameKey, NewString("Class"u8));

        DefineMethod(BasicObjectClass, Names.Initialize, MRubyMethod.Nop);
        DefineMethod(BasicObjectClass, Names.OpNot, BasicObjectMembers.Not);
        DefineMethod(BasicObjectClass, Names.OpEq, BasicObjectMembers.OpEq);
        DefineMethod(BasicObjectClass, Intern("__id__"u8), BasicObjectMembers.Id);
        DefineMethod(BasicObjectClass, Intern("__send__"u8), BasicObjectMembers.Send);
        DefineMethod(BasicObjectClass, Names.QEqual, BasicObjectMembers.OpEq);
        DefineMethod(BasicObjectClass, Names.InstanceEval, BasicObjectMembers.InstanceEval);
        DefineMethod(BasicObjectClass, Names.SingletonMethodAdded, MRubyMethod.Nop);
        DefineMethod(BasicObjectClass, Names.MethodMissing, BasicObjectMembers.MethodMissing);

        DefineSingletonMethod(ClassClass, Names.New, ClassMembers.NewClass);
        DefineMethod(ClassClass, Names.New, ClassMembers.New);
        DefineMethod(ClassClass, Names.Initialize, ClassMembers.Initialize);
        DefineMethod(ClassClass, Intern("superclass"u8), ClassMembers.Superclass);
        DefineMethod(ClassClass, Intern("inherited"u8), MRubyMethod.Nop);

        DefineMethod(ModuleClass, Intern("extend_object"u8), ModuleMembers.ExtendObject);
        DefineMethod(ModuleClass, Intern("extended"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("prepended"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("prepend_features"u8), ModuleMembers.PrependFeatures);
        DefineMethod(ModuleClass, Intern("include?"u8), ModuleMembers.QInclude);
        DefineMethod(ModuleClass, Intern("append_features"u8), ModuleMembers.AppendFeatures);
        DefineMethod(ModuleClass, Intern("class_eval"u8), ModuleMembers.ClassEval);
        DefineMethod(ModuleClass, Intern("module_eval"u8), ModuleMembers.ClassEval);
        DefineMethod(ModuleClass, Intern("included"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Names.Initialize, ModuleMembers.Initialize);
        DefineMethod(ModuleClass, Intern("module_function"u8), ModuleMembers.ModuleFunction);
        DefineMethod(ModuleClass, Intern("private"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("protected"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("public"u8), MRubyMethod.Nop);
        DefineMethod(ModuleClass, Intern("attr_reader"u8), ModuleMembers.AttrReader);
        DefineMethod(ModuleClass, Intern("attr_writer"u8), ModuleMembers.AttrWriter);
        DefineMethod(ModuleClass, Intern("attr_accessor"u8), ModuleMembers.AttrAccessor);
        DefineMethod(ModuleClass, Names.ToS, ModuleMembers.ToS);
        DefineMethod(ModuleClass, Names.Inspect, ModuleMembers.ToS);
        DefineMethod(ModuleClass, Intern("alias_method"u8), ModuleMembers.AliasMethod);
        DefineMethod(ModuleClass, Intern("ancestors"u8), ModuleMembers.Ancestors);
        DefineMethod(ModuleClass, Intern("undef_method"u8), ModuleMembers.UndefMethod);
        DefineMethod(ModuleClass, Intern("const_defined?"u8), ModuleMembers.ConstDefined);
        DefineMethod(ModuleClass, Intern("const_get"u8), ModuleMembers.ConstGet);
        DefineMethod(ModuleClass, Intern("const_set"u8), ModuleMembers.ConstSet);
        DefineMethod(ModuleClass, Intern("remove_const"u8), ModuleMembers.RemoveConst);
        DefineMethod(ModuleClass, Intern("const_missing"u8), ModuleMembers.ConstMissing);
        DefineMethod(ModuleClass, Intern("method_defined?"u8), ModuleMembers.MethodDefined);
        DefineMethod(ModuleClass, Intern("define_method"u8), ModuleMembers.DefineMethod);
        DefineMethod(ModuleClass, Names.OpEqq, ModuleMembers.Eqq);
        DefineMethod(ModuleClass, Names.Dup, ModuleMembers.Dup);
        DefineMethod(ModuleClass, Names.MethodAdded, MRubyMethod.Nop);

        UndefMethod(ClassClass, Intern("append_features"u8));
        UndefMethod(ClassClass, Intern("prepend_features"u8));
        UndefMethod(ClassClass, Intern("extend_object"u8));
        UndefMethod(ClassClass, Intern("module_function"u8));

        TopSelf = new RObject(MRubyVType.Object, ObjectClass);
        // DefineSingletonMethod(TopSelf, Names.Inspect, MainMembers.Inspect);
        // DefineSingletonMethod(TopSelf, Names.ToS, MainMembers.ToS);
        // DefineSingletonMethod(TopSelf, Intern("define_method"u8), MainMembers.DefineMethod);
    }

    void InitObject()
    {
        NilClass = DefineClass(Intern("NilClass"u8), ObjectClass, MRubyVType.False);
        UndefClassMethod(NilClass, Names.New);
        DefineMethod(NilClass, Names.OpAnd, FalseClassMembers.And);
        DefineMethod(NilClass, Names.OpOr, FalseClassMembers.Or);
        DefineMethod(NilClass, Names.OpXor, FalseClassMembers.Xor);
        DefineMethod(NilClass, Names.QNil, MRubyMethod.True);
        DefineMethod(NilClass, Names.ToS, NilClassMembers.Tos);
        DefineMethod(NilClass, Names.Inspect, NilClassMembers.Inspect);

        TrueClass = DefineClass(Intern("TrueClass"u8), ObjectClass, MRubyVType.True);
        UndefClassMethod(TrueClass, Names.New);
        DefineMethod(TrueClass, Names.OpAnd, TrueClassMembers.And);
        DefineMethod(TrueClass, Names.OpOr, TrueClassMembers.Or);
        DefineMethod(TrueClass, Names.OpXor, TrueClassMembers.Xor);
        DefineMethod(TrueClass, Names.ToS, TrueClassMembers.ToS);
        DefineMethod(TrueClass, Names.Inspect, TrueClassMembers.ToS);

        FalseClass = DefineClass(Intern("FalseClass"u8), ObjectClass, MRubyVType.False);
        UndefClassMethod(FalseClass, Names.New);
        DefineMethod(FalseClass, Names.OpAnd, FalseClassMembers.And);
        DefineMethod(FalseClass, Names.OpOr, FalseClassMembers.Or);
        DefineMethod(FalseClass, Names.OpXor, FalseClassMembers.Xor);
        DefineMethod(FalseClass, Names.ToS, FalseClassMembers.ToS);
        DefineMethod(FalseClass, Names.Inspect, FalseClassMembers.ToS);
    }

    void InitKernel()
    {
        KernelModule = DefineModule(Intern("Kernel"u8), ObjectClass);
        DefineClassMethod(KernelModule, Names.Raise, KernelMembers.Raise);

        DefineMethod(KernelModule, Names.OpEqq, KernelMembers.OpEqq);
        DefineMethod(KernelModule, Names.OpCmp, KernelMembers.Cmp);
        DefineMethod(KernelModule, Names.QBlockGiven, KernelMembers.BlockGiven);
        DefineMethod(KernelModule, Names.Clone, KernelMembers.Clone);
        DefineMethod(KernelModule, Names.Dup, KernelMembers.Dup);
        DefineMethod(KernelModule, Names.Inspect, KernelMembers.Inspect);
        DefineMethod(KernelModule, Names.InitializeCopy, KernelMembers.InitializeCopy);
        DefineMethod(KernelModule, Names.Raise, KernelMembers.Raise);
        DefineMethod(KernelModule, Names.Class, KernelMembers.Class);
        DefineMethod(KernelModule, Names.QEql, KernelMembers.Eql);
        DefineMethod(KernelModule, Names.QNil, MRubyMethod.False);
        DefineMethod(KernelModule, Intern("freeze"u8), KernelMembers.Freeze);
        DefineMethod(KernelModule, Intern("frozen?"u8), KernelMembers.Frozen);
        DefineMethod(KernelModule, Names.Hash, KernelMembers.Hash);
        DefineMethod(KernelModule, Intern("instance_of?"u8), KernelMembers.InstanceOf);
        DefineMethod(KernelModule, Names.QIsA, KernelMembers.KindOf);
        DefineMethod(KernelModule, Names.QKindOf, KernelMembers.KindOf);
        DefineMethod(KernelModule, Intern("iterator?"u8), KernelMembers.BlockGiven);
        DefineMethod(KernelModule, Intern("kind_of?"u8), KernelMembers.KindOf);
        DefineMethod(KernelModule, Names.Nil, MRubyMethod.Nop);
        DefineMethod(KernelModule, Intern("object_id"u8), KernelMembers.ObjectId);
        DefineMethod(KernelModule, Intern("p"u8), KernelMembers.P);
        DefineMethod(KernelModule, Intern("print"u8), KernelMembers.Print);
        DefineMethod(KernelModule, Intern("remove_instance_variable"u8), KernelMembers.RemoveInstanceVariable);
        DefineMethod(KernelModule, Names.QRespondTo, KernelMembers.RespondTo);
        DefineMethod(KernelModule, Names.QRespondToMissing, MRubyMethod.False);
        DefineMethod(KernelModule, Names.ToS, KernelMembers.ToS);
        DefineMethod(KernelModule, Intern("lambda"u8), KernelMembers.Lambda);
        // internally used
        DefineMethod(KernelModule, Intern("__case_eqq"u8), KernelMembers.InternalCaseEqq);
        DefineMethod(KernelModule, Intern("__to_int"u8), KernelMembers.InternalToInt);

        IncludeModule(ObjectClass, KernelModule);
    }

    void InitSymbol()
    {
        SymbolClass = DefineClass(Intern("Symbol"u8), ObjectClass, MRubyVType.Symbol);
        UndefClassMethod(SymbolClass, Names.New);

        DefineMethod(SymbolClass, Names.ToS, SymbolMembers.ToS);
        DefineMethod(SymbolClass, Names.Name, SymbolMembers.Name);
        DefineMethod(SymbolClass, Names.ToSym, MRubyMethod.Identity);
        DefineMethod(SymbolClass, Names.Inspect, SymbolMembers.Inspect);
        DefineMethod(SymbolClass, Names.OpCmp, SymbolMembers.Cmp);
        DefineMethod(SymbolClass, Names.OpEq, KernelMembers.Eql);
    }

    void InitProc()
    {
        ProcClass = DefineClass(Intern("Proc"u8), ObjectClass, MRubyVType.Proc);
        DefineClassMethod(ProcClass, Names.New, ProcMembers.New);
        DefineMethod(ProcClass, Intern("arity"u8), MRubyMethod.Nop);
        DefineMethod(ProcClass, Names.OpEq, ProcMembers.Eql);
        DefineMethod(ProcClass, Names.QEql, ProcMembers.Eql);

        // NOTE: Why implement Proc#call in byte code?
        // The arguments at the time of `call` method call need to be copied to Proc execution,
        // but bytecode does not need such copying
        var callProc = new RProc(
            new Irep
            {
                RegisterVariableCount = 2,
                Sequence = [(byte)OpCode.Call],
            },
            0,
            ProcClass)
        {
            Upper = null,
            Scope = null
        };
        callProc.SetFlag(MRubyObjectFlags.ProcStrict);
        callProc.SetFlag(MRubyObjectFlags.ProcScope);
        callProc.SetFlag(MRubyObjectFlags.Frozen);

        var callMethod = new MRubyMethod(callProc);
        DefineMethod(ProcClass, Names.Call, callMethod);
        DefineMethod(ProcClass, Names.OpAref, callMethod);
    }

    void InitException()
    {
        ExceptionClass = DefineClass(Intern("Exception"u8), ObjectClass, MRubyVType.Exception);
        DefineSingletonMethod(ExceptionClass, Names.Exception, ExceptionMembers.New);
        DefineMethod(ExceptionClass, Names.Exception, ExceptionMembers.Exception);
        DefineMethod(ExceptionClass, Names.Initialize, ExceptionMembers.Initialize);
        DefineMethod(ExceptionClass, Names.ToS, ExceptionMembers.ToS);
        DefineMethod(ExceptionClass, Intern("message"u8), ExceptionMembers.ToS);
        DefineMethod(ExceptionClass, Names.Inspect, ExceptionMembers.Inspect);
        DefineMethod(ExceptionClass, Intern("backtrace"u8), ExceptionMembers.Backtrace);

        StandardErrorClass = DefineClass(Intern("StandardError"u8), ExceptionClass);
        DefineClass(Names.RuntimeError, StandardErrorClass);
        DefineClass(Names.TypeError, StandardErrorClass);
        DefineClass(Names.ZeroDivisionError, StandardErrorClass);
        DefineClass(Names.ArgumentError, StandardErrorClass);
        DefineClass(Names.IndexError, StandardErrorClass);
        DefineClass(Names.RangeError, StandardErrorClass);
        DefineClass(Names.FrozenError, StandardErrorClass);
        DefineClass(Names.NotImplementedError, StandardErrorClass);
        DefineClass(Names.LocalJumpError, StandardErrorClass);
        DefineClass(Names.FloatDomainError, StandardErrorClass);

        DefineClass(Intern("ScriptError"u8), ExceptionClass);
        DefineClass(Intern("SyntaxError"u8), ExceptionClass);
        DefineClass(Intern("StopIteration"u8), ExceptionClass);
    }

    void InitNumeric()
    {
        var numericClass = DefineClass(Intern("Numeric"u8), ObjectClass);
        DefineMethod(numericClass, Intern("finite?"u8), MRubyMethod.True);
        DefineMethod(numericClass, Intern("infinite?"u8), MRubyMethod.False);
        DefineMethod(numericClass, Names.QEql, NumericMembers.Eql);

        IntegerClass = DefineClass(Intern("Integer"u8), numericClass, MRubyVType.Integer);
        UndefClassMethod(IntegerClass, Names.New);
        // DefineMethod(IntegerClass);
        DefineMethod(IntegerClass, Names.OpPow, IntegerMembers.OpPow);
        DefineMethod(IntegerClass, Names.OpAdd, IntegerMembers.OpAdd);
        DefineMethod(IntegerClass, Names.OpSub, IntegerMembers.OpSub);
        DefineMethod(IntegerClass, Names.OpMul, IntegerMembers.OpMul);
        DefineMethod(IntegerClass, Names.OpDiv, IntegerMembers.OpDiv);
        DefineMethod(IntegerClass, Names.ToS, IntegerMembers.ToS);
        DefineMethod(IntegerClass, Names.Inspect, IntegerMembers.ToS);
        DefineMethod(IntegerClass, Names.OpMod, IntegerMembers.Mod);
        DefineMethod(IntegerClass, Names.OpPlus, IntegerMembers.OpPlus);
        DefineMethod(IntegerClass, Names.OpMinus, IntegerMembers.OpMinus);
        DefineMethod(IntegerClass, Intern("div"u8), IntegerMembers.IntDiv);
        DefineMethod(IntegerClass, Intern("fdiv"u8), IntegerMembers.FDiv);
        DefineMethod(IntegerClass, Intern("abs"u8), IntegerMembers.Abs);
        DefineMethod(IntegerClass, Intern("quo"u8), IntegerMembers.Quo);
        DefineMethod(IntegerClass, Intern("ceil"u8), IntegerMembers.Ceil);
        DefineMethod(IntegerClass, Intern("floor"u8), IntegerMembers.Floor);
        DefineMethod(IntegerClass, Intern("round"u8), IntegerMembers.Round);
        DefineMethod(IntegerClass, Intern("next"u8), IntegerMembers.Next);
        DefineMethod(IntegerClass, Intern("succ"u8), IntegerMembers.Next);
        DefineMethod(IntegerClass, Intern("truncate"u8), IntegerMembers.Truncate);
        DefineMethod(IntegerClass, Names.Hash, IntegerMembers.Hash);
        DefineMethod(IntegerClass, Intern("divmod"u8), IntegerMembers.DivMod);
        DefineMethod(IntegerClass, Intern("to_f"u8), IntegerMembers.ToF);
        DefineMethod(IntegerClass, Names.ToI, MRubyMethod.Identity);
        DefineMethod(IntegerClass, Intern("to_int"u8), MRubyMethod.Identity);
        DefineMethod(IntegerClass, Names.OpAnd, IntegerMembers.OpAnd);
        DefineMethod(IntegerClass, Names.OpOr, IntegerMembers.OpOr);
        DefineMethod(IntegerClass, Names.OpXor, IntegerMembers.OpXor);
        DefineMethod(IntegerClass, Names.OpLShift, IntegerMembers.OpLShift);
        DefineMethod(IntegerClass, Names.OpRShift, IntegerMembers.OpRShift);
        DefineMethod(IntegerClass, Names.OpCmp, NumericMembers.OpCmp);

        FloatClass = DefineClass(Intern("Float"u8), numericClass, MRubyVType.Float);
        UndefClassMethod(FloatClass, Names.New);
        DefineMethod(FloatClass, Names.OpPow, FloatMembers.OpPow);
        DefineMethod(FloatClass, Names.OpAdd, FloatMembers.OpAdd);
        DefineMethod(FloatClass, Names.OpSub, FloatMembers.OpSub);
        DefineMethod(FloatClass, Names.OpMul, FloatMembers.OpMul);
        DefineMethod(FloatClass, Names.OpDiv, FloatMembers.OpDiv);
        DefineMethod(FloatClass, Names.OpMod, FloatMembers.Mod);
        DefineMethod(FloatClass, Names.OpCmp, FloatMembers.OpCmp);
        DefineMethod(FloatClass, Names.OpLt, FloatMembers.OpLt);
        DefineMethod(FloatClass, Names.OpLe, FloatMembers.OpLe);
        DefineMethod(FloatClass, Names.OpGt, FloatMembers.OpGt);
        DefineMethod(FloatClass, Names.OpGe, FloatMembers.OpGe);
        DefineMethod(FloatClass, Names.OpEq, FloatMembers.OpEq);
        DefineMethod(FloatClass, Names.OpPlus, (state, self) => self);
        DefineMethod(FloatClass, Names.OpMinus, FloatMembers.OpNeg);
        DefineMethod(FloatClass, Names.OpAnd, FloatMembers.OpAnd);
        DefineMethod(FloatClass, Names.OpOr, FloatMembers.OpOr);
        DefineMethod(FloatClass, Names.OpXor, FloatMembers.OpXor);
        DefineMethod(FloatClass, Names.OpLShift, FloatMembers.OpLshift);
        DefineMethod(FloatClass, Names.OpRShift, FloatMembers.OpRshift);
        DefineMethod(FloatClass, Intern("ceil"u8), FloatMembers.Ceil);
        DefineMethod(FloatClass, Intern("finite?"u8), FloatMembers.QFinite);
        DefineMethod(FloatClass, Intern("floor"u8), FloatMembers.Floor);
        DefineMethod(FloatClass, Intern("infinite?"u8), FloatMembers.QInfinite);
        DefineMethod(FloatClass, Intern("round"u8), FloatMembers.Round);
        DefineMethod(FloatClass, Intern("to_f"u8), FloatMembers.ToF);
        DefineMethod(FloatClass, Names.ToI, FloatMembers.ToI);
        DefineMethod(FloatClass, Intern("truncate"u8), FloatMembers.Truncate);
        DefineMethod(FloatClass, Intern("divmod"u8), FloatMembers.DivMod);
        DefineMethod(FloatClass, Names.ToS, FloatMembers.ToS);
        DefineMethod(FloatClass, Names.Inspect, FloatMembers.Inspect);
        DefineMethod(FloatClass, Intern("nan?"u8), FloatMembers.QNan);
        DefineMethod(FloatClass, Intern("abs"u8), FloatMembers.Abs);
        DefineMethod(FloatClass, Names.Hash, FloatMembers.Hash);
        DefineMethod(FloatClass, Names.QEql, FloatMembers.QEql);
        DefineMethod(FloatClass, Intern("quo"u8), FloatMembers.Quo);
        DefineMethod(FloatClass, Intern("div"u8), FloatMembers.Div);
        DefineConst(FloatClass, Intern("INFINITY"u8), double.PositiveInfinity);
        DefineConst(FloatClass, Intern("NAN"u8), double.NaN);
    }

    void InitString()
    {
        DefineMethod(KernelModule, Intern("__ENCODING__"u8), (state, _) =>
            state.NewString("UTF-8"u8));

        StringClass = DefineClass(Intern("String"u8), ObjectClass, MRubyVType.String);
        DefineMethod(StringClass, Names.Initialize, StringMembers.Initialize);
        DefineMethod(StringClass, Names.InitializeCopy, StringMembers.InitializeCopy);
        DefineMethod(StringClass, Names.OpEq, StringMembers.OpEq);
        DefineMethod(StringClass, Names.QEql, StringMembers.OpEq);
        DefineMethod(StringClass, Names.OpCmp, StringMembers.OpCmp);
        DefineMethod(StringClass, Names.OpAref, StringMembers.OpAref);
        DefineMethod(StringClass, Names.OpAset, StringMembers.OpAset);
        DefineMethod(StringClass, Names.OpMul, StringMembers.Times);
        DefineMethod(StringClass, Names.Inspect, StringMembers.Inspect);
        DefineMethod(StringClass, Names.ToSym, StringMembers.ToSym);
        DefineMethod(StringClass, Names.ToS, StringMembers.ToS);
        DefineMethod(StringClass, Names.ToI, StringMembers.ToI);
        DefineMethod(StringClass, Intern("to_f"u8), StringMembers.ToF);
        DefineMethod(StringClass, Intern("size"u8), StringMembers.Size);
        DefineMethod(StringClass, Intern("length"u8), StringMembers.Size);
        DefineMethod(StringClass, Intern("empty?"u8), StringMembers.Empty);
        DefineMethod(StringClass, Intern("include?"u8), StringMembers.Include);
        DefineMethod(StringClass, Intern("index"u8), StringMembers.Index);
        DefineMethod(StringClass, Intern("rindex"u8), StringMembers.RIndex);
        DefineMethod(StringClass, Intern("intern"u8), StringMembers.Intern);
        DefineMethod(StringClass, Intern("replace"u8), StringMembers.Replace);
        DefineMethod(StringClass, Intern("chomp"u8), StringMembers.Chomp);
        DefineMethod(StringClass, Intern("chomp!"u8), StringMembers.ChompBang);
        DefineMethod(StringClass, Intern("chop"u8), StringMembers.Chop);
        DefineMethod(StringClass, Intern("chop!"u8), StringMembers.ChopBang);
        DefineMethod(StringClass, Intern("upcase"u8), StringMembers.Upcase);
        DefineMethod(StringClass, Intern("upcase!"u8), StringMembers.UpcaseBang);
        DefineMethod(StringClass, Intern("downcase"u8), StringMembers.Downcase);
        DefineMethod(StringClass, Intern("downcase!"u8), StringMembers.DowncaseBang);
        DefineMethod(StringClass, Intern("capitalize"u8), StringMembers.Capitalize);
        DefineMethod(StringClass, Intern("capitalize!"u8), StringMembers.CapitalizeBang);
        DefineMethod(StringClass, Intern("reverse"u8), StringMembers.Reverse);
        DefineMethod(StringClass, Intern("reverse!"u8), StringMembers.ReverseBang);
        DefineMethod(StringClass, Intern("slice"u8), StringMembers.OpAref);
        DefineMethod(StringClass, Intern("split"u8), StringMembers.Split);

        DefineMethod(StringClass, Intern("bytesize"u8), StringMembers.ByteCount);
        DefineMethod(StringClass, Intern("bytes"u8), StringMembers.Bytes);
        DefineMethod(StringClass, Intern("getbyte"u8), StringMembers.GetByte);
        DefineMethod(StringClass, Intern("setbyte"u8), StringMembers.SetByte);
        DefineMethod(StringClass, Intern("byteindex"u8), StringMembers.ByteIndex);
        DefineMethod(StringClass, Intern("byteslice"u8), StringMembers.BytesSlice);
        DefineMethod(StringClass, Intern("bytesplice"u8), StringMembers.ByteSplice);

        DefineMethod(StringClass, Intern("__sub_replace"u8), StringMembers.InternalSubReplace);
    }

    void InitArray()
    {
        ArrayClass = DefineClass(Intern("Array"u8), ObjectClass, MRubyVType.Array);

        DefineClassMethod(ArrayClass, Names.OpAref, ArrayMembers.Create);

        DefineMethod(ArrayClass, Names.OpEq, ArrayMembers.OpEq);
        DefineMethod(ArrayClass, Names.QEql, ArrayMembers.Eql);
        DefineMethod(ArrayClass, Names.OpLShift, ArrayMembers.Push);
        DefineMethod(ArrayClass, Names.OpAdd, ArrayMembers.OpAdd);
        DefineMethod(ArrayClass, Names.OpAref, ArrayMembers.OpAref);
        DefineMethod(ArrayClass, Names.OpAset, ArrayMembers.OpAset);
        DefineMethod(ArrayClass, Names.OpAdd, ArrayMembers.Plus);
        DefineMethod(ArrayClass, Names.OpMul, ArrayMembers.Times);
        DefineMethod(ArrayClass, Intern("push"u8), ArrayMembers.Push);
        DefineMethod(ArrayClass, Intern("concat"u8), ArrayMembers.Concat);
        DefineMethod(ArrayClass, Intern("size"u8), ArrayMembers.Size);
        DefineMethod(ArrayClass, Intern("length"u8), ArrayMembers.Size);
        DefineMethod(ArrayClass, Intern("empty?"u8), ArrayMembers.Empty);
        DefineMethod(ArrayClass, Intern("first"u8), ArrayMembers.First);
        DefineMethod(ArrayClass, Intern("last"u8), ArrayMembers.Last);
        DefineMethod(ArrayClass, Intern("reverse"u8), ArrayMembers.Reverse);
        DefineMethod(ArrayClass, Intern("reverse!"u8), ArrayMembers.ReverseBang);
        DefineMethod(ArrayClass, Intern("pop"u8), ArrayMembers.Pop);
        DefineMethod(ArrayClass, Intern("delete_at"u8), ArrayMembers.DeleteAt);
        DefineMethod(ArrayClass, Intern("clear"u8), ArrayMembers.Clear);
        DefineMethod(ArrayClass, Intern("index"u8), ArrayMembers.Index);
        DefineMethod(ArrayClass, Intern("rindex"u8), ArrayMembers.RIndex);
        DefineMethod(ArrayClass, Intern("join"u8), ArrayMembers.Join);
        DefineMethod(ArrayClass, Intern("replace"u8), ArrayMembers.Replace);
        DefineMethod(ArrayClass, Intern("shift"u8), ArrayMembers.Shift);
        DefineMethod(ArrayClass, Intern("unshift"u8), ArrayMembers.Unshift);
        DefineMethod(ArrayClass, Intern("slice"u8), ArrayMembers.OpAref);
        DefineMethod(ArrayClass, Names.ToS, ArrayMembers.ToS);
        DefineMethod(ArrayClass, Names.Inspect, ArrayMembers.Inspect);
        DefineMethod(ArrayClass, Names.InitializeCopy, ArrayMembers.Replace);

        DefineMethod(ArrayClass, Intern("__ary_eq"u8), ArrayMembers.InternalEq);
        DefineMethod(ArrayClass, Intern("__ary_cmp"u8), ArrayMembers.InternalCmp);
        DefineMethod(ArrayClass, Intern("__svalue"u8), ArrayMembers.InternalSValue);
    }

    void InitHash()
    {
        HashClass = DefineClass(Intern("Hash"u8), ObjectClass, MRubyVType.Hash);
        DefineMethod(HashClass, Names.Initialize, HashMembers.Initialize);
        DefineMethod(HashClass, Names.InitializeCopy, HashMembers.InitializeCopy);
        DefineMethod(HashClass, Names.ToS, HashMembers.Inspect);
        DefineMethod(HashClass, Names.Inspect, HashMembers.Inspect);
        DefineMethod(HashClass, Names.OpAref, HashMembers.OpAref);
        DefineMethod(HashClass, Names.OpAset, HashMembers.OpAset);
        DefineMethod(HashClass, Intern("size"u8), HashMembers.Size);
        DefineMethod(HashClass, Intern("length"u8), HashMembers.Size);
        DefineMethod(HashClass, Intern("keys"u8), HashMembers.Keys);
        DefineMethod(HashClass, Intern("values"u8), HashMembers.Values);
        DefineMethod(HashClass, Intern("has_key?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("key?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("has_value?"u8), HashMembers.HasValue);
        DefineMethod(HashClass, Intern("value?"u8), HashMembers.HasValue);
        DefineMethod(HashClass, Intern("include?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("member?"u8), HashMembers.HasKey);
        DefineMethod(HashClass, Intern("empty?"u8), HashMembers.Empty);
        DefineMethod(HashClass, Intern("default"u8), HashMembers.Default);
        DefineMethod(HashClass, Intern("default_proc"u8), HashMembers.DefaultProc);
        DefineMethod(HashClass, Intern("default="u8), HashMembers.SetDefault);
        DefineMethod(HashClass, Intern("size"u8), HashMembers.Size);
        DefineMethod(HashClass, Intern("clear"u8), HashMembers.Clear);
        DefineMethod(HashClass, Intern("replace"u8), HashMembers.InitializeCopy);
        DefineMethod(HashClass, Intern("store"u8), HashMembers.OpAset);
        DefineMethod(HashClass, Intern("shift"u8), HashMembers.Shift);
        DefineMethod(HashClass, Intern("assoc"u8), HashMembers.Assoc);
        DefineMethod(HashClass, Intern("rassoc"u8), HashMembers.RAssoc);
        DefineMethod(HashClass, Intern("rehash"u8), HashMembers.Rehash);
        DefineMethod(HashClass, Intern("__delete"u8), HashMembers.InternalDelete);
        DefineMethod(HashClass, Intern("__merge"u8), HashMembers.InternalMerge);
    }

    void InitRange()
    {
        RangeClass = DefineClass(Intern("Range"u8), ObjectClass, MRubyVType.Range);
        DefineMethod(RangeClass, Intern("begin"u8), RangeMembers.Begin);
        DefineMethod(RangeClass, Intern("end"u8), RangeMembers.End);
        DefineMethod(RangeClass, Intern("first"u8), RangeMembers.First);
        DefineMethod(RangeClass, Intern("last"u8), RangeMembers.Last);
        DefineMethod(RangeClass, Intern("exclude_end?"u8), RangeMembers.ExcludeEnd);
        DefineMethod(RangeClass, Intern("member?"u8), RangeMembers.IsInclude);
        DefineMethod(RangeClass, Intern("eql?"u8), RangeMembers.OpEql);
        DefineMethod(RangeClass, Names.Initialize, RangeMembers.Initialize);
        DefineMethod(RangeClass, Names.InitializeCopy, RangeMembers.InitializeCopy);
        DefineMethod(RangeClass, Names.OpEq, RangeMembers.OpEq);
        DefineMethod(RangeClass, Names.OpEqq, RangeMembers.IsInclude);
        DefineMethod(RangeClass, Names.QInclude, RangeMembers.IsInclude);
        DefineMethod(RangeClass, Names.ToS, RangeMembers.ToS);
        DefineMethod(RangeClass, Names.Inspect, RangeMembers.Inspect);
        DefineMethod(RangeClass, Intern("__num_to_a"u8), RangeMembers.InternalNumToA);
    }

    void InitEnumerable()
    {
        var enumerableModule = DefineModule(Intern("Enumerable"u8), ObjectClass);
        DefineMethod(enumerableModule, Intern("__update_hash"u8), EnumerableMembers.InternalUpdateHash);
    }

    void InitMrbLib()
    {
        LoadBytecode(LibEmbedded.Bytes);
    }

    void InitFiber()
    {
        FiberClass = DefineClass(Intern("Fiber"u8), ObjectClass, MRubyVType.Fiber);

        DefineMethod(FiberClass, Names.Initialize, FiberMembers.Initialize);
        DefineMethod(FiberClass, Names.OpEq, FiberMembers.OpEq);
        DefineMethod(FiberClass, Names.ToS, FiberMembers.ToS);
        DefineMethod(FiberClass, Names.Inspect, FiberMembers.ToS);
        DefineMethod(FiberClass, Intern("resume"u8), FiberMembers.Resume);
        DefineMethod(FiberClass, Intern("transfer"u8), FiberMembers.Transfer);
        DefineMethod(FiberClass, Intern("alive?"u8), FiberMembers.Alive);
        DefineClassMethod(FiberClass, Intern("yield"u8), FiberMembers.Yield);
        DefineClassMethod(FiberClass, Intern("current"u8), FiberMembers.Current);

        DefineClass(Intern("FiberError"u8), StandardErrorClass);
    }

    void InitObjectExt()
    {
        DefineMethod(NilClass, Names.ToA, (state, _) => state.NewArray(0));
        DefineMethod(NilClass, Names.ToI, (state, _) => 0);
        DefineMethod(NilClass, Names.ToF, (state, _) => 0.0);
        DefineMethod(NilClass, Intern("to_h"u8), (state, self) => state.NewHash(0));

        DefineMethod(KernelModule, Intern("itself"u8), MRubyMethod.Identity);

        // TODO: impl `instance_exec`
    }

    bool TrySetClassPathLink(RClass outer, RClass c, Symbol name)
    {
        if (c.InstanceVariables.TryGet(Names.OuterKey, out _)) return false;

        c.InstanceVariables.Set(Names.OuterKey, outer);
        outer.InstanceVariables.Set(name, c);

        if (!c.InstanceVariables.TryGet(Names.ClassNameKey, out _))
        {
            c.InstanceVariables.Set(Names.ClassNameKey, name);
        }
        return true;
    }
}
