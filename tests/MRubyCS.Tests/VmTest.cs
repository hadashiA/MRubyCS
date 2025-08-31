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
    public void IntegerOverflowAdd()
    {
        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = 9223372036854775807
                                   a += 8
                                  return a
                 """u8);
        }, "Integer overflow in add (RangeError)");

        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = 9223372036854775807
                                   a += 8000
                                  return a
                 """u8);
        }, "Integer overflow in add (RangeError)");
    }

    [Test]
    public void IntegerOverflowSub()
    {
        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = -9223372036854775806
                                   a -= 8
                                  return a
                 """u8);
        }, "Integer overflow in sub (RangeError)");

        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = -9223372036854775806
                                   a -= 8000
                                  return a
                 """u8);
        }, "Integer overflow in sub (RangeError)");
    }

    [Test]
    public void IntegerOverflowMul()
    {
        Assert.Throws<MRubyRaiseException>(() =>
        {
            Exec("""
                                  a = 10000000000
                                   a *= 10000000000
                                  return a
                 """u8);
        }, "Integer overflow in mul (RangeError)");
    }

    [Test]
    public void GetInstances()
    {
        Exec("""
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
             """u8);

        // get class instance
        var classA = mrb.GetConst(mrb.Intern("A"u8), mrb.ObjectClass);

        // call class method
        mrb.Send(classA, mrb.Intern("foo="u8), MRubyValue.From(123));
        var result1 = mrb.Send(classA, mrb.Intern("foo"u8));
        Assert.That(result1, Is.EqualTo(MRubyValue.From(123)));

        // root instance variable
        var instanceB = mrb.GetInstanceVariable(mrb.TopSelf, mrb.Intern("@b"u8));
        mrb.Send(instanceB, mrb.Intern("bar="u8), MRubyValue.From(123));

        var result2 = mrb.Send(instanceB, mrb.Intern("bar"u8));
        Assert.That(result2, Is.EqualTo(MRubyValue.From(123)));

        // find class instance on the hierarchy
        var classC = mrb.Send(mrb.ObjectClass, mrb.Intern("const_get"u8), mrb.NewString("M::C"u8));
        Assert.That(classC.IsObject, Is.True);
    }

    MRubyValue Exec(ReadOnlySpan<byte> code)
    {
        var irep = compiler.Compile(code);
        return mrb.Execute(irep);
    }
}