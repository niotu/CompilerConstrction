using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;

#if NET9_0_OR_GREATER
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
#endif

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Small PoC emitter that creates a persisted PE which executes a tiny constructor.
    /// Two helpers:
    ///  - EmitConsolePoC: constructor calls Console.WriteLine(string)
    ///  - EmitBuiltinPrintPoC: constructor calls BuiltinTypes.OInteger.Print(int)
    ///
    /// This uses Reflection.Emit and PersistedAssemblyBuilder on .NET 9+. On older runtimes
    /// the emitter will throw NotSupportedException when attempting to save.
    /// </summary>
    public static class PeEmitter
    {
        // Console PoC removed by user request. Use EmitBuiltinPrintPoC or SRM emitter instead.

        public static void EmitBuiltinPrintPoC(string outputPath, int value)
        {
            var assemblyName = new AssemblyName(Path.GetFileNameWithoutExtension(outputPath));

            #if NET9_0_OR_GREATER
            var assemblyBuilder = new PersistedAssemblyBuilder(assemblyName, typeof(object).Assembly);
            #else
            var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
            #endif

            var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);
            var typeBuilder = moduleBuilder.DefineType("App", TypeAttributes.Public | TypeAttributes.Class);

            var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var il = ctor.GetILGenerator();

            // Emit: BuiltinTypes.OInteger.Print(value);
            il.Emit(OpCodes.Ldc_I4, value);

            // Find the runtime method info for BuiltinTypes.OInteger.Print(int)
            var builtinType = typeof(BuiltinTypes).GetNestedType("OInteger", BindingFlags.Public | BindingFlags.Static);
            if (builtinType == null)
                throw new InvalidOperationException("BuiltinTypes.OInteger type not found in runtime.");

            var printMethod = builtinType.GetMethod("Print", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(int) }, null);
            if (printMethod == null)
                throw new InvalidOperationException("Builtin Print(int) method not found.");

            il.Emit(OpCodes.Call, printMethod);
            il.Emit(OpCodes.Ret);

            var created = typeBuilder.CreateTypeInfo().AsType();

            #if NET9_0_OR_GREATER
            var persisted = (PersistedAssemblyBuilder)assemblyBuilder;

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                persisted.Save(fs);
                fs.Flush();
            }

            Console.WriteLine($"**[ OK ] PE emitted to: {outputPath}");

            try
            {
                var loaded = Assembly.LoadFile(Path.GetFullPath(outputPath));
                var t = loaded.GetType("App");
                if (t != null)
                {
                    Activator.CreateInstance(t);
                    Console.WriteLine("**[ OK ] Executed emitted assembly (constructor invoked)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"**[ WARN ] Could not load/execute emitted PE: {ex.Message}");
            }
            #else
            throw new NotSupportedException("Persisted PE emission requires .NET 9 or greater. Use --run to execute in-memory.");
            #endif
        }
    }
}
