using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MRubyCS.StdLib;

class MRubyRegexpData(string pattern, int rubyOptions = 0) : IEquatable<MRubyRegexpData>
{
    public const int RubyIgnoreCase = 1;
    public const int RubyExtended = 2;
    public const int RubyMultiline = 4;

    public Regex Regex { get; } = new(pattern, ConvertToRegexOptions(rubyOptions));
    public string Pattern => pattern;
    public int RubyOptions => rubyOptions;

    public bool Equals(MRubyRegexpData? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Pattern == other.Pattern && RubyOptions == other.RubyOptions;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((MRubyRegexpData)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Pattern, RubyOptions);
    }

    /// <summary>
    /// Converts Ruby options to .NET RegexOptions.
    /// Ruby: IGNORECASE=1, EXTENDED=2, MULTILINE=4 (dot matches newline)
    /// .NET: Multiline means ^/$ match line boundaries (Ruby's default)
    ///       Singleline means . matches newline (Ruby's MULTILINE)
    /// </summary>
    static RegexOptions ConvertToRegexOptions(int rubyOptions)
    {
        // Always enable Multiline so ^/$ match at line boundaries (Ruby default)
        var options = RegexOptions.Multiline;

        if ((rubyOptions & RubyIgnoreCase) != 0)
        {
            options |= RegexOptions.IgnoreCase;
        }
        if ((rubyOptions & RubyExtended) != 0)
        {
            options |= RegexOptions.IgnorePatternWhitespace;
        }
        if ((rubyOptions & RubyMultiline) != 0)
        {
            // Ruby's MULTILINE = .NET's Singleline (dot matches newline)
            options |= RegexOptions.Singleline;
        }

        return options;
    }
}

static class RegexpMembers
{
    public static RData CreateRDataFromRegexp(MRubyState mrb, MRubyRegexpData regexpData)
    {
        var regexpClass = mrb.RegexpClass;
        return new RData(regexpClass, regexpData);
    }

    public static bool TryGetRegexpData(MRubyValue value, out MRubyRegexpData data)
    {
        if (value.Object is RData { Data: MRubyRegexpData regexpData })
        {
            data = regexpData;
            return true;
        }
        data = default!;
        return false;
    }

    public static MRubyRegexpData GetRegexpData(MRubyState mrb, MRubyValue value)
    {
        if (TryGetRegexpData(value, out var data))
        {
            return data;
        }
        mrb.Raise(Names.TypeError, "expected Regexp"u8);
        return default!; // unreachable
    }

    /// <summary>
    /// Updates regex global variables ($~, $&amp;, $`, $', $+, $1-$9) after a match.
    /// </summary>
    public static void UpdateRegexpGlobalVariables(MRubyState mrb, MRubyMatchData? matchData)
    {
        if (matchData == null)
        {
            // Clear all global variables
            mrb.SetGlobalVariable(Names.GvMatch, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.GvMatchedString, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.GvPreMatch, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.GvPostMatch, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.GvLastCapture, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv1, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv2, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv3, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv4, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv5, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv6, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv7, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv8, MRubyValue.Nil);
            mrb.SetGlobalVariable(Names.Gv9, MRubyValue.Nil);
            return;
        }

        var match = matchData.Match;
        var input = matchData.OriginalString;

        // $~ = MatchData object
        var matchDataRData = MatchDataMembers.CreateRDataFromMatchData(mrb, matchData);
        mrb.SetGlobalVariable(Names.GvMatch, matchDataRData);

        // $& = matched string
        mrb.SetGlobalVariable(Names.GvMatchedString, mrb.NewString(match.Value));

        // $` = pre_match
        mrb.SetGlobalVariable(Names.GvPreMatch, mrb.NewString(input.Substring(0, match.Index)));

        // $' = post_match
        mrb.SetGlobalVariable(Names.GvPostMatch, mrb.NewString(input.Substring(match.Index + match.Length)));

        // $+ = last successful capture (last non-empty group)
        MRubyValue lastCapture = MRubyValue.Nil;
        for (var i = match.Groups.Count - 1; i >= 1; i--)
        {
            var g = match.Groups[i];
            if (g.Success)
            {
                lastCapture = mrb.NewString(g.Value);
                break;
            }
        }
        mrb.SetGlobalVariable(Names.GvLastCapture, lastCapture);

        // $1-$9 capture groups
        Symbol[] gvSymbols = [Names.Gv1, Names.Gv2, Names.Gv3, Names.Gv4, Names.Gv5, Names.Gv6, Names.Gv7, Names.Gv8, Names.Gv9];
        for (var i = 0; i < 9; i++)
        {
            var groupIndex = i + 1;
            if (groupIndex < match.Groups.Count && match.Groups[groupIndex].Success)
            {
                mrb.SetGlobalVariable(gvSymbols[i], mrb.NewString(match.Groups[groupIndex].Value));
            }
            else
            {
                mrb.SetGlobalVariable(gvSymbols[i], MRubyValue.Nil);
            }
        }
    }

    // Regexp.new(pattern, options = 0)
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod New = new((mrb, self) =>
    {
        var patternValue = mrb.GetArgumentAt(0);
        string pattern;

        if (TryGetRegexpData(patternValue, out var existingRegexp))
        {
            // If first arg is a Regexp, return a copy (ignore second arg)
            return CreateRDataFromRegexp(mrb, new MRubyRegexpData(existingRegexp.Pattern, existingRegexp.RubyOptions));
        }

        if (patternValue.Object is RString patternStr)
        {
            pattern = patternStr.ConvertToString();
        }
        else
        {
            mrb.Raise(Names.TypeError, "no implicit conversion into String"u8);
            return MRubyValue.Nil;
        }

        var rubyOptions = 0;
        if (mrb.TryGetArgumentAt(1, out var optionsValue))
        {
            if (optionsValue.IsInteger)
            {
                rubyOptions = (int)optionsValue.IntegerValue;
            }
            else if (optionsValue.Truthy)
            {
                rubyOptions = MRubyRegexpData.RubyIgnoreCase;
            }
        }

        try
        {
            var regexpData = new MRubyRegexpData(pattern, rubyOptions);
            return CreateRDataFromRegexp(mrb, regexpData);
        }
        catch (ArgumentException ex)
        {
            mrb.Raise(Names.RegexpError, $"{ex.Message}");
            return MRubyValue.Nil;
        }
    });

    // Regexp.compile(pattern, options = 0) - alias for new
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod Compile = new((mrb, self) =>
    {
        return New.Invoke(mrb, self);
    });

    // Regexp.escape(str) - Escapes metacharacters
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Escape = new((mrb, self) =>
    {
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ConvertToString();
        var escaped = EscapeForRegexp(input);
        return mrb.NewString(escaped);
    });

    static string EscapeForRegexp(string input)
    {
        var sb = new StringBuilder(input.Length * 2);
        foreach (var c in input)
        {
            switch (c)
            {
                case '.':
                case '*':
                case '+':
                case '?':
                case '^':
                case '$':
                case '{':
                case '}':
                case '[':
                case ']':
                case '(':
                case ')':
                case '|':
                case '\\':
                    sb.Append('\\');
                    sb.Append(c);
                    break;
                case ' ':
                    sb.Append("\\ ");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    // Regexp.quote(str) - alias for escape
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Quote = new((mrb, self) =>
    {
        return Escape.Invoke(mrb, self);
    });

    // Regexp.union(*patterns)
    [MRubyMethod]
    public static MRubyMethod Union = new((mrb, self) =>
    {
        var argc = mrb.GetArgumentCount();
        var patterns = new List<string>();

        // Handle single array argument
        if (argc == 1)
        {
            var arg = mrb.GetArgumentAt(0);
            if (arg.Object is RArray array)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    patterns.Add(ExtractPattern(mrb, array[i]));
                }
            }
            else
            {
                patterns.Add(ExtractPattern(mrb, arg));
            }
        }
        else
        {
            for (var i = 0; i < argc; i++)
            {
                patterns.Add(ExtractPattern(mrb, mrb.GetArgumentAt(i)));
            }
        }

        if (patterns.Count == 0)
        {
            return CreateRDataFromRegexp(mrb, new MRubyRegexpData("(?!)"));
        }

        var unionPattern = string.Join("|", patterns);
        try
        {
            return CreateRDataFromRegexp(mrb, new MRubyRegexpData(unionPattern));
        }
        catch (ArgumentException ex)
        {
            mrb.Raise(Names.RegexpError, $"{ex.Message}");
            return MRubyValue.Nil;
        }
    });

    static string ExtractPattern(MRubyState mrb, MRubyValue value)
    {
        if (TryGetRegexpData(value, out var regexpData))
        {
            // Include inline modifiers to preserve options
            var modifiers = "";
            if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0)
            {
                modifiers += "i";
            }
            if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) != 0)
            {
                modifiers += "x";
            }
            if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) != 0)
            {
                modifiers += "s"; // .NET's singleline = Ruby's multiline
            }

            if (modifiers.Length > 0)
            {
                return $"(?{modifiers}:{regexpData.Pattern})";
            }
            return $"(?:{regexpData.Pattern})";
        }
        if (value.Object is RString str)
        {
            return EscapeForRegexp(str.ConvertToString());
        }
        mrb.Raise(Names.TypeError, "no implicit conversion into String"u8);
        return "";
    }

    // Regexp.try_convert(obj)
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod TryConvert = new((mrb, self) =>
    {
        var arg = mrb.GetArgumentAt(0);
        if (TryGetRegexpData(arg, out _))
        {
            return arg;
        }
        return MRubyValue.Nil;
    });

    // Regexp.last_match(n = nil)
    [MRubyMethod(OptionalArguments = 1)]
    public static MRubyMethod LastMatch = new((mrb, self) =>
    {
        var matchValue = mrb.GetGlobalVariable(Names.GvMatch);
        if (matchValue.IsNil)
        {
            return MRubyValue.Nil;
        }

        if (!mrb.TryGetArgumentAt(0, out var indexArg))
        {
            return matchValue;
        }

        // Regexp.last_match(n) returns the nth capture
        var n = (int)mrb.AsInteger(indexArg);
        return MatchDataMembers.OpAref.Invoke(mrb, matchValue);
    });

    // Regexp#match(str, pos = 0)
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod Match = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ConvertToString();

        var pos = 0;
        if (mrb.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)mrb.AsInteger(posValue);
        }

        // Convert character position to actual position in string
        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            UpdateRegexpGlobalVariables(mrb, null);
            return MRubyValue.Nil;
        }

        var match = regexpData.Regex.Match(input, pos);
        if (!match.Success)
        {
            UpdateRegexpGlobalVariables(mrb, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        UpdateRegexpGlobalVariables(mrb, matchData);
        return MatchDataMembers.CreateRDataFromMatchData(mrb, matchData);
    });

    // Regexp#match?(str, pos = 0) - boolean match without setting global variables
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod QMatch = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ConvertToString();

        var pos = 0;
        if (mrb.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)mrb.AsInteger(posValue);
        }

        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            return MRubyValue.False;
        }

        var match = regexpData.Regex.Match(input, pos);
        return match.Success ? MRubyValue.True : MRubyValue.False;
    });

    // Regexp#=~(str)
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpMatch = new((mrb, self) =>
    {
        var arg = mrb.GetArgumentAt(0);
        if (arg.IsNil)
        {
            return MRubyValue.Nil;
        }

        var regexpData = GetRegexpData(mrb, self);
        var str = mrb.GetArgumentAsStringAt(0);
        var input = str.ConvertToString();

        var match = regexpData.Regex.Match(input);
        if (!match.Success)
        {
            UpdateRegexpGlobalVariables(mrb, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        UpdateRegexpGlobalVariables(mrb, matchData);

        // Return character index (not byte index)
        return match.Index;
    });

    // Regexp#===(str)
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod Eqq = new((mrb, self) =>
    {
        var arg = mrb.GetArgumentAt(0);
        if (arg.IsNil)
        {
            return MRubyValue.False;
        }

        RString str;
        if (arg.Object is RString s)
        {
            str = s;
        }
        else
        {
            // Try to convert to string
            var converted = mrb.Send(arg, Names.ToS);
            if (converted.Object is not RString convertedStr)
            {
                return MRubyValue.False;
            }
            str = convertedStr;
        }

        var regexpData = GetRegexpData(mrb, self);
        var input = str.ConvertToString();
        var match = regexpData.Regex.Match(input);

        if (match.Success)
        {
            var matchData = new MRubyMatchData(match, regexpData, input);
            UpdateRegexpGlobalVariables(mrb, matchData);
            return MRubyValue.True;
        }

        UpdateRegexpGlobalVariables(mrb, null);
        return MRubyValue.False;
    });

    // Regexp#source
    [MRubyMethod]
    public static MRubyMethod Source = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        return mrb.NewString(regexpData.Pattern);
    });

    // Regexp#options
    [MRubyMethod]
    public static MRubyMethod Options = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        return regexpData.RubyOptions;
    });

    // Regexp#casefold?
    [MRubyMethod]
    public static MRubyMethod QCasefold = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        return (regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0;
    });

    // Regexp#to_s
    [MRubyMethod]
    public static MRubyMethod ToS = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        var sb = new StringBuilder();
        sb.Append("(?");

        // Add option flags
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) != 0)
        {
            sb.Append('m');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0)
        {
            sb.Append('i');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) != 0)
        {
            sb.Append('x');
        }

        // Add disabled options
        sb.Append('-');
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) == 0)
        {
            sb.Append('m');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) == 0)
        {
            sb.Append('i');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) == 0)
        {
            sb.Append('x');
        }

        sb.Append(':');
        sb.Append(regexpData.Pattern);
        sb.Append(')');
        return mrb.NewString(sb.ToString());
    });

    // Regexp#inspect
    [MRubyMethod]
    public static MRubyMethod Inspect = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        var sb = new StringBuilder();
        sb.Append('/');
        sb.Append(regexpData.Pattern);
        sb.Append('/');

        if ((regexpData.RubyOptions & MRubyRegexpData.RubyIgnoreCase) != 0)
        {
            sb.Append('i');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyMultiline) != 0)
        {
            sb.Append('m');
        }
        if ((regexpData.RubyOptions & MRubyRegexpData.RubyExtended) != 0)
        {
            sb.Append('x');
        }
        return mrb.NewString(sb.ToString());
    });

    // Regexp#==
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpEq = new((mrb, self) =>
    {
        var other = mrb.GetArgumentAt(0);
        if (!TryGetRegexpData(other, out var otherData))
        {
            return MRubyValue.False;
        }
        var selfData = GetRegexpData(mrb, self);
        return selfData.Equals(otherData);
    });

    // Regexp#eql?
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod QEql = new((mrb, self) =>
    {
        return OpEq.Invoke(mrb, self);
    });

    // Regexp#hash
    [MRubyMethod]
    public static MRubyMethod Hash = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        return regexpData.GetHashCode();
    });

    // Regexp#named_captures
    [MRubyMethod]
    public static MRubyMethod NamedCaptures = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        var hash = mrb.NewHash(0);

        var groupNames = regexpData.Regex.GetGroupNames();
        foreach (var name in groupNames)
        {
            // Skip numeric group names
            if (int.TryParse(name, out _)) continue;

            var groupNumber = regexpData.Regex.GroupNumberFromName(name);
            var indices = mrb.NewArray(1);
            indices.Push(groupNumber);
            hash[mrb.NewString(name)] = indices;
        }

        return hash;
    });

    // Regexp#names
    [MRubyMethod]
    public static MRubyMethod NamesMethod = new((mrb, self) =>
    {
        var regexpData = GetRegexpData(mrb, self);
        var groupNames = regexpData.Regex.GetGroupNames();

        var names = new List<string>();
        foreach (var name in groupNames)
        {
            // Skip numeric group names
            if (!int.TryParse(name, out _))
            {
                names.Add(name);
            }
        }

        var array = mrb.NewArray(names.Count);
        foreach (var name in names)
        {
            array.Push(mrb.NewString(name));
        }
        return array;
    });
}

/// <summary>
/// Regexp-related methods for String class.
/// </summary>
static class StringRegexpMembers
{
    // String#=~ (regexp)
    [MRubyMethod(RequiredArguments = 1)]
    public static MRubyMethod OpMatch = new((state, self) =>
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        if (arg.IsNil)
        {
            return MRubyValue.Nil;
        }

        if (!RegexpMembers.TryGetRegexpData(arg, out var regexpData))
        {
            // Try calling =~ on the other object
            return state.Send(arg, state.Intern("=~"u8), self);
        }

        var input = str.ConvertToString();
        var match = regexpData.Regex.Match(input);

        if (!match.Success)
        {
            RegexpMembers.UpdateRegexpGlobalVariables(state, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
        return match.Index;
    });

    // String#match(regexp, pos = 0)
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod Match = new((state, self) =>
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        MRubyRegexpData regexpData;
        if (RegexpMembers.TryGetRegexpData(arg, out var data))
        {
            regexpData = data;
        }
        else if (arg.Object is RString patternStr)
        {
            try
            {
                regexpData = new MRubyRegexpData(patternStr.ConvertToString());
            }
            catch (ArgumentException ex)
            {
                state.Raise(Names.RegexpError, $"{ex.Message}");
                return MRubyValue.Nil;
            }
        }
        else
        {
            state.Raise(Names.TypeError, "wrong argument type"u8);
            return MRubyValue.Nil;
        }

        var input = str.ConvertToString();
        var pos = 0;
        if (state.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)state.AsInteger(posValue);
        }

        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            RegexpMembers.UpdateRegexpGlobalVariables(state, null);
            return MRubyValue.Nil;
        }

        var match = regexpData.Regex.Match(input, pos);
        if (!match.Success)
        {
            RegexpMembers.UpdateRegexpGlobalVariables(state, null);
            return MRubyValue.Nil;
        }

        var matchData = new MRubyMatchData(match, regexpData, input);
        RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
        return MatchDataMembers.CreateRDataFromMatchData(state, matchData);
    });

    // String#match?(regexp, pos = 0)
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod QMatch = new((state, self) =>
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        MRubyRegexpData regexpData;
        if (RegexpMembers.TryGetRegexpData(arg, out var data))
        {
            regexpData = data;
        }
        else if (arg.Object is RString patternStr)
        {
            try
            {
                regexpData = new MRubyRegexpData(patternStr.ConvertToString());
            }
            catch (ArgumentException ex)
            {
                state.Raise(Names.RegexpError, $"{ex.Message}");
                return MRubyValue.False;
            }
        }
        else
        {
            state.Raise(Names.TypeError, "wrong argument type"u8);
            return MRubyValue.False;
        }

        var input = str.ConvertToString();
        var pos = 0;
        if (state.TryGetArgumentAt(1, out var posValue))
        {
            pos = (int)state.AsInteger(posValue);
        }

        if (pos < 0)
        {
            pos = input.Length + pos;
        }
        if (pos < 0 || pos > input.Length)
        {
            return MRubyValue.False;
        }

        var match = regexpData.Regex.Match(input, pos);
        return match.Success ? MRubyValue.True : MRubyValue.False;
    });

    // String#sub(pattern, replacement) or String#sub(pattern) { |match| block }
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod Sub = new((state, self) =>
    {
        var str = self.As<RString>();
        return SubImpl(state, str, false);
    });

    // String#sub!(pattern, replacement) or String#sub!(pattern) { |match| block }
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod SubBang = new((state, self) =>
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);
        return SubImpl(state, str, true);
    });

    static MRubyValue SubImpl(MRubyState state, RString str, bool inPlace)
    {
        var patternArg = state.GetArgumentAt(0);
        var block = state.GetBlockArgument();
        var input = str.ConvertToString();

        // Handle Regexp pattern
        if (RegexpMembers.TryGetRegexpData(patternArg, out var regexpData))
        {
            var match = regexpData.Regex.Match(input);
            if (!match.Success)
            {
                RegexpMembers.UpdateRegexpGlobalVariables(state, null);
                return inPlace ? MRubyValue.Nil : str.Dup();
            }

            var matchData = new MRubyMatchData(match, regexpData, input);
            RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);

            string replacement;
            if (block != null)
            {
                var matchStr = state.NewString(match.Value);
                var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                replacement = state.Stringify(blockResult).ConvertToString();
            }
            else
            {
                var replacementArg = state.GetArgumentAsStringAt(1);
                replacement = ProcessReplacementString(replacementArg.ConvertToString(), match, input);
            }

            var result = input.Substring(0, match.Index) + replacement + input.Substring(match.Index + match.Length);

            if (inPlace)
            {
                var newBytes = Encoding.UTF8.GetBytes(result);
                str.MakeModifiable(newBytes.Length, true);
                newBytes.CopyTo(str.AsSpan());
                return str;
            }
            return state.NewString(result);
        }

        // Handle String pattern
        if (patternArg.Object is RString patternStr)
        {
            var pattern = patternStr.ConvertToString();
            var index = input.IndexOf(pattern, StringComparison.Ordinal);

            if (index < 0)
            {
                return inPlace ? MRubyValue.Nil : str.Dup();
            }

            string replacement;
            if (block != null)
            {
                var matchStr = state.NewString(pattern);
                var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                replacement = state.Stringify(blockResult).ConvertToString();
            }
            else
            {
                var replacementArg = state.GetArgumentAsStringAt(1);
                // Process replacement string for \0, \&, etc. but without capture groups
                replacement = ProcessSimpleReplacementString(replacementArg.ConvertToString(), pattern, input, index);
            }

            var result = input.Substring(0, index) + replacement + input.Substring(index + pattern.Length);

            if (inPlace)
            {
                var newBytes = Encoding.UTF8.GetBytes(result);
                str.MakeModifiable(newBytes.Length, true);
                newBytes.CopyTo(str.AsSpan());
                return str;
            }
            return state.NewString(result);
        }

        state.Raise(Names.TypeError, "wrong argument type"u8);
        return MRubyValue.Nil;
    }

    // String#gsub(pattern, replacement) or String#gsub(pattern) { |match| block } or String#gsub(pattern, hash)
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod Gsub = new((state, self) =>
    {
        var str = self.As<RString>();
        return GsubImpl(state, str, false);
    });

    // String#gsub!(pattern, replacement) or String#gsub!(pattern) { |match| block } or String#gsub!(pattern, hash)
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod GsubBang = new((state, self) =>
    {
        var str = self.As<RString>();
        state.EnsureNotFrozen(str);
        return GsubImpl(state, str, true);
    });

    static MRubyValue GsubImpl(MRubyState state, RString str, bool inPlace)
    {
        var argc = state.GetArgumentCount();
        if (argc == 0)
        {
            state.RaiseArgumentNumberError(argc, 1, 2);
            return MRubyValue.Nil;
        }
        if (argc > 2)
        {
            state.RaiseArgumentNumberError(argc, 1, 2);
            return MRubyValue.Nil;
        }

        var patternArg = state.GetArgumentAt(0);
        var block = state.GetBlockArgument();
        var input = str.ConvertToString();

        // Check for hash argument
        RHash? hashArg = null;
        RString? replacementStr = null;
        if (block == null && state.TryGetArgumentAt(1, out var arg1))
        {
            if (arg1.Object is RHash hash)
            {
                hashArg = hash;
            }
            else
            {
                replacementStr = state.GetArgumentAsStringAt(1);
            }
        }

        // Handle Regexp pattern
        if (RegexpMembers.TryGetRegexpData(patternArg, out var regexpData))
        {
            var matches = regexpData.Regex.Matches(input);
            if (matches.Count == 0)
            {
                RegexpMembers.UpdateRegexpGlobalVariables(state, null);
                return inPlace ? MRubyValue.Nil : str.Dup();
            }

            var sb = new StringBuilder();
            var lastEnd = 0;
            MRubyMatchData? lastMatchData = null;

            foreach (Match match in matches)
            {
                sb.Append(input, lastEnd, match.Index - lastEnd);

                var matchData = new MRubyMatchData(match, regexpData, input);
                lastMatchData = matchData;

                string replacement;
                if (block != null)
                {
                    // Set global variables before calling block
                    RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
                    var matchStr = state.NewString(match.Value);
                    var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                    replacement = state.Stringify(blockResult).ConvertToString();
                }
                else if (hashArg != null)
                {
                    var key = state.NewString(match.Value);
                    if (hashArg.TryGetValue(key, out var value))
                    {
                        replacement = state.Stringify(value).ConvertToString();
                    }
                    else
                    {
                        // Key not found in hash - remove the match (Ruby behavior)
                        replacement = "";
                    }
                }
                else
                {
                    replacement = ProcessReplacementString(replacementStr!.ConvertToString(), match, input);
                }

                sb.Append(replacement);
                lastEnd = match.Index + match.Length;
            }

            sb.Append(input, lastEnd, input.Length - lastEnd);

            // Update global variables with last match
            RegexpMembers.UpdateRegexpGlobalVariables(state, lastMatchData);

            var result = sb.ToString();

            if (inPlace)
            {
                var newBytes = Encoding.UTF8.GetBytes(result);
                str.MakeModifiable(newBytes.Length, true);
                newBytes.CopyTo(str.AsSpan());
                return str;
            }
            return state.NewString(result);
        }

        // Handle String pattern
        if (patternArg.Object is RString patternStr)
        {
            var pattern = patternStr.ConvertToString();

            // Handle empty pattern - replace between each character
            if (pattern.Length == 0)
            {
                var sb = new StringBuilder();

                // Insert replacement at start, between each character, and at end
                for (var i = 0; i <= input.Length; i++)
                {
                    string replacement;
                    if (block != null)
                    {
                        var matchStr = state.NewString("");
                        var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                        replacement = state.Stringify(blockResult).ConvertToString();
                    }
                    else if (hashArg != null)
                    {
                        var key = state.NewString("");
                        if (hashArg.TryGetValue(key, out var value))
                        {
                            replacement = state.Stringify(value).ConvertToString();
                        }
                        else
                        {
                            replacement = "";
                        }
                    }
                    else
                    {
                        replacement = replacementStr?.ConvertToString() ?? "";
                    }

                    sb.Append(replacement);
                    if (i < input.Length) sb.Append(input[i]);
                }

                var result = sb.ToString();
                if (inPlace)
                {
                    var newBytes = Encoding.UTF8.GetBytes(result);
                    str.MakeModifiable(newBytes.Length, true);
                    newBytes.CopyTo(str.AsSpan());
                    return str;
                }
                return state.NewString(result);
            }

            {
                var sb = new StringBuilder();
                var lastEnd = 0;
                var hasMatch = false;

                var index = 0;
                while ((index = input.IndexOf(pattern, lastEnd, StringComparison.Ordinal)) >= 0)
                {
                    hasMatch = true;
                    sb.Append(input, lastEnd, index - lastEnd);

                    string replacement;
                    if (block != null)
                    {
                        var matchStr = state.NewString(pattern);
                        var blockResult = state.YieldWithClass(state.StringClass, matchStr, [matchStr], block);
                        replacement = state.Stringify(blockResult).ConvertToString();
                    }
                    else if (hashArg != null)
                    {
                        var key = state.NewString(pattern);
                        if (hashArg.TryGetValue(key, out var value))
                        {
                            replacement = state.Stringify(value).ConvertToString();
                        }
                        else
                        {
                            replacement = "";
                        }
                    }
                    else
                    {
                        replacement = ProcessSimpleReplacementString(replacementStr!.ConvertToString(), pattern, input, index);
                    }

                    sb.Append(replacement);
                    lastEnd = index + pattern.Length;
                }

                if (!hasMatch)
                {
                    return inPlace ? MRubyValue.Nil : str.Dup();
                }

                sb.Append(input, lastEnd, input.Length - lastEnd);
                var result = sb.ToString();

                if (inPlace)
                {
                    var newBytes = Encoding.UTF8.GetBytes(result);
                    str.MakeModifiable(newBytes.Length, true);
                    newBytes.CopyTo(str.AsSpan());
                    return str;
                }
                return state.NewString(result);
            }
        }

        state.Raise(Names.TypeError, "wrong argument type"u8);
        return MRubyValue.Nil;
    }

    static string ProcessReplacementString(string replacement, Match match, string input)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] == '\\' && i + 1 < replacement.Length)
            {
                var next = replacement[i + 1];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case '&':
                    case '0':
                        sb.Append(match.Value);
                        i++;
                        break;
                    case '`':
                        sb.Append(input, 0, match.Index);
                        i++;
                        break;
                    case '\'':
                        sb.Append(input, match.Index + match.Length, input.Length - (match.Index + match.Length));
                        i++;
                        break;
                    case '+':
                        // Last successful capture
                        for (var j = match.Groups.Count - 1; j >= 1; j--)
                        {
                            if (match.Groups[j].Success)
                            {
                                sb.Append(match.Groups[j].Value);
                                break;
                            }
                        }
                        i++;
                        break;
                    case >= '1' and <= '9':
                        var groupIndex = next - '0';
                        if (groupIndex < match.Groups.Count && match.Groups[groupIndex].Success)
                        {
                            sb.Append(match.Groups[groupIndex].Value);
                        }
                        i++;
                        break;
                    default:
                        sb.Append(replacement[i]);
                        break;
                }
            }
            else
            {
                sb.Append(replacement[i]);
            }
        }
        return sb.ToString();
    }

    static string ProcessSimpleReplacementString(string replacement, string matched, string input, int matchIndex)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < replacement.Length; i++)
        {
            if (replacement[i] == '\\' && i + 1 < replacement.Length)
            {
                var next = replacement[i + 1];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case '&':
                    case '0':
                        sb.Append(matched);
                        i++;
                        break;
                    case '`':
                        sb.Append(input, 0, matchIndex);
                        i++;
                        break;
                    case '\'':
                        sb.Append(input, matchIndex + matched.Length, input.Length - (matchIndex + matched.Length));
                        i++;
                        break;
                    case >= '1' and <= '9':
                    case '+':
                        // No capture groups for string pattern - these are empty
                        i++;
                        break;
                    default:
                        sb.Append(replacement[i]);
                        break;
                }
            }
            else
            {
                sb.Append(replacement[i]);
            }
        }
        return sb.ToString();
    }

    // String#scan(pattern) or String#scan(pattern) { |match| block }
    [MRubyMethod(RequiredArguments = 1, BlockArgument = true)]
    public static MRubyMethod Scan = new((state, self) =>
    {
        var str = self.As<RString>();
        var patternArg = state.GetArgumentAt(0);
        var block = state.GetBlockArgument();
        var input = str.ConvertToString();

        MRubyRegexpData regexpData;
        if (RegexpMembers.TryGetRegexpData(patternArg, out var data))
        {
            regexpData = data;
        }
        else if (patternArg.Object is RString patternStr)
        {
            try
            {
                regexpData = new MRubyRegexpData(Regex.Escape(patternStr.ConvertToString()));
            }
            catch (ArgumentException ex)
            {
                state.Raise(Names.RegexpError, $"{ex.Message}");
                return MRubyValue.Nil;
            }
        }
        else
        {
            state.Raise(Names.TypeError, "wrong argument type"u8);
            return MRubyValue.Nil;
        }

        var matches = regexpData.Regex.Matches(input);
        var result = state.NewArray(matches.Count);

        foreach (Match match in matches)
        {
            var matchData = new MRubyMatchData(match, regexpData, input);
            RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);

            MRubyValue item;
            if (match.Groups.Count > 1)
            {
                // Has capture groups - return array of captures
                var captures = state.NewArray(match.Groups.Count - 1);
                for (var i = 1; i < match.Groups.Count; i++)
                {
                    if (match.Groups[i].Success)
                    {
                        captures.Push(state.NewString(match.Groups[i].Value));
                    }
                    else
                    {
                        captures.Push(MRubyValue.Nil);
                    }
                }
                item = captures;
            }
            else
            {
                // No capture groups - return matched string
                item = state.NewString(match.Value);
            }

            if (block != null)
            {
                state.YieldWithClass(state.StringClass, self, [item], block);
            }
            else
            {
                result.Push(item);
            }
        }

        return block != null ? self : (MRubyValue)result;
    });

    // String#index with Regexp support
    [MRubyMethod(RequiredArguments = 1, OptionalArguments = 1)]
    public static MRubyMethod Index = new((state, self) =>
    {
        var str = self.As<RString>();
        var arg = state.GetArgumentAt(0);

        // Check if it's a Regexp
        if (RegexpMembers.TryGetRegexpData(arg, out var regexpData))
        {
            var input = str.ConvertToString();
            var pos = 0;
            if (state.TryGetArgumentAt(1, out var posValue))
            {
                pos = (int)state.AsInteger(posValue);
            }

            if (pos < 0)
            {
                pos = input.Length + pos;
            }
            if (pos < 0 || pos > input.Length)
            {
                return MRubyValue.Nil;
            }

            var match = regexpData.Regex.Match(input, pos);
            if (!match.Success)
            {
                return MRubyValue.Nil;
            }

            var matchData = new MRubyMatchData(match, regexpData, input);
            RegexpMembers.UpdateRegexpGlobalVariables(state, matchData);
            return match.Index;
        }

        // Fall back to string index
        return StringMembers.Index.Invoke(state, self);
    });
}
