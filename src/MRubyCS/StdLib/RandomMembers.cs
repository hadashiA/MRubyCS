using System;

namespace MRubyCS.StdLib;

class MRubyRandomData
{
    public Random Rand { get; private set; } = null!;
    public int Seed => seed;

    int seed;

    public MRubyRandomData()
    {
        ResetSeed();
    }

    public MRubyRandomData(int seed)
    {
        SetSeed(seed);
    }

    public void SetSeed(int seed)
    {
        this.seed = seed;
        Rand = new Random(seed);
    }

    public void ResetSeed()
    {
        SetSeed(Guid.NewGuid().GetHashCode());
    }
}

public class RandomMembers
{
    public static MRubyMethod DefaultRand = new((mrb, self) =>
    {
        var rand = DefaultInstanceOf(mrb).Rand;
        if (mrb.TryGetArgumentAt(0, out var maxValue))
        {
            var max = mrb.AsInteger(maxValue);
            return rand.Next(0, (int)max);
        }
        return rand.NextDouble();
    });

    public static MRubyMethod DefaultSRand = new((mrb, self) =>
    {
        var data =  DefaultInstanceOf(mrb);
        var currentSeed = data.Seed;
        if (mrb.TryGetArgumentAt(0, out var seedValue))
        {
            data.SetSeed((int)mrb.AsInteger(seedValue));
        }
        else
        {
            data.ResetSeed();
        }
        return currentSeed;
    });

    public static MRubyMethod DefaultBytes = new((mrb, self) =>
    {
        var length = mrb.GetArgumentAsIntegerAt(0);
        if (length < 0)
        {
            mrb.Raise(Names.ArgumentError, "negative string size"u8);
        }

        var rand =  DefaultInstanceOf(mrb).Rand;
        var bytes = new byte[length];
        rand.NextBytes(bytes);
        return mrb.NewStringOwned(bytes);
    });

    public static MRubyMethod Initialize = new((mrb, self) =>
    {
        if (mrb.TryGetArgumentAt(0, out var seedValue))
        {
            var seed = (int)mrb.AsInteger(seedValue);
            self.As<RData>().Data = new MRubyRandomData(seed);
        }
        else
        {
            self.As<RData>().Data = new MRubyRandomData();
        }

        return self;
    });

    public static MRubyMethod Rand = new((mrb, self) =>
    {
        var rand = InstanceOf(self).Rand;
        if (mrb.TryGetArgumentAt(0, out var maxValue))
        {
            var max = mrb.AsInteger(maxValue);
            return rand.Next(0, (int)max);
        }
        return rand.NextDouble();
    });

    public static MRubyMethod SRand = new((mrb, self) =>
    {
        var data =  InstanceOf(self);
        var currentSeed = data.Seed;
        if (mrb.TryGetArgumentAt(0, out var seedValue))
        {
            data.SetSeed((int)mrb.AsInteger(seedValue));
        }
        else
        {
            data.ResetSeed();
        }
        return currentSeed;
    });

    public static MRubyMethod Bytes = new((mrb, self) =>
    {
        var length = mrb.GetArgumentAsIntegerAt(0);
        if (length < 0)
        {
            mrb.Raise(Names.ArgumentError, "negative string size"u8);
        }

        var rand =  InstanceOf(self).Rand;
        var bytes = new byte[length];
        rand.NextBytes(bytes);
        return mrb.NewStringOwned(bytes);
    });

    public static MRubyMethod ArrayShuffle = new((mrb, self) =>
    {
        var array = self.As<RArray>();
        var result = array.Dup();
        ArrayShuffleBang.Invoke(mrb, result);
        return result;
    });

    public static MRubyMethod ArrayShuffleBang = new((mrb, self) =>
    {
        var array = self.As<RArray>();
        if (array.Length <= 1)
        {
            return self;
        }
        var rand =  DefaultInstanceOf(mrb).Rand;
        for (var i = array.Length - 1; i > 0; i--)
        {
            var j = rand.Next(0, i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
        return self;
    });

    public static MRubyMethod ArraySample = new((mrb, self) =>
    {
        var array = self.As<RArray>();
        var rand =  DefaultInstanceOf(mrb).Rand;
        var i = rand.Next(0, array.Length);
        return array[i];
    });

    static MRubyRandomData DefaultInstanceOf(MRubyState mrb)
    {
        var randomClass = mrb.GetClass(mrb.Intern("Random"u8));
        var defaultValue = mrb.GetInstanceVariable(randomClass, Names.Default);
        return InstanceOf(defaultValue);
    }

    static MRubyRandomData InstanceOf(MRubyValue value)
    {
        return (value.As<RData>().Data as MRubyRandomData)!;
    }
}