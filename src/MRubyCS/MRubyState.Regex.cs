namespace MRubyCS;

partial class MRubyState
{
    public void DefineRegexp()
    {
        var regexpClass = DefineClass(Intern("Regexp"u8), ObjectClass, MRubyVType.CSharpData);


    }
}