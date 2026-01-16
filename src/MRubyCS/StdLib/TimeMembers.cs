using System;
using Utf8StringInterpolation;

namespace MRubyCS.StdLib;

public enum MRubyTimeZone
{
    None,
    Utc,
    Local,
    Last,
}

/// <summary>
/// A mutable reference to DateTime that is encapsulated in RData and can be mutation from the outside.
/// </summary>
class MRubyTimeData(DateTimeOffset dateTimeOffset) :
    IEquatable<MRubyTimeData>,
    IComparable<MRubyTimeData>
{
    public DateTimeOffset DateTimeOffset { get; set; } = dateTimeOffset;

    public long Ticks
    {
        get => DateTimeOffset.Ticks;
        set => DateTimeOffset = new DateTimeOffset(value, dateTimeOffset.Offset);
    }

    public MRubyTimeZone TimeZone => dateTimeOffset.Offset.Ticks > 0
        ? MRubyTimeZone.Local
        : MRubyTimeZone.Utc;

    public bool Equals(MRubyTimeData? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return DateTimeOffset.Ticks == other.DateTimeOffset.Ticks; // ignore timezone
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((MRubyTimeData)obj);
    }

    public override int GetHashCode()
    {
        return DateTimeOffset.Ticks.GetHashCode();
    }

    public int CompareTo(MRubyTimeData? other)
    {
        if (ReferenceEquals(this, other)) return 0;
        if (other is null) return 1;
        return Ticks.CompareTo(other.Ticks);
    }
}

static class TimeMembers
{
    const long TicksPerMicrosecond = 10;

    public static MRubyMethod Now = new((mrb, _) =>
    {
        var data = new MRubyTimeData(DateTimeOffset.Now);
        return Wrap(mrb, data);
    });

    public static MRubyMethod CreateAt = new((mrb, _) =>
    {
        var secValue = mrb.GetArgumentAt(0);

        var ticks = ConvertToTicks(mrb, secValue, true);

        if (mrb.TryGetArgumentAt(1, out var usecValue))
        {
            ticks += ConvertToTicks(mrb, usecValue, false) / 10;
        }
        var data = new MRubyTimeData(new DateTime(ticks, DateTimeKind.Local));
        return Wrap(mrb, data);
    });

    public static MRubyMethod CreateUtc = new((mrb, _) =>
    {
        var year = (int)mrb.GetArgumentAsIntegerAt(0);
        var month = 1;
        var day = 1;
        var hour = 0;
        var minute = 0;
        var sec = 0;
        var usec = 0;

        if (mrb.TryGetArgumentAt(1, out var monthValue))
        {
            month = (int)mrb.ToInteger(monthValue);
        }
        if (mrb.TryGetArgumentAt(2, out var dayValue))
        {
            day = (int)mrb.ToInteger(dayValue);
        }
        if (mrb.TryGetArgumentAt(3, out var hourValue))
        {
            hour = (int)mrb.ToInteger(hourValue);
        }
        if (mrb.TryGetArgumentAt(4, out var minuteValue))
        {
            minute = (int)mrb.ToInteger(minuteValue);
        }

        if (mrb.TryGetArgumentAt(5, out var secValue))
        {
            sec = (int)mrb.ToInteger(secValue);
        }
        if (mrb.TryGetArgumentAt(6, out var usecValue))
        {
            usec = (int)mrb.ToInteger(usecValue);
        }
        var dateTime = new DateTime(year, month, day, hour, minute, sec, DateTimeKind.Utc);
        dateTime = dateTime.AddTicks(usec * TicksPerMicrosecond);
        var data =  new MRubyTimeData(dateTime);
        return Wrap(mrb, data);
    });

    public static MRubyMethod CreateLocal = new((mrb, _) =>
    {
        var year = (int)mrb.GetArgumentAsIntegerAt(0);
        var month = 1;
        var day = 1;
        var hour = 0;
        var minute = 0;
        var sec = 0;
        var usec = 0;

        if (mrb.TryGetArgumentAt(1, out var monthValue))
        {
            month = (int)mrb.ToInteger(monthValue);
        }
        if (mrb.TryGetArgumentAt(2, out var dayValue))
        {
            day = (int)mrb.ToInteger(dayValue);
        }
        if (mrb.TryGetArgumentAt(3, out var hourValue))
        {
            hour = (int)mrb.ToInteger(hourValue);
        }
        if (mrb.TryGetArgumentAt(4, out var minuteValue))
        {
            minute = (int)mrb.ToInteger(minuteValue);
        }

        if (mrb.TryGetArgumentAt(5, out var secValue))
        {
            sec = (int)mrb.ToInteger(secValue);
        }
        if (mrb.TryGetArgumentAt(6, out var usecValue))
        {
            usec = (int)mrb.ToInteger(usecValue);
        }
        var dateTime = new DateTime(year, month, day, hour, minute, sec, DateTimeKind.Local);
        dateTime = dateTime.AddTicks(usec * TicksPerMicrosecond);
        var data =  new MRubyTimeData(dateTime);
        return Wrap(mrb, data);
    });

    public static MRubyMethod Initialize = new((mrb, self) =>
    {
        DateTimeOffset dateTimeOffset;
        if (mrb.GetArgumentCount() <= 0)
        {
            dateTimeOffset = DateTimeOffset.Now;
        }
        else
        {
            var year = 0;
            var month = 1;
            var day = 1;
            var hour = 0;
            var minute = 0;
            var sec = 0;
            var usec = 0;

            if (mrb.TryGetArgumentAt(1, out var yearValue))
            {
                year = (int)mrb.ToInteger(yearValue);
            }
            if (mrb.TryGetArgumentAt(2, out var monthValue))
            {
                month = (int)mrb.ToInteger(monthValue);
            }
            if (mrb.TryGetArgumentAt(3, out var dayValue))
            {
                day = (int)mrb.ToInteger(dayValue);
            }
            if (mrb.TryGetArgumentAt(4, out var hourValue))
            {
                hour = (int)mrb.ToInteger(hourValue);
            }
            if (mrb.TryGetArgumentAt(5, out var minuteValue))
            {
                minute = (int)mrb.ToInteger(minuteValue);
            }
            if (mrb.TryGetArgumentAt(6, out var secValue))
            {
                sec = (int)mrb.ToInteger(secValue);
            }
            if (mrb.TryGetArgumentAt(7, out var usecValue))
            {
                usec = (int)mrb.ToInteger(usecValue);
            }

            var dateTime = new DateTime(year, month, day, hour, minute, sec, DateTimeKind.Local);
            dateTime = dateTime.AddTicks(usec * TicksPerMicrosecond);
            dateTimeOffset = new DateTimeOffset(dateTime);
        }
        self.As<RData>().Data = Wrap(mrb, new MRubyTimeData(dateTimeOffset));
        return self;
    });

    public static MRubyMethod InitializeCopy = new((mrb, self) =>
    {
        var copyValue = mrb.GetArgumentAt(0);
        if (mrb.ValueEquals(copyValue, self)) return copyValue;

        if (!mrb.InstanceOf(copyValue, mrb.ClassOf(self)))
        {
            mrb.Raise(Names.TypeError, "wrong argument class"u8);
        }

        var src = GetTimeData(mrb, self);

        DateTimeOffset dateTimeOffset;
        if (copyValue.As<RData>().Data is MRubyTimeData copy)
        {
            dateTimeOffset = copy.DateTimeOffset;
        }
        else
        {
            dateTimeOffset = DateTimeOffset.Now;
        }
        src.DateTimeOffset = dateTimeOffset;
        return copyValue;
    });

    public static MRubyMethod Hash = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).Ticks.GetHashCode();
    });

    public static MRubyMethod OpEq = new((mrb, self) =>
    {
        var a = GetTimeData(mrb, self);
        var b = GetTimeData(mrb, mrb.GetArgumentAt(0));
        return a.Equals(b);
    });

    public static MRubyMethod OpCmp = new((mrb, self) =>
    {
        var a = GetTimeData(mrb, self);
        var b = GetTimeData(mrb, mrb.GetArgumentAt(0));
        return a.CompareTo(b);
    });

    public static MRubyMethod OpAdd = new((mrb, self) =>
    {
        var time = GetTimeData(mrb, self);
        var ticksAdd = ConvertToTicks(mrb, mrb.GetArgumentAt(0), true);

        long ticks = 0;
        try
        {
            checked
            {
                ticks = time.Ticks + ticksAdd;
            }
        }
        catch (OverflowException)
        {
            mrb.Raise(Names.RangeError, $"Time out of range in addition");
        }

        var result = new DateTimeOffset(ticks, time.DateTimeOffset.Offset);
        return Wrap(mrb, new MRubyTimeData(result));
    });

    public static MRubyMethod OpSub = new((mrb, self) =>
    {
        var time = GetTimeData(mrb, self);

        var arg0 = mrb.GetArgumentAt(0);
        if (arg0.Object is RData { Data: MRubyTimeData other })
        {
            var diff = time.DateTimeOffset - other.DateTimeOffset;
            return diff.Ticks / TimeSpan.TicksPerSecond;
        }

        var ticks = ConvertToTicks(mrb, arg0, true);
        try
        {
            checked
            {
                time.Ticks -= ticks;
            }
        }
        catch (OverflowException)
        {
            mrb.Raise(Names.RangeError, $"Time out of range in subtraction");
        }
        var result = new DateTimeOffset(ticks, time.DateTimeOffset.Offset);
        return new RData(new MRubyTimeData(result));
    });

    public static MRubyMethod Asctime = new((mrb, self) =>
    {
        var d = GetTimeData(mrb, self).DateTimeOffset;
        return mrb.NewString($"{d:ddd} {d:MMM} {d.Day,2} {d:HH}:{d:mm}:{d:ss} {d:yyyy}");
    });

    public static MRubyMethod ToS = new((mrb, self) =>
    {
        var data = GetTimeData(mrb, self);
        var t = data.DateTimeOffset;
        if (t.Offset == TimeSpan.Zero)
        {
            // utc
            return mrb.NewString($"{t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}:{t.Minute:00}:{t.Second:00} UTC");
        }
        // local
        return mrb.NewString($"{t.Year:0000}-{t.Month:00}-{t.Day:00} {t.Hour:00}:{t.Minute:00}:{t.Second:00} +{t.Offset.Hours:00}:00");
    });

    public static MRubyMethod ToF = new((mrb, self) =>
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return (dateTimeOffset - DateTimeOffset.UnixEpoch).TotalSeconds;
    });

    public static MRubyMethod ToI = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.ToUnixTimeSeconds();
    });

    public static MRubyMethod UtcOffset = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.Offset.TotalSeconds;
    });

    public static MRubyMethod Year = new((mrb, self) =>
        GetTimeData(mrb, self).DateTimeOffset.Year);

    public static MRubyMethod Month = new((mrb, self) =>
        GetTimeData(mrb, self).DateTimeOffset.Month);

    public static MRubyMethod Day = new((mrb, self) =>
        GetTimeData(mrb, self).DateTimeOffset.Day);

    public static MRubyMethod Hour = new((mrb, self) =>
        GetTimeData(mrb, self).DateTimeOffset.Hour);

    public static MRubyMethod Minute = new((mrb, self) =>
        GetTimeData(mrb, self).DateTimeOffset.Minute);

    public static MRubyMethod Second = new((mrb, self) =>
        GetTimeData(mrb, self).DateTimeOffset.Second);

    public static MRubyMethod MicroSecond = new((mrb, self) =>
        (GetTimeData(mrb, self).DateTimeOffset.Ticks / TicksPerMicrosecond) % 10000);

    public static MRubyMethod NanoSecond = new((mrb, self) =>
        (GetTimeData(mrb, self).DateTimeOffset.Ticks / TicksPerMicrosecond) * 100);

    public static MRubyMethod Wday = new((mrb, self) =>
        (int)GetTimeData(mrb, self).DateTimeOffset.DayOfWeek);

    public static MRubyMethod Yday = new((mrb, self) =>
        GetTimeData(mrb, self).DateTimeOffset.DayOfYear);

    public static MRubyMethod Zone = new((mrb, self) =>
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        if (dateTimeOffset.Offset == TimeSpan.Zero)
        {
            return mrb.NewString("UTC"u8);
        }

        Span<byte> result = stackalloc byte[5];

        var format = Utf8String.Format($"{dateTimeOffset:zzz}");
        format[0..3].CopyTo(result);
        format[4..6].CopyTo(result[3..]);
        return mrb.NewString(result);
    });

    public static MRubyMethod QUtc = new((mrb, self) =>
    {
        var dateTimeOffset =  GetTimeData(mrb, self).DateTimeOffset;
        return dateTimeOffset.Offset == TimeSpan.Zero;
    });

    public static MRubyMethod QSunday = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Sunday;
    });

    public static MRubyMethod QMonday = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Monday;
    });

    public static MRubyMethod QTuesday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Tuesday;
    });

    public static MRubyMethod QWednesday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Wednesday;
    });

    public static MRubyMethod QThursday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Thursday;
    });

    public static MRubyMethod QFriday  = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Friday;
    });

    public static MRubyMethod QSaturday = new((mrb, self) =>
    {
        return GetTimeData(mrb, self).DateTimeOffset.DayOfWeek == DayOfWeek.Saturday;
    });

    public static MRubyMethod QDaylightSavintTime = new((mrb, self) =>
    {
        var dateTimeOffset = GetTimeData(mrb, self).DateTimeOffset;
        return TimeZoneInfo.Local.IsDaylightSavingTime(dateTimeOffset);
    });

    public static MRubyMethod GetUtc = new((mrb, self) =>
    {
        var t = GetTimeData(mrb, self);
        var utc = new MRubyTimeData(t.DateTimeOffset.ToUniversalTime());
        return new RData(utc);
    });

    public static MRubyMethod GetLocal = new((mrb, self) =>
    {
        var t = GetTimeData(mrb, self);
        var utc = new MRubyTimeData(t.DateTimeOffset.ToLocalTime());
        return new RData(utc);
    });

    public static MRubyMethod ConvertToUtc = new((mrb, self) =>
    {
        var t = GetTimeData(mrb, self);
        t.DateTimeOffset = t.DateTimeOffset.ToUniversalTime();
        return self;
    });

    public static MRubyMethod ConvertToLocal = new((mrb, self) =>
    {
        var t = GetTimeData(mrb, self);
        t.DateTimeOffset = t.DateTimeOffset.ToLocalTime();
        return self;
    });

    static MRubyTimeData GetTimeData(MRubyState mrb, MRubyValue value)
    {
        if (value.As<RData>().Data is MRubyTimeData timeData)
        {
            return timeData;
        }

        mrb.Raise(Names.ArgumentError, "uninitialized Time"u8);
        return default!; // unreachable
    }

    static RData Wrap(MRubyState mrb, MRubyTimeData timeData)
    {
        var timeClass = mrb.GetConst(mrb.Intern("Time"u8), mrb.ObjectClass).As<RClass>();
        return new RData(timeClass, timeData);
    }

    static long ConvertToTicks(MRubyState mrb, MRubyValue secValue, bool withUSecs)
    {
        if (secValue.IsFloat)
        {
            var sec = secValue.FloatValue;
            mrb.EnsureExactValue(sec);

            if (sec is >= long.MaxValue - 1.0 or < long.MinValue + 1.0)
            {
                mrb.Raise(Names.ArgumentError, $"{sec} out of Time range");
            }
            if (withUSecs)
            {
                var secFloored = Math.Floor(sec);
                var ticks = (long)secFloored * TimeSpan.TicksPerSecond;
                ticks += (long)Math.Truncate((sec - secFloored) * TicksPerMicrosecond);
                return ticks;
            }
            return (long)Math.Round(sec) * TimeSpan.TicksPerSecond;
        }
        if (secValue.IsInteger)
        {
            return secValue.IntegerValue * TimeSpan.TicksPerSecond;
        }

        mrb.Raise(Names.TypeError, $"cannot convert {mrb.Stringify(secValue)} to time");
        return default; // unreachable
    }
}
