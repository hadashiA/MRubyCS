using System;
using System.IO;

namespace MRubyCS.StdLib;

static class IOMembers
{
    // State carried into the static map-lambda for IO#read so it doesn't
    // capture state-machine fields (no closure allocation per call).
    readonly record struct ReadCtx(MRubyState State, byte[] Buffer);

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
                scheduler.Await(
                    ReadAllAsync(stream),
                    fiber,
                    static (bytes, s) => new MRubyValue(s.NewString(bytes)),
                    state);
                return MRubyValue.Nil;
            }
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return state.NewString(ms.ToArray());
        }

        var n = (int)state.GetArgumentAsIntegerAt(0);
        if (n < 0) state.Raise(Names.ArgumentError, "negative length"u8);
        if (n == 0) return state.NewString([]);

        var buffer = new byte[n];

        if (scheduler is not null && !fiber.IsRoot)
        {
            scheduler.Await(
                stream.ReadAsync(buffer, 0, n).ToValueTask(),
                fiber,
                static (bytesRead, ctx) =>
                    bytesRead == 0
                        ? MRubyValue.Nil
                        : new MRubyValue(ctx.State.NewString(ctx.Buffer.AsSpan(0, bytesRead))),
                new ReadCtx(state, buffer));
            return MRubyValue.Nil;
        }

        var read = stream.Read(buffer, 0, n);
        return read == 0 ? MRubyValue.Nil : state.NewString(buffer.AsSpan(0, read));
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
            // Copy because the stream may outlive `bytes`'s lifetime once we
            // yield. Cheap relative to the syscall.
            var data = bytes.ToArray();
            scheduler.Await(
                stream.WriteAsync(data, 0, data.Length).ToValueTask(),
                fiber,
                new MRubyValue((long)data.Length));
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

    static async System.Threading.Tasks.ValueTask<byte[]> ReadAllAsync(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}

static class IOExtensions
{
    public static System.Threading.Tasks.ValueTask<int> ToValueTask(this System.Threading.Tasks.Task<int> task)
        => new(task);

    public static System.Threading.Tasks.ValueTask ToValueTask(this System.Threading.Tasks.Task task)
        => new(task);
}
