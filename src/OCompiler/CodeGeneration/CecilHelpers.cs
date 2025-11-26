using System;
using System.Linq;
using Mono.Cecil;

namespace OCompiler.CodeGeneration
{
    internal static class CecilHelpers
    {
        public static void SetEntryPointWithCecil(string dllPath)
        {
            // Load module in read-write mode
            var readerParams = new ReaderParameters { ReadWrite = true };
            using var mod = ModuleDefinition.ReadModule(dllPath, readerParams);

            // Try to find the <Program>.Main(string[]) method (static)
            MethodDefinition? entry = null;

            // Prefer type named <Program>
            var programType = mod.Types.FirstOrDefault(t => t.Name == "<Program>" || t.Name == "Program" || t.Name == "Main");
            if (programType != null)
            {
                entry = programType.Methods.FirstOrDefault(m => (m.Name == "Main" || m.Name == "main") && m.IsStatic);
            }

            // Fallback: search all methods
            if (entry == null)
            {
                entry = mod.Types.SelectMany(t => t.Methods)
                    .FirstOrDefault(m => (m.Name == "Main" || m.Name == "main") && m.IsStatic);
            }

            if (entry == null)
            {
                throw new InvalidOperationException("Could not find suitable static Main method to set as entry point");
            }

            // Set module entry point
            mod.EntryPoint = entry;

            // Persist changes
            mod.Write();
        }
    }
}
