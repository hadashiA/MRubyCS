using System.Text;
using MRubyCS;
using MRubyCS.Compiler;
using UnityEngine;

public class SampleBehaviour : MonoBehaviour
{
    void Start()
    {
        var state = MRubyState.Create();
        var compiler = MRubyCompiler.Create(state);
        var result = compiler.LoadSourceCode(Encoding.UTF8.GetBytes("1 + 1"));
        Debug.Log(result);
    }
}
