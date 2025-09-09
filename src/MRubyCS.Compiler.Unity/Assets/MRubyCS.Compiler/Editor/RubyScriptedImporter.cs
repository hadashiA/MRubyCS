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
            var rbAsset = new TextAsset(source)
            {
                name = Path.GetFileName(ctx.assetPath)
            };
            ctx.AddObjectToAsset("Main", rbAsset);
            ctx.SetMainObject(rbAsset);

            if (compiler == null)
            {
                var state = MRubyState.Create();
                compiler = MRubyCompiler.Create(state);
            }

            using var bin = compiler.CompileToBytecode(source);
            var mrbAsset = new TextAsset(bin.AsSpan())
            {
                name = Path.GetFileNameWithoutExtension(ctx.assetPath) + ".mrb"
            };
            ctx.AddObjectToAsset("Bytecode", mrbAsset);
        }
    }
}
