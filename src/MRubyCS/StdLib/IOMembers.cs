using System;
using System.Buffers;
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

        var fiber = state.CurrentFiber;
        var scheduler = state.FiberScheduler;
        var hasArg = state.GetArgumentCount() > 0 && !state.GetArgumentAt(0).IsNil;

        if (!hasArg)
        {
            // Read-to-EOF path.
            if (scheduler is not null && !fiber.IsRoot)
            {
                var writer = new ArrayBufferWriter<byte>();
                scheduler.ReadStreamToEnd(
                    stream,
                    writer,
                    (State: state, Writer: writer),
                    static (_, ctx) => new MRubyValue(ctx.State.NewString(ctx.Writer.WrittenSpan)),
                    fiber);
                return MRubyValue.Nil;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return state.NewStringOwned(ms.ToArray());
        }

        var n = (int)state.GetArgumentAsIntegerAt(0);
        if (n < 0) state.Raise(Names.ArgumentError, "negative length"u8);
        if (n == 0) return state.NewString([]);

        var buffer = new byte[n];

        if (scheduler is not null && !fiber.IsRoot)
        {
            scheduler.ReadStream(
                stream,
                buffer,
                (State: state, Buffer: buffer),
                static (bytesRead, ctx) => bytesRead == 0
                    ? MRubyValue.Nil
                    : new MRubyValue(ctx.State.NewString(ctx.Buffer.AsSpan(0, bytesRead))),
                fiber);
            return MRubyValue.Nil;
        }

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

        var fiber = state.CurrentFiber;
        var scheduler = state.FiberScheduler;

        if (scheduler is not null && !fiber.IsRoot)
        {
            // Copy because the source may outlive `bytes`'s lifetime once we
            // yield. Cheap relative to the syscall.
            var data = bytes.ToArray();
            scheduler.WriteStream(stream, data, fiber);
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
