using MRubyCS.StdLib;

namespace MRubyCS;

partial class MRubyState
{
    public RClass RegexpClass { get; private set; } = default!;
    public RClass MatchDataClass { get; private set; } = default!;

    public void DefineRegexp()
    {
        // Define Regexp class
        RegexpClass = DefineClass(Intern("Regexp"u8), ObjectClass, MRubyVType.CSharpData);

        // Constants
        DefineConst(RegexpClass, Intern("IGNORECASE"u8), MRubyRegexpData.RubyIgnoreCase);
        DefineConst(RegexpClass, Intern("EXTENDED"u8), MRubyRegexpData.RubyExtended);
        DefineConst(RegexpClass, Intern("MULTILINE"u8), MRubyRegexpData.RubyMultiline);

        // Class methods
        DefineSingletonMethod(RegexpClass, Intern("new"u8), RegexpMembers.New);
        DefineClassMethod(RegexpClass, Intern("compile"u8), RegexpMembers.Compile);
        DefineClassMethod(RegexpClass, Intern("escape"u8), RegexpMembers.Escape);
        DefineClassMethod(RegexpClass, Intern("quote"u8), RegexpMembers.Quote);
        DefineClassMethod(RegexpClass, Intern("union"u8), RegexpMembers.Union);
        DefineClassMethod(RegexpClass, Intern("try_convert"u8), RegexpMembers.TryConvert);
        DefineClassMethod(RegexpClass, Intern("last_match"u8), RegexpMembers.LastMatch);

        // Instance methods
        DefineMethod(RegexpClass, Intern("initialize"u8), RegexpMembers.New);
        DefineMethod(RegexpClass, Intern("match"u8), RegexpMembers.Match);
        DefineMethod(RegexpClass, Intern("match?"u8), RegexpMembers.QMatch);
        DefineMethod(RegexpClass, Intern("=~"u8), RegexpMembers.OpMatch);
        DefineMethod(RegexpClass, Intern("==="u8), RegexpMembers.Eqq);
        DefineMethod(RegexpClass, Intern("source"u8), RegexpMembers.Source);
        DefineMethod(RegexpClass, Intern("options"u8), RegexpMembers.Options);
        DefineMethod(RegexpClass, Intern("casefold?"u8), RegexpMembers.QCasefold);
        DefineMethod(RegexpClass, Intern("to_s"u8), RegexpMembers.ToS);
        DefineMethod(RegexpClass, Intern("inspect"u8), RegexpMembers.Inspect);
        DefineMethod(RegexpClass, Intern("=="u8), RegexpMembers.OpEq);
        DefineMethod(RegexpClass, Intern("eql?"u8), RegexpMembers.QEql);
        DefineMethod(RegexpClass, Intern("hash"u8), RegexpMembers.Hash);
        DefineMethod(RegexpClass, Intern("named_captures"u8), RegexpMembers.NamedCaptures);
        DefineMethod(RegexpClass, Intern("names"u8), RegexpMembers.NamesMethod);

        // Define MatchData class
        MatchDataClass = DefineClass(Intern("MatchData"u8), ObjectClass, MRubyVType.CSharpData);

        // Instance methods
        DefineMethod(MatchDataClass, Intern("[]"u8), MatchDataMembers.OpAref);
        DefineMethod(MatchDataClass, Intern("begin"u8), MatchDataMembers.Begin);
        DefineMethod(MatchDataClass, Intern("end"u8), MatchDataMembers.End);
        DefineMethod(MatchDataClass, Intern("offset"u8), MatchDataMembers.Offset);
        DefineMethod(MatchDataClass, Intern("captures"u8), MatchDataMembers.Captures);
        DefineMethod(MatchDataClass, Intern("to_a"u8), MatchDataMembers.ToA);
        DefineMethod(MatchDataClass, Intern("to_s"u8), MatchDataMembers.ToS);
        DefineMethod(MatchDataClass, Intern("size"u8), MatchDataMembers.Size);
        DefineMethod(MatchDataClass, Intern("length"u8), MatchDataMembers.Length);
        DefineMethod(MatchDataClass, Intern("pre_match"u8), MatchDataMembers.PreMatch);
        DefineMethod(MatchDataClass, Intern("post_match"u8), MatchDataMembers.PostMatch);
        DefineMethod(MatchDataClass, Intern("regexp"u8), MatchDataMembers.Regexp);
        DefineMethod(MatchDataClass, Intern("string"u8), MatchDataMembers.String);
        DefineMethod(MatchDataClass, Intern("named_captures"u8), MatchDataMembers.NamedCaptures);
        DefineMethod(MatchDataClass, Intern("names"u8), MatchDataMembers.NamesMethod);
        DefineMethod(MatchDataClass, Intern("values_at"u8), MatchDataMembers.ValuesAt);
        DefineMethod(MatchDataClass, Intern("inspect"u8), MatchDataMembers.Inspect);

        // Regexp-related String methods
        DefineMethod(StringClass, Intern("=~"u8), StringRegexpMembers.OpMatch);
        DefineMethod(StringClass, Intern("match"u8), StringRegexpMembers.Match);
        DefineMethod(StringClass, Intern("match?"u8), StringRegexpMembers.QMatch);
        DefineMethod(StringClass, Intern("sub"u8), StringRegexpMembers.Sub);
        DefineMethod(StringClass, Intern("sub!"u8), StringRegexpMembers.SubBang);
        DefineMethod(StringClass, Intern("gsub"u8), StringRegexpMembers.Gsub);
        DefineMethod(StringClass, Intern("gsub!"u8), StringRegexpMembers.GsubBang);
        DefineMethod(StringClass, Intern("scan"u8), StringRegexpMembers.Scan);
        DefineMethod(StringClass, Intern("index"u8), StringRegexpMembers.Index);
    }
}