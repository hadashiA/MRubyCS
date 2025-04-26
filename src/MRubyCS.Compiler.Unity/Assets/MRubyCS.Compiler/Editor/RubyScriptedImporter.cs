using System.IO;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace MRubyCS.Compiler.Editor
{
    [ScriptedImporter(1, "rb")]
    public class RubyScriptedImporter : ScriptedImporter
    {
        static MRubyCompiler? compiler;

        public override void OnImportAsset(AssetImportContext ctx)
        {
            var source = File.ReadAllBytes(ctx.assetPath);
            var rbAsset = new TextAsset(source);
            rbAsset.name = Path.GetFileName(ctx.assetPath);
            ctx.AddObjectToAsset("Main", rbAsset);
            ctx.SetMainObject(rbAsset);

            if (compiler == null)
            {
                var state = MRubyState.Create();
                compiler = MRubyCompiler.Create(state);
            }

            var bin = compiler.CompileToBinaryFormat(source);
            var mrbAsset = new TextAsset(bin.GetNativeData())
            {
                name = Path.GetFileNameWithoutExtension(ctx.assetPath) + ".mrb"
            };
            ctx.AddObjectToAsset("Bytecode", mrbAsset);
        }
    }
}
