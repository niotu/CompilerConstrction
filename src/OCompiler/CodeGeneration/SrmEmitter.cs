using System;
using System.IO;

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Placeholder SRM emitter. Intention: implement low-level PE/metadata emission
    /// using System.Reflection.Metadata. For now this file provides a thin wrapper
    /// that delegates to the existing PeEmitter PoC so CLI flags work while SRM
    /// implementation is developed iteratively.
    ///
    /// TODO: Replace PeEmitter calls with direct System.Reflection.Metadata usage.
    /// </summary>
    public static class SrmEmitter
    {
        public static void EmitConsolePoC(string outputPath)
        {
            Console.WriteLine("**[ INFO ] SRM emitter placeholder: delegating to PeEmitter (Reflection.Emit) for Console PoC");
            PeEmitter.EmitBuiltinPrintPoC(outputPath, 0); // temporary: use builtin print with value 0 as proxy
        }

        public static void EmitBuiltinPrintPoC(string outputPath, OCompiler.Parser.ProgramNode ast)
        {
            Console.WriteLine("**[ INFO ] SRM emitter: emitting Builtin Print PoC via SRM (AST mode)");
            SrmEmitterReal.EmitBuiltinPrintPoC(outputPath, ast);
        }
    }
}
