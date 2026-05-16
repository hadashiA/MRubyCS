using System;
using System.IO;

namespace MRubyCS.StdLib;

static class IOMembers
{
    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod Read = new((state, self) =>
    {
        var io = self.As<RIO>();
        EnsureOpen(state, io);
        var stream = io.Stream!;
        state.TryGetActiveFiberScheduler(out var scheduler);
        var hasArg = state.GetArgumentCount() > 0 && !state.GetArgumentAt(0).IsNil;

        if (!hasArg)
        {
            if (scheduler is not null)
            {
                scheduler.ReadStreamToEnd(stream);
                return MRubyValue.Nil;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return state.NewStringOwned(ms.ToArray());
        }

        var n = (int)state.GetArgumentAsIntegerAt(0);
        if (n < 0) state.Raise(Names.ArgumentError, "negative length"u8);
        if (n == 0) return state.NewString([]);

        if (scheduler is not null)
        {
            scheduler.ReadStream(stream, n);
            return MRubyValue.Nil;
        }

        var buffer = new byte[n];
        var read = stream.Read(buffer, 0, n);
        return read == 0
            ? MRubyValue.Nil
            : state.NewString(buffer.AsSpan(0, read));
    });

    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Write = new((state, self) =>
    {
        var io = self.As<RIO>();
        EnsureOpen(state, io);
        var stream = io.Stream!;

        var arg = state.GetArgumentAsStringAt(0);
        var bytes = arg.AsSpan();

        if (state.TryGetActiveFiberScheduler(out var scheduler))
        {
            // Copy because the source may outlive `bytes`'s lifetime once we
            // yield. Cheap relative to the syscall.
            var data = bytes.ToArray();
            scheduler.WriteStream(stream, data);
            return MRubyValue.Nil;
        }

        stream.Write(bytes);
        return new MRubyValue((long)bytes.Length);
    });

    [MRubyMethod]
    public static MRubyMethod Close = new((state, self) =>
    {
        self.As<RIO>().Close();
        return MRubyValue.Nil;
    });

    [MRubyMethod]
    public static MRubyMethod ClosedQ = new((state, self) =>
    {
        return self.As<RIO>().Closed ? MRubyValue.True : MRubyValue.False;
    });

    static void EnsureOpen(MRubyState state, RIO io)
    {
        if (io.Closed)
        {
            state.Raise(state.Intern("IOError"u8), "closed stream"u8);
        }
    }
}
