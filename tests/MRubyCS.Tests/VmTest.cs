using MRubyCS.Compiler;

namespace MRubyCS.Tests;

[TestFixture]
public class VmTest
{
    MRubyState mrb = default!;
    MRubyCompiler compiler = default!;

    [SetUp]
    public void Before()
    {
        mrb = MRubyState.Create();
        compiler = MRubyCompiler.Create(mrb);

        mrb.DefineMethod(mrb.ObjectClass, mrb.Intern("__log"u8), (state, _) =>
        {
            var arg = state.GetArgumentAt(0);
            TestContext.Out.WriteLine(state.Stringify(arg).ToString());
            return MRubyValue.Nil;
        });
    }

    [TearDown]
    public void After()
    {
        compiler.Dispose();
    }

    [Test]
    public void Recursive()
    {
        var result = Exec("""
                          def fibonacci(n)
                            return n if n <= 1
                            fibonacci(n - 1) + fibonacci(n - 2)
                          end

                          fibonacci 10
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(55)));
    }

    [Test]
    public void StackOverflow()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            Exec("""
                 def f(x)
                   f(x + 1)
                 end
                 f(1)
                 """u8);
        }, "stack level too deep");
    }

    [Test]
    public void Closure()
    {
         var result = Exec("""
                           def fb
                             n = 0
                             Proc.new do
                               n += 1
                               case
                               when n % 15 == 0
                               else n
                               end
                             end
                           end
                           fb.call
                           """u8);
         Assert.That(result, Is.EqualTo(MRubyValue.From(1)));
    }

    [Test]
    public void CatchHandler()
    {
        var result = Exec("""
                          loops = 0
                          limit = 2
                          loop do
                            begin
                              limit -= 1
                              break unless limit > 0
                              raise "!"
                            rescue
                              redo
                            ensure
                              loops += 1
                            end
                          end
                          loops
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(2)));
    }

    [Test]
    public void ModuleInclude()
    {
        var result = Exec("""
                          module M
                            def foo
                              123
                            end
                          end

                          class A
                            include M
                          end

                          A.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void ClassNew()
    {
        var result = Exec("""
                          c = Class.new do
                            def foo
                              123
                            end
                          end
                          c.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void NewWithBlock()
    {
        var result = Exec("""
                          a = Array.new(1) { 123 }
                          a[0]
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void ReturnBlk()
    {
        var result = Exec("""
                          [1,2,3].find{|x| x == 2 }
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(2)));
    }

    [Test]
    public void DefineAttr()
    {
        var result = Exec("""
                          class Foo
                            attr_reader :a

                            def initialize(a)
                              @a = a
                            end
                          end
                          Foo.new(123).a
                          """u8);

        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void NoStrictProcCall()
    {
        var result = Exec("""
                          def iter
                            yield 1
                          end
                          iter do |a, b=2, c|
                            c
                          end
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.Nil));
    }

    [Test]
    public void InstanceEval()
    {
        var result = Exec("""
                          class A
                            attr_reader :x

                            def foo
                              @x = 123
                            end
                          end

                          a = A.new
                          a.instance_eval { foo }
                          a.x
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void ClassEval()
    {
        var result = Exec("""
                          class A
                          end

                          A.class_eval do
                            def foo = 123
                          end

                          A.new.foo
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.From(123)));
    }

    [Test]
    public void Hoge()
    {
        var result = Exec("""

                          class HashKey
                            attr_accessor :value, :error, :callback

                            self.class.alias_method :[], :new

                            def initialize(value, error: nil, callback: nil)
                              @value = value
                              @error = error
                              @callback = callback
                            end

                            def ==(other)
                              @callback.(:==, self, other) if @callback
                              return raise_error(:==) if @error == true || @error == :==
                              other.kind_of?(self.class) && @value == other.value
                            end

                            def eql?(other)
                              @callback.(:eql?, self, other) if @callback
                              return raise_error(:eql?) if @error == true || @error == :eql?
                              other.kind_of?(self.class) && @value.eql?(other.value)
                            end

                            def hash
                              @callback.(:hash, self) if @callback
                              return raise_error(:hash) if @error == true || @error == :hash
                              @value % 3
                            end

                            def to_s
                              "#{self.class}[#{@value}]"
                            end
                            alias inspect to_s

                            def raise_error(name)
                              raise "##{self}: #{name} error"
                            end
                          end

                          class HashEntries < Array
                            self.class.alias_method :[], :new

                            def initialize(entries) self.replace(entries) end
                            def key(index, k=get=true) get ? self[index][0] : (self[index][0] = k) end
                            def value(index, v=get=true) get ? self[index][1] : (self[index][1] = v) end
                            def keys; map{|k, v| k} end
                            def values; map{|k, v| v} end
                            def each_key(&block) each{|k, v| block.(k)} end
                            def each_value(&block) each{|k, v| block.(v)} end
                            def dup2; self.class[*map{|k, v| [k.dup, v.dup]}] end
                            def to_s; "#{self.class}#{super}" end
                            alias inspect to_s

                            def hash_for(hash={}, &block)
                              each{|k, v| hash[k] = v}
                              block.(hash) if block
                              hash
                            end
                          end

                          def ar_entries
                            HashEntries[
                              [1, "one"],
                              [HashKey[2], :two],
                              [nil, :two],
                              [:one, 1],
                              ["&", "&amp;"],
                              [HashKey[6], :six],
                              [HashKey[5], :five],  # same hash code as HashKey[2]
                            ]
                          end

                          def ht_entries
                            ar_entries.dup.push(
                              ["id", 32],
                              [:date, "2020-05-02"],
                              [200, "OK"],
                              ["modifiers", ["left_shift", "control"]],
                              [:banana, :yellow],
                              ["JSON", "JavaScript Object Notation"],
                              [:size, :large],
                              ["key_code", "h"],
                              ["h", 0x04],
                              [[3, 2, 1], "three, two, one"],
                              [:auto, true],
                              [HashKey[12], "December"],
                              [:path, "/path/to/file"],
                              [:name, "Ruby"],
                            )
                          end

                          proc = ->(h, k){ h[k] = k * 3 }

                          h = Hash.new(&proc)
                          h2 = ar_entries.hash_for(h)

                          h2.default -2
                          """u8);
        Assert.That(result, Is.EqualTo(MRubyValue.True));
    }

    MRubyValue Exec(ReadOnlySpan<byte> code)
    {
        var irep = compiler.Compile(code);
        return mrb.Exec(irep);
    }
}
