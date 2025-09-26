# MRubyCS

> [!NOTE]
> This project was initially called MRubyD, but was renamed to MRubyCS.


MRubyCS is a new [mruby](https://github.com/mruby/mruby) virtual machine implemented in pure C#. The name "mruby/cs" stands for *mruby implemented in C#*. Designed with seamless integration in mind for C#-based game engines, and emphasize ruby level compatibility. MRubyCS leverages the latest C# features for high performance and high extensibility.

## Features

- **Implemented in C#**
  Utilizes the robust capabilities of C# to ensure seamless integration with C#-based game engines.

- **High Performance**
  Takes advantage of modern C# language features such as managed pointers, `Span`, and the performance benefits of the .NET runtimes GC and JIT compiler to deliver superior speed and efficiency.

- **High compatibility with Ruby-level APIs**
  It is intended for use in software with a certain amount of resources, such as games/gui-apps. For this reason, we are focusing on Ruby API compatibility.
  At this time, all opcodes are implemented and pass the [syntax.rb](https://github.com/hadashiA/MRubyCS/blob/main/tests/MRubyCS.Tests/ruby/test/syntax.rb), [class.rb](https://github.com/hadashiA/MRubyCS/blob/main/tests/MRubyCS.Tests/ruby/test/class.rb), [module.rb](https://github.com/hadashiA/MRubyCS/blob/main/tests/MRubyCS.Tests/ruby/test/module.rb) and built-in libs tests from the original mruby repository.

- **Portability & Extensibility**
  Compared to the original C implementation, calling C# extensive libraries from Ruby is straightforward, making the VM highly extensible.

- **Fiber and async/await integration**
  You can communicate between the Ruby world and the C# world like channels without blocking threads with `Fiber`. This enables integration such as suspending Ruby and then executing async methods in the C# world.

## Limitations

- `private` and `protected` visibitily is not yet implemented. (mruby got support for this in 3.4)

### Most recent roadmap

- [ ] [VitalRouter.MRuby](https://github.com/hadashiA/VitalRouter) for the new version.

## Table of Contents

- [Installation](#installation)
    - [NuGet](#nuget)
    - [Unity](#unity)
- [Basic Usage](#basic-usage)
    - [MRubyValue](#mrubyvalue)
        - [Symbol/String](#symbolstring)
        - [Embeded custom C# data into MRubyValue](#embedded-custom-c-data-into-mrubyvalue)
    - [Define ruby class/module/method by C#](#define-ruby-classmodulemethod-by-c)
    - [Call ruby method from C# side](#call-ruby-method-from-c-side)
- [Compiling Ruby source code](#compiling-ruby-source-code)
    - [MRubyCS.Compiler](#mrubycscompiler)
- [Fiber (Coroutine)](#fiber-coroutine)
- [MRubyCS.Serializer](#mrubycsserializer)

## Installation

### NuGet

| Package   | Description    | Latest version |
|:----------|:---------------|----------------|
| MRubyCS   |  Main package. A mruby vm implementation. | [![NuGet](https://img.shields.io/nuget/v/MRubyCS)](https://www.nuget.org/packages/MRubyCS) |
| MRubyCS.Compiler | Compile ruby source code utility. (Native binding)  | [![NuGet](https://img.shields.io/nuget/v/MRubyCS.Compiler)](https://www.nuget.org/packages/MRubyCS.Compiler)   |
| MRubyCS.Serializer  | Converting Ruby and C# Objects Between Each Other | [![NuGet](https://img.shields.io/nuget/v/MRubyCS.Serializer)](https://www.nuget.org/packages/MRubyCS.Serializer)  |

### Unity

> [!NOTE]
> Requirements: Unity 2021.3 or later.

1. Install [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity).
2. Install following pacakges via NugetForUnity
    - Utf8StringInterpolation
    - MRubyCS
    - (Optional) MRubyCS.Serializer
3. (Optional) To install utilities for generating mrb bytecode, refer to the [Compiling Ruby source code](#compiling-ruby-source-code) section.

## Basic Usage

```ruby
def fibonacci(n)
  return n if n <= 1
  fibonacci(n - 1) + fibonacci(n - 2)
end

fibonacci 10
```

> [!NOTE]
> MRubyCS is a C# implementation of the bytecode machine component, with the source code compiler separated.
> You will likely use either `mrbc` or `MRubyCS.Compiler`.
> For details,  please refer to the [Compiling Ruby source code](#compiling-ruby-source-code) .

``` bash
$ mrbc -o fibonaci.mrbc fibonacci.rb
```

``` cs
using MRubyCS;

// initialize state
var mrb = MRubyState.Create();

// Read the .mrb byte-code.
var bytecode = File.ReadAllBytes("fibonacci.mrb");

// execute bytecoe
var result = mrb.LoadBytecode(bytecode);

result.IsInteger    //=> true
result.IntegerValue //=> 55
```

You can also parse bytecode in advance.
The result of parsing bytecode is called `Irep` in mruby terminology.

``` cs
Irep irep = mrb.ParseBytecode(bytecode);

mrb.Execute(irep);
```

`Irep` can be executed as is, or converted to `Proc`, `Fiber` before use. For details on Fiber, refer to the [Fiber](#fiber-coroutine) section.

### `MRubyValue`

Above `result` is `MRubyValue`. This represents a Ruby value.

``` cs
value.IsNil //=> true if `nil`
value.IsInteger //=> true if integrr
value.IsFloat //=> true if float
value.IsSymbol //=> true if Symbol
value.IsObject //=> true if any allocated object type

value.VType //=> get known ruby-type as C# enum.

value.IntegerValue //=> get as C# Int64
value.FloatValue //=> get as C# float
value.SymbolValue //=> get as `Symbol`

value.As<RString>() //=> get as internal String representation
value.As<RArray>() //=> get as internal Array representation
value.As<RHash>() //=> get as internal Hash representation

// pattern matching
if (vlaue.Object is RString str)
{
    // ...
}

swtich (value)
{
    case { IsInteger: true }:
        // ...
        break;
    case { Object: RString str }:
        // ...
        break;
}

var intValue = new MRubyValue(100); // create int value
var floatValue = new MRubyValue(1.234f); // create float value
var objValue = new MRubyValue(str); // create allocated ruby object value

// Or, we can cast implicitly and target-typed new
MRubyValue intValue = new(100);
```

#### Symbol/String

The string representation within mruby is utf8.
Therefore, to generate a ruby string from C#, [Utf8StringInterpolation](https://github.com/Cysharp/Utf8StringInterpolation) is used internally.


```cs
// Create string literal.
var str1 = mrb.NewString("HOGE HOGE"u8); // use u8 literal (C# 11 or newer)
var str2 = mrb.NewString($"FOO BAR"); // use string interpolation

var x = 123;
var str3 = mrb.NewString($"x={x}");

// wrap MRubyValue..
var strValue = new MRubyValue(str1);
```

There is a concept in mruby similar to String called `Symbol`.
Like String, it is created using utf8 strings, but internally it is a uint integer.
Symbols are usually used for method IDs and class IDs.

To create a symbol from C#, use `Intern`.

```cs
// symbol literal
var sym1 = mrb.Intern("sym");

// create symbol from string interporation
var x = 123;
var sym2 = mrb.Intern($"sym{x}");

// symbol to utf8 bytes
mrb.NameOf(sym1); //=> "sym"u8
mrb.NameOf(sym2); //=> "sym123"u8

// create symbol from string
var sym2 = mrb.ToSymbol(mrb.NewString($"hoge"));
```

> [!NOTE]
> Both `Intern(“str”)` and `Intern(“str”u8)` are valid, but the u8 literal is faster. We recommend using the u8 literal whenever possible.

#### Embeded custom C# data into MRubyValue

You can stuff any C# object into an MRubyValue.

This is useful when calling C# functionality from Ruby methods defined in C#.

```cs
class YourCustomClass
{
    public string Value { get; set; }
}

var csharpInstance = new YourCustomClass { Value = "abcde" };

var mrb = MRubyState.Create();

var data = new RData(csharpInstance);
state.SetConst(state.Intern("MYDATA"u8), state.ObjectClass, data);

// Use custom data from ruby
mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("from_csharp_data"), (_, self) =>
{
    var dataValue = mrb.GetConst(state.Intern("MYDATA"u8), mrb.ObjectClass);
    var csharpInstance = dataValue.As<RData>().Data as YourCustomClass;
    // ...
});
```

### Define ruby class/module/method by C#

``` cs
// Define class
var classA = mrb.DefineClass(Intern("A"), c =>
{
    // Method definition that takes a required argument.
    c.DefineMethod(Intern("plus100"), (_, self) =>
    {
        var arg0 = mrb.GetArgumentAsIntegerAt(0); // get first argument (index:0)
        return arg0 + 100;
    });

    // Method definition that takes a block argument.
    c.DefineMethod(mrb.Intern("method2"), (_, self) =>
    {
        var arg0 = mrb.GetArgumentAt(0);
        var blockArg = mrb.GetBlockArgument();
        if (!blockArg.IsNil)
        {
            // Execute `Proc#call`
            mrb.Send(blockArg, mrb.Intern("call"), arg0);
        }
    });

    // Other complex arguments...
    c.DefineMethod(mrb.Intern("method3"), (_, self) =>
    {
        var keywordArg = mrb.GetKeywordArgument(mrb.Intern("foo"))
        Console.WriteLine($"foo: {keywordArg}");

        // argument type checking
        mrb.EnsureValueType(keywordArg, MrubyVType.Integer);

        var restArguments = mrb.GetRestArgumentsAfter(0);
        for (var i = 0; i < restArguments.Length; i++)
        {
            Console.WriteLine($"rest arg({i}: {restArguments[i]})");
        }
    });

    // class method
    c.DefineClassMethod(Intern("classmethod1"), (_, self) =>
    {
        return mrb.NewString($"hoge fuga");
    });
});

// Monkey patching
classA.DefineMethod(mrb.Intern("additional_method1"u8), (_, self) => { /* ... */ });

// Define module
var moduleA = mrb.DefineModule(mrb.Intern("ModuleA");)
mrb.DefineMethod(moduleA, mrb.Intern("additional_method2"), (_, self) => new MRubyValue(123));

mrb.IncludeModule(classA, moduleA);
```

As a result of the definition, the following Ruby code can now be executed.

```ruby
a = A.new
a.plus100(123) #=> 223

a.method2(1) { |a| a } #=> 1

a.additionoa_method2 #=> 123

A.classmethod1 #=> "hoge fuga"
```

### Call ruby method from C# side

```ruby
class A
  def self.foo = @@foo

  def self.foo=(x)
    @@foo = x
  end
end

class B
  attr_accessor :bar
end
@b = B.new

module M
  class C
    def self.foo = 999
  end
end
```

```cs
// get class instance
var classA = mrb.GetConst(mrb.Intern("A"), mrb.ObjectClass);

// call class method
mrb.Send(classA, mrb.Intern("foo="), new MRubyValue(123));
mrb.Send(classA, mrb.Intern("foo")); //=> 123

// get instance variable from top
var instanceB = mrb.GetInstanceVariable(mrb.TopSelf, mrb.Intern("@b"));
mrb.Send(instanceB, mrb.Intern("bar="), 456);
mrb.Send(instanceB, mrb.Intern("bar")); //=> 456

// find class instance on the hierarchy
var classC = mrb.Send(mrb.ObjectClass, mrb.Intern("const_get"), mrb.NewString($"M::C"));
```


## Compiling Ruby source code

mruby has the following architecture, and allows the compiler and runtime to be separated.

By distributing only precompiled bytecode, you can optimize the installation on the application.

```mermaid
graph TB
    subgraph host["host machine"]
        A[source code<br/>.rb files]
        C[byte-code<br/>.mrb files]

        A -->|compile| C
    end

    C -->|deploy/install| E

    subgraph application["application"]
        D[mruby VM]
        E[byte-code<br>.mrb files]

        E -->|exucute byte-cose| D
    end
```

By the way, MRubyCS only includes the mruby virtual machine. Therefore it is necessary to convert it to .mrb bytecode before executing the .rb source.

##### `mrbc`

Using the original [mruby](https://github.com/mruby/mruby) project's compiler is one approach.

```bash
$ git clone git@github.com:mruby/mruby.git
$ cd mruby
$ rake
$ ./build/host/bin/mrubc
```

### MRubyCS.Compiler

To simplify compilation from C#, we  provide the MRubyCS.Compiler package, which is a thin wrapper of the C# API for the native compiler.

> [!NOTE]
> Currently, builds for linux (x64/arm64), macOS (x64/arm64), and windows (x64) are provided.

```cs
dotnet add package MRubyCS.Compiler
```

#### Unity

Open the Package Manager window by selecting Window > Package Manager, then click on [+] > Add package from git URL and enter the following URL:

```
https://github.com/hadashiA/MRubyCS.git?path=src/MRubyCS.Compiler.Unity/Assets/MRubyCS.Compiler#0.18.1
```

For manual compilation, refer to the following.

#### Usage

```cs
using MRubyCS.Compiler;

var source = """
def f(a)
  1 * a
end

f 100
"""u8;

var mrb = MRubyState.Create();
var compiler = MRubyCompiler.Create(mrb);

// Compile to irep (internal executable representation)
var irep = compiler.Compile(source);

// irep can be used later..
var result = mrb.Execute(irep); // => 100

// Compile to bytecode (mruby called this format is "Rite")
using var bin = compiler.CompileToBytecode(source);

// bytecode can be save a file or any other storage
File.WriteAllBytes("compiled.mrb", bin.AsSpan());

// Can be used later from file
mrb.LoadBytecode(File.ReadAllBytes("compiled.mrb")); //=> 100

// or, you can evaluate source code directly
result = compiler.LoadSourceCode("f(100)"u8);
result = compiler.LoadSourceCode("f(100)");
```

#### Unity AssetImporter

In Unity, if you install this extension, importing a .rb text file will generate .mrb bytecode as a subasset.

For example, importing the text file `hoge.rb` into a project will result in the following.

![docs/screenshot_subasset](./docs/screenshot_subasset.png)

This subasset is a TextAsset. To specify it in the inspector.

Or, to extract in C#, do the following:

``` cs
var mrb = MRubyState.Create();

var bytecodeAsset = (TextAsset)AssetDatabase.LoadAllAssetsAtPath("Assets/hoge.rb")
       .First(x => x.name.EndsWith(".mrb"));
mrb.LoadBytecode(bytecodeAsset.GetData<byte>().AsSpan());
```

To read a subasset in Addressables, you would do the following.

```cs
Addressables.LoadAssetAsync<TextAsset>("Assets/hoge.rb[hoge.mrb]")
```

Alternatively, you can generate the .mrb bytecode yourself within your project.

## Fiber (Coroutine)

MRubyCS supports Ruby Fibers, which are lightweight concurrency primitives that allow you to pause and resume code execution. In addition to standard Ruby Fiber features, MRubyCS provides seamless integration with C#'s async/await pattern.

### Basic Fiber Usage

```cs
using MRubyCS;
using MRubyCS.Compiler;

// Create state and compiler
var mrb = MRubyState.Create();
var compiler = MRubyCompiler.Create(mrb);

// Define a fiber that yields values
var code = """
    Fiber.new do |x|
      Fiber.yield(x * 2)
      Fiber.yield(x * 3)
      x * 4
    end
    """u8;

// Load the Ruby code as a Fiber
var irep = compiler.Compile(code);
var fiber = mrb.Execute(irep).As<RFiber>();

// Resume the fiber with initial value
var result1 = fiber.Resume(new MRubyValue(10));  // => 20

var result2 = fiber.Resume(new MRubyValue(10));  // => 30

var result3 = fiber.Resume(new MRubyValue(10));  // => 40 (final return value)

// Check if fiber is still alive
fiber.IsAlive  // => false
```

If you want to execute arbitrary code snippets as fibers, do the following.

```cs
var code = """
  x = 1
  y = 2
  Fiber.yield (x + y) * 100
  Fiber.yield (x + y) * 200
"""u8);

var fiber = compiler.LoadSourceCodeAsFiber(code);

// `LoadSourceCodeAsFiber` is same as:
// var irep = compiler.Compile(code);
// var proc = mrb.CreateProc(irep);
// var fiber = mrb.CreateFiber(proc);

fiber.Resume(); //=> 300
fiber.Resume(); //=> 600
```

### Async/Await Integration

MRubyCS provides unique C# async integration features for working with Fibers:

```cs
// Wait for fiber to terminate
var code = """
    Fiber.new do |x|
      Fiber.yield
      Fiber.yield
      "done"
    end
    """u8;

var irep = compiler.Compile(code);
var fiber = mrb.Execute(irep).As<RFiber>();

// Start async wait before resuming
var terminateTask = fiber.WaitForTerminateAsync();

// Resume the fiber multiple times
fiber.Resume();
fiber.Resume();
fiber.Resume();

// Wait for completion
await terminateTask;
Console.WriteLine("Fiber has terminated");
```

You can consume fiber results as async enumerable:

```cs
var code = """
    Fiber.new do |x|
      3.times do |i|
        Fiber.yield(x * (i + 1))
      end
    end
    """u8;

var irep = compiler.Compile(code);
var fiber = mrb.Execute(irep).As<RFiber>();

// Process each yielded value asynchronously
await foreach (var value in fiber.AsAsyncEnumerable())
{
    Console.WriteLine($"Yielded: {value.IntegerValue}");
}
```

MRubyCS supports multiple consumers waiting for fiber results simultaneously:

```cs
var irep = compiler.Compile(code);
var fiber = mrb.Execute(irep).As<RFiber>();

// Create multiple consumers
var consumer1 = Task.Run(async () =>
{
    while (fiber.IsAlive)
    {
        var result = await fiber.WaitForResumeAsync();
        Console.WriteLine($"Consumer 1 received: {result}");
    }
});

var consumer2 = Task.Run(async () =>
{
    while (fiber.IsAlive)
    {
        var result = await fiber.WaitForResumeAsync();
        Console.WriteLine($"Consumer 2 received: {result}");
    }
});

// Resume fiber and both consumers will receive the results
fiber.Resume(new MRubyValue(10));
fiber.Resume(new MRubyValue(20));
fiber.Resume(new MRubyValue(30));

await Task.WhenAll(consumer1, consumer2);
```

> [!CAUTION]
> Waiting for fiber can be performed in a separate thread.
> However, MRubyState and mruby methods are not thread-safe.
> Please note that when using mruby functions, you must always return to the original thread.

### Error Handling in Fibers

Exceptions raised within fibers are properly propagated:

```cs
var code = """
    Fiber.new do |x|
      Fiber.yield(x)
      raise "Something went wrong"
    end
    """u8;

var irep = compiler.Compile(code);
var fiber = mrb.Execute(irep).As<RFiber>();

// First resume succeeds
var result1 = fiber.Resume(new MRubyValue(10));  // => 10

// Second resume will throw
try
{
    fiber.Resume();
}
catch (MRubyRaiseException ex)
{
    Console.WriteLine($"Ruby exception: {ex.Message}");
}

// Async wait will also propagate the exception
var waitTask = fiber.WaitForResumeAsync();
try
{
    fiber.Resume();
    await waitTask;
}
catch (MRubyRaiseException ex)
{
    Console.WriteLine($"Async exception: {ex.Message}");
}
```

### yield/resume from C#

It is possible to resume/yield from a method defined in C#.

```cs
mrb.DefineMethod(mrb.FiberClass, mrb.Intern("resume_by_csharp"u8), (state, self) =>
{
    return self.As<RFiber>().Resume();
});
```

```ruby
 fiber = Fiber.new do
   3.times do
     Fiber.yield
   end
 end

 fiber.resume_by_csharp
```

## MRubyCS.Serializer

Using the MRuby.Serializer package enables conversion between MRubyValue and C# objects.

```cs
// Deserialize (MRubyValue -> C#)

MRubyValue result1 = mrb.LoadSourceCode("111 + 222");
MRubyValueSerializer.Deserialize<int>(result1, mrb); //=> 333

MRubyValue result2 = mrb.LoadSourceCode("'hoge'.upcase");
MRubyValueSerialize.Deserialize<string>(result2, mrb); //=> "HOGE"
```

```cs
// Serialize (C# -> MRubyValue)

var intArray = new int[] { 111, 222, 333 };

MRubyValue value = MRubyValueSerializer.Serialize(intArray, mrb);

var mrubyArray = value.As<RArray>();
mrubyArray[0] //=> 111
mrubyArray[1] //=> 222
mrubyArray[2] //=> 333
```

```cs
MRubyValue mrubyStringValue = MRubyValueSerialize.Serialize("hoge fuga", mrb);

// Use the serialized value...
mrb.Send(mrubyStringValue, mrb.Intern("upcase")); //=> MRubyValue("UPCASE")
```

### Builtin Supported types

The following C# types and MRubyValue type conversions are supported natively:

| mruby     | C#                                                                                                                                                                                                                                                                                                                                                                                      |
|-----------|:----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `Integer` | `int`, `uint`, `long`, `ulong`, `shot`, `ushot`, `byte`, `sbyte`, `char`                                                                                                                                                                                                                                                                                                                |
| `Float`   | `float`, `double`, `decimal`                                                                                                                                                                                                                                                                                                                                                            |
| `Array`   | `T`, `List<>`, `T[,]`, `T[,]`, `T[,,]`, <br />`Tuple<...>`, `ValueTuple<...>`, <br />, `Stack<>`, `Queue<>`, `LinkedList<>`, `HashSet<>`, `SortedSet<>`, <br />`Collection<>`, `BlockingCollection<>`, <br />`ConcurrentQueue<>`, `ConcurrentStack<>`, `ConcurrentBag<>`, <br />`IEnumerable<>`, `ICollection<>`, `IReadOnlyCollection<>`, <br />`IList<>`, `IReadOnlyList<>`, `ISet<>` |
| `Hash`    | `Dictionary<,>`, `SortedDictionary<,>`, `ConcurrentDictionary<,>`, <br />`IDictionary<,>`, `IReadOnlyDictionary<,>`                                                                                                                                                                                                                                                                     |
| `String`  | `string`, `byte[]`                                                                                                                                                                                                                                                                                                                                                                      |
| `Symbol`  | `Enum`
| `nil`     | `T?`, `Nullable<T>`                                                                                                                                                                                                                                                                                                                                                                     |


### `[MRubyObject]` attribute

Marking with `[MRubyObject]` enables bidirectional conversion between custom C# types and MRubyValue.

- Converts C# type properties/fields into Ruby world `Hash` key/value pairs.
- class, struct, and record are all supported.
- A partial declaration is required.
- Members that meet the following conditions are converted from mruby:
    - public fields or properties, or fields or properties with the `[MRubyMember]` attribute.
    - And have a setter (private is acceptable).

```cs
[MRubyObject]
partial struct SerializeExample
{
    // this is serializable members
    public string Id { get; private set; }
    public int X { get; init; }
    public int FooBar;

    [MRubyMember]
    public int Z;

    // ignore members
    [MRubyIgnore]
    public float Foo;
}
```

```cs
// Deserialize (MRubyValue -> C#)

var value = mrb.LoadSourceCode("{ id: 'aiueo', x: 1234, foo_bar: 4567, z: 8901 }");

SerializeExample deserialized = MRubyValueSerializer.Deserialize<SerializeExample>(value, mrb);
deserialized.Id     //=> "aiueo"
deserialized.X      //=> 1234
deserialized.FooBar //=> 4567
deserialized.Z      //=> 8901
```

```cs
// Serialize (C# -> MRubyVAlue)
var value = MRubyValueSerializer.Serialize(new SerializeExample { Id = "aiueo", X = 1234, FooBar = 4567 });

var props = value.As<RHash>();
props[mrb.Intern("id")] //=> "aiueo"
props[mrb.Intern("x")] //=> 1234
props[mrb.Intern("foo_bar")] //=> 4567
```

The list of properties specified by mruby is assigned to the C# member names that match the key names.

Note:
- The names on the ruby side are converted to CamelCase.
   - Example: ruby's `foo_bar` maps to C#'s `FooBar`.
- The values of C# enums are serialized as Ruby symbols.
    - Example: `Season.Summer` becomes Ruby's `:summer`.

You can change the member name specified from Ruby by using `[MRubyMember("alias name")]`.

```cs
[MRubyObject]
partial class Foo
{
    [MRubyMember("alias_y")]
    public int Y;
}
```

Also, you can receive data from Ruby via any constructor by using the `[MRubyConstructor]` attribute.

```cs
[MRubyObject]
partial class Foo
{
    public int X { ge; }

    [MRubyConstructor]
    public Foo(int x)
    {
        X = x;
    }
}
```

### Custom Formatter

You can also customize the conversion of any C# type to an MRubyValue.

```cs
 // custom type example
struct Vector3
{
    public int X;
    public int Y;
    public int Z;
}
```

```cs
// Implement `IMRubyValueFormatter`
class CustomVector3Formatter : IMRubyValueFormatter<A>
{
    public static readonly CustomAFormatter Instance = new();

    public MRubyValue Serialize(A value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        var array = mrb.NewArray();
        array.Push(value.X);
        array.Push(value.Y);
        array.Push(value.Z);
        return array;
    }
    public A Deserialize(MRubyValue value, MRubyState mrb, MRubyValueSerializerOptions options)
    {
        // validation
        MRubySerializationException.ThrowIfTypeMismatch(value, MRubyVType.Array);
        MRubySerializationException.ThrowIfNotEnoughArrayLength(value, 3);

        var array = mrb.As<RArray>();
        return new Vector3
        {
            X = array[0].IntegerValue,
            Y = array[1].IntegerValue,
            Z = array[2].IntegerValue,
        }
    }
}
```

To set a custom formatter, specify options as an argument to MRubyValueSerializer.

Specify the enumeration of Formatter and Formatter's Resolver instances.
`StandardResolver` supports the default behavior, so specify this along with additional formatters.

```cs
// Create a new formatter resolver.
var resolver = CompositeResolver.Create(
    [CustomAFormatter.Instance],
    [StandardResolver.Instance]
    );

var options = new MRubyValueSerializerOptions
{
    Resolver = resolver,
};

var value = mrb.LoadSourceCode("[111, 222, 333]");
Vectro3 deserialized = MRubyValueSerializer.Deserialize<Vector3>(value, mrb, options);
deserialized.X //=> 111
deserialized.Y //=> 222
deserialized.Z //=> 333
```

## LICENSE

MIT

## Contact

[@hadahsiA](https://x.com/hadashiA)

