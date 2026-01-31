using MRubyCS.StdLib;

namespace MRubyCS;

partial class MRubyState
{
    public RClass RegexpClass { get; private set; } = default!;
    public RClass MatchDataClass { get; private set; } = default!;

    public void DefineRegexp()
    {
        // Define Regexp class
        RegexpClass = DefineClass(Names.Regexp, ObjectClass, MRubyVType.CSharpData);

        // Constants
        DefineConst(RegexpClass, Intern("IGNORECASE"u8), MRubyRegexpData.RubyIgnoreCase);
        DefineConst(RegexpClass, Intern("EXTENDED"u8), MRubyRegexpData.RubyExtended);
        DefineConst(RegexpClass, Intern("MULTILINE"u8), MRubyRegexpData.RubyMultiline);

        // Class methods
        DefineSingletonMethod(RegexpClass, Names.New, RegexpMembers.New);
        DefineClassMethod(RegexpClass, Names.Compile, RegexpMembers.Compile);
        DefineClassMethod(RegexpClass, Names.Escape, RegexpMembers.Escape);
        DefineClassMethod(RegexpClass, Names.Quote, RegexpMembers.Quote);
        DefineClassMethod(RegexpClass, Names.Union, RegexpMembers.Union);
        DefineClassMethod(RegexpClass, Names.TryConvert, RegexpMembers.TryConvert);
        DefineClassMethod(RegexpClass, Names.LastMatch, RegexpMembers.LastMatch);

        // Instance methods
        DefineMethod(RegexpClass, Names.Initialize, RegexpMembers.New);
        DefineMethod(RegexpClass, Names.Match, RegexpMembers.Match);
        DefineMethod(RegexpClass, Names.QMatch, RegexpMembers.QMatch);
        DefineMethod(RegexpClass, Intern("=~"u8), RegexpMembers.OpMatch);
        DefineMethod(RegexpClass, Names.OpEqq, RegexpMembers.Eqq);
        DefineMethod(RegexpClass, Names.Source, RegexpMembers.Source);
        DefineMethod(RegexpClass, Names.Options, RegexpMembers.Options);
        DefineMethod(RegexpClass, Names.QCasefold, RegexpMembers.QCasefold);
        DefineMethod(RegexpClass, Names.ToS, RegexpMembers.ToS);
        DefineMethod(RegexpClass, Names.Inspect, RegexpMembers.Inspect);
        DefineMethod(RegexpClass, Names.OpEq, RegexpMembers.OpEq);
        DefineMethod(RegexpClass, Names.QEql, RegexpMembers.QEql);
        DefineMethod(RegexpClass, Names.Hash, RegexpMembers.Hash);
        DefineMethod(RegexpClass, Names.NamedCaptures, RegexpMembers.NamedCaptures);
        DefineMethod(RegexpClass, Intern("names"u8), RegexpMembers.NamesMethod);

        // Define MatchData class
        MatchDataClass = DefineClass(Names.MatchData, ObjectClass, MRubyVType.CSharpData);

        // Instance methods
        DefineMethod(MatchDataClass, Names.OpAref, MatchDataMembers.OpAref);
        DefineMethod(MatchDataClass, Names.Begin, MatchDataMembers.Begin);
        DefineMethod(MatchDataClass, Names.End, MatchDataMembers.End);
        DefineMethod(MatchDataClass, Names.Offset, MatchDataMembers.Offset);
        DefineMethod(MatchDataClass, Names.Captures, MatchDataMembers.Captures);
        DefineMethod(MatchDataClass, Names.ToA, MatchDataMembers.ToA);
        DefineMethod(MatchDataClass, Names.ToS, MatchDataMembers.ToS);
        DefineMethod(MatchDataClass, Intern("size"u8), MatchDataMembers.Size);
        DefineMethod(MatchDataClass, Intern("length"u8), MatchDataMembers.Length);
        DefineMethod(MatchDataClass, Names.PreMatch, MatchDataMembers.PreMatch);
        DefineMethod(MatchDataClass, Names.PostMatch, MatchDataMembers.PostMatch);
        DefineMethod(MatchDataClass, Intern("regexp"u8), MatchDataMembers.Regexp);
        DefineMethod(MatchDataClass, Names.String, MatchDataMembers.String);
        DefineMethod(MatchDataClass, Names.NamedCaptures, MatchDataMembers.NamedCaptures);
        DefineMethod(MatchDataClass, Intern("names"u8), MatchDataMembers.NamesMethod);
        DefineMethod(MatchDataClass, Names.ValuesAt, MatchDataMembers.ValuesAt);
        DefineMethod(MatchDataClass, Names.Inspect, MatchDataMembers.Inspect);

        // Regexp-related String methods
        DefineMethod(StringClass, Intern("=~"u8), StringRegexpMembers.OpMatch);
        DefineMethod(StringClass, Names.Match, StringRegexpMembers.Match);
        DefineMethod(StringClass, Names.QMatch, StringRegexpMembers.QMatch);
        DefineMethod(StringClass, Names.Sub, StringRegexpMembers.Sub);
        DefineMethod(StringClass, Names.SubBang, StringRegexpMembers.SubBang);
        DefineMethod(StringClass, Names.Gsub, StringRegexpMembers.Gsub);
        DefineMethod(StringClass, Names.GsubBang, StringRegexpMembers.GsubBang);
        DefineMethod(StringClass, Names.Scan, StringRegexpMembers.Scan);
        DefineMethod(StringClass, Intern("index"u8), StringRegexpMembers.Index);
    }
}
