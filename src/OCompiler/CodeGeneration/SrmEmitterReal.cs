using System;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Реальная реализация SRM-эмиттера (частично). Первый рабочий шаг: EmitConsolePoC —
    /// генерирует минимальную сборку с типом `App` и конструктором, который вызывает
    /// `System.Console.WriteLine(string)`.
    ///
    /// Это PoC: основной упор на то, чтобы показать работу через System.Reflection.Metadata.
    /// </summary>
    public static class SrmEmitterReal
    {
        public static void EmitConsolePoC(string outputPath)
        {
            // Message to print
            const string message = "[SRM-POC] Hello from emitted SRM PE (Console)";

            var metadataBuilder = new MetadataBuilder();

            // Assembly & module names
            var assemblyName = Path.GetFileNameWithoutExtension(outputPath);
            // var moduleName = metadataBuilder.GetOrAddString(assemblyName + ".dll");

            // Add assembly definition (fix: add hashAlgorithm)
            var asmHandle = metadataBuilder.AddAssembly(
                name: metadataBuilder.GetOrAddString(assemblyName),
                version: new Version(1, 0, 0, 0),
                culture: default(StringHandle),
                publicKey: default(BlobHandle),
                flags: AssemblyFlags.PublicKey,
                hashAlgorithm: AssemblyHashAlgorithm.None);

            // Add TypeRef for System.Console in its defining assembly
            var consoleType = typeof(Console);
            var consoleAssemblyName = consoleType.Assembly.GetName().Name ?? "System.Private.CoreLib";
            var consoleNamespace = metadataBuilder.GetOrAddString(typeof(Console).Namespace ?? "System");
            var consoleName = metadataBuilder.GetOrAddString("Console");

            // Create an AssemblyRef to the assembly that contains System.Console
            var asmRef = metadataBuilder.AddAssemblyReference(
                name: metadataBuilder.GetOrAddString(consoleAssemblyName),
                version: new Version(0,0,0,0),
                culture: default(StringHandle),
                publicKeyOrToken: default(BlobHandle),
                hashValue: default(BlobHandle),
                flags: 0);

            var consoleTypeRef = metadataBuilder.AddTypeReference(
                asmRef,
                consoleNamespace,
                consoleName);

            // Signature for void WriteLine(string)
            var sigBlob = new BlobBuilder();
            var encoder = new BlobEncoder(sigBlob);
            var methodSigEncoder = encoder.MethodSignature();
            methodSigEncoder.Parameters(1, out var retEncoder, out var paramsEncoder);
            retEncoder.Void();
            paramsEncoder.AddParameter().Type().String();

            var memberRef = metadataBuilder.AddMemberReference(
                consoleTypeRef,
                metadataBuilder.GetOrAddString("WriteLine"),
                metadataBuilder.GetOrAddBlob(sigBlob));

            // Build type App with a public constructor
            // Removed: typeName, systemNamespace, objectName, mscorlibAssemblyRef, objectTypeRef

            // Add parameterless constructor method definition
            var methodName = metadataBuilder.GetOrAddString(".ctor");
            var methodSigBuilder = new BlobBuilder();
            var encoderCtor = new BlobEncoder(methodSigBuilder);
            var methodSigEncoderCtor = encoderCtor.MethodSignature();
            methodSigEncoderCtor.Parameters(0, out var retEncoderCtor, out _);
            retEncoderCtor.Void();

            // var methodHandle = metadataBuilder.AddMethodDefinition(
            //     MethodAttributes.Public | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName,
            //     MethodImplAttributes.IL,
            //     methodName,
            //     metadataBuilder.GetOrAddBlob(methodSigBuilder),
            //     bodyOffset: 0,
            //     parameterList: default(ParameterHandle));

            // Add the type's method list: need to update the TypeDefinition with the method index.
            // For simplicity, we will not update the methodList here; ManagedPEBuilder will fix up relative ordering.

            // Build IL: ldstr <user string token>; call memberref; ret
            var il = new BlobBuilder();
            // add user string
            var userStrHandle = metadataBuilder.GetOrAddUserString(message);
            // Compute user string token: high byte 0x70 + heap offset
            int userStrOffset = MetadataTokens.GetHeapOffset(userStrHandle);
            int userStrToken = 0x70000000 | userStrOffset;

            int memberRefToken = MetadataTokens.GetToken(memberRef);

            il.WriteByte(0x72); // ldstr
            il.WriteInt32(userStrToken);
            il.WriteByte(0x28); // call
            il.WriteInt32(memberRefToken);
            il.WriteByte(0x2A); // ret

            // Build method body blob (tiny header for IL method)
            var methodBody = new BlobBuilder();
            // Small header: 1 byte header + code size (for tiny methods) is possible only if code size < 64 and no locals.
            if (il.Count < 64)
            {
                byte header = (byte)((il.Count << 2) | 0x2); // Tiny header: Flags=2
                methodBody.WriteByte(header);
                methodBody.WriteBytes(il.ToArray());
            }
            else
            {
                // Not expected for our tiny sample
                throw new InvalidOperationException("IL too large for tiny header in PoC");
            }

            // Prepare streams for PE
            var metadataStream = new BlobBuilder();
            var idBuilder = new MetadataRootBuilder(metadataBuilder);
            idBuilder.Serialize(metadataStream, 0, 0);

            var peHeaders = new PEHeaderBuilder(
                imageCharacteristics: Characteristics.Dll,
                imageBase: 0x00400000,
                fileAlignment: 0x200,
                sectionAlignment: 0x1000,
                subsystem: Subsystem.WindowsCui);

            // Define the Main method as the entry point
            var mainMethodName = metadataBuilder.GetOrAddString("Main");
            var mainMethodSigBuilder = new BlobBuilder();
            var mainEncoder = new BlobEncoder(mainMethodSigBuilder);
            var mainMethodSigEncoder = mainEncoder.MethodSignature();
            mainMethodSigEncoder.Parameters(0, out var mainRetEncoder, out _);
            mainRetEncoder.Void();

            var mainMethodDef = metadataBuilder.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig,
                MethodImplAttributes.IL,
                mainMethodName,
                metadataBuilder.GetOrAddBlob(mainMethodSigBuilder),
                bodyOffset: 0,
                parameterList: default(ParameterHandle));

            // Set the entry point
            var peBuilder = new ManagedPEBuilder(
                peHeaders,
                new MetadataRootBuilder(metadataBuilder),
                ilStream: methodBody,
                entryPoint: mainMethodDef);

            // Serialize the PE file
            var peBlobBuilder = new BlobBuilder();
            peBuilder.Serialize(peBlobBuilder);
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                peBlobBuilder.WriteContentTo(fs);
            }

            Console.WriteLine("**[ OK ] SRM PE emitted with Main entry point.");

            // Try to load and execute the emitted assembly in-process
            try
            {
                var asm = Assembly.LoadFile(Path.GetFullPath(outputPath));
                var t = asm.GetType("App");
                if (t != null)
                {
                    Activator.CreateInstance(t);
                    Console.WriteLine("**[ OK ] Executed SRM emitted assembly (ctor invoked)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"**[ WARN ] Could not load/execute SRM PE: {ex.Message}");
            }

            // Validate metadata completeness
            Console.WriteLine("**[ INFO ] Metadata validation skipped (method unavailable).");
        }

        public static void EmitBuiltinPrintPoC(string outputPath, Parser.ProgramNode ast)
        {
            // Пример: ищем класс Main и его конструктор this()
        {
            // 1. Найти Main.this() и переменную c, а также вызов c.Print()
            var mainClass = ast.Classes.Find(cls => cls.Name == "Main");
            if (mainClass == null)
            {
                Console.WriteLine("**[ ERR ] Main class not found in AST");
                return;
            }
            var ctor = mainClass.Members.Find(m => m is Parser.ConstructorDeclaration) as Parser.ConstructorDeclaration;
            if (ctor == null)
            {
                Console.WriteLine("**[ ERR ] Main.this() not found in AST");
                return;
            }
            int? cValue = null;
            foreach (var elem in ctor.Body.Elements)
            {
                if (elem is Parser.VariableDeclaration v && v.Identifier == "c")
                {
                    // Поддержка двух вариантов: прямой IntegerLiteral и ConstructorInvocation
                    if (v.Expression is Parser.IntegerLiteral intLit)
                    {
                        cValue = intLit.Value;
                    }
                    else if (v.Expression is Parser.ConstructorInvocation ci && ci.Arguments.Count == 1 && ci.Arguments[0] is Parser.IntegerLiteral intLit2)
                    {
                        cValue = intLit2.Value;
                    }
                }
                if (elem is Parser.ExpressionStatement es && es.Expression is Parser.FunctionalCall fc)
                {
                    if (fc.Function is Parser.MemberAccessExpression ma && ma.Target is Parser.IdentifierExpression id && id.Name == "c")
                    {
                        if (ma.Member is Parser.IdentifierExpression methodId && methodId.Name == "Print")
                        {
                        }
                    }
                }
            }
            if (cValue == null)
            {
                Console.WriteLine("**[ ERR ] Не найдена переменная c или вызов c.Print() в Main.this()");
                return;
            }

            // 2. Генерация PE
            var metadataBuilder = new MetadataBuilder();
            var assemblyName = Path.GetFileNameWithoutExtension(outputPath);
            metadataBuilder.AddAssembly(
                name: metadataBuilder.GetOrAddString(assemblyName),
                version: new Version(1, 0, 0, 0),
                culture: default(StringHandle),
                publicKey: default(BlobHandle),
                flags: 0,
                hashAlgorithm: AssemblyHashAlgorithm.None);

            var builtinAsmNameStr = typeof(BuiltinTypes).Assembly.GetName().Name ?? "OCompiler";
            var builtinAsmName = metadataBuilder.GetOrAddString(builtinAsmNameStr);
            var builtinAsmRef = metadataBuilder.AddAssemblyReference(
                builtinAsmName,
                typeof(BuiltinTypes).Assembly.GetName().Version ?? new Version(1,0,0,0),
                default(StringHandle),
                default(BlobHandle),
                0,
                default(BlobHandle));

            var nsHandle = metadataBuilder.GetOrAddString("OCompiler.CodeGeneration.BuiltinTypes");
            var typeHandle = metadataBuilder.GetOrAddString("OInteger");
            var ointegerTypeRef = metadataBuilder.AddTypeReference(
                builtinAsmRef,
                nsHandle,
                typeHandle);

            var sigBlob = new BlobBuilder();
            var encoder = new BlobEncoder(sigBlob);
            var methodSigEncoder = encoder.MethodSignature();
            methodSigEncoder.Parameters(1, out var retEncoder, out var paramsEncoder);
            retEncoder.Void();
            paramsEncoder.AddParameter().Type().Int32();

            var memberRef = metadataBuilder.AddMemberReference(
                ointegerTypeRef,
                metadataBuilder.GetOrAddString("Print"),
                metadataBuilder.GetOrAddBlob(sigBlob));

            var typeName = metadataBuilder.GetOrAddString("App");
            var systemNamespace = metadataBuilder.GetOrAddString("System");
            var objectName = metadataBuilder.GetOrAddString("Object");
            var mscorlibAssemblyRef = metadataBuilder.AddAssemblyReference(
                metadataBuilder.GetOrAddString("System.Private.CoreLib"),
                new Version(4, 0, 0, 0),
                default(StringHandle),
                default(BlobHandle),
                0,
                default(BlobHandle));
            var objectTypeRef = metadataBuilder.AddTypeReference(
                mscorlibAssemblyRef,
                systemNamespace,
                objectName);

            metadataBuilder.AddTypeDefinition(
                TypeAttributes.Public | TypeAttributes.Class,
                typeName,
                typeName,
                objectTypeRef,
                default(FieldDefinitionHandle),
                default(MethodDefinitionHandle));

            var methodName = metadataBuilder.GetOrAddString(".ctor");
            var methodSigBuilder = new BlobBuilder();
            var encoderCtor = new BlobEncoder(methodSigBuilder);
            var methodSigEncoderCtor = encoderCtor.MethodSignature();
            methodSigEncoderCtor.Parameters(0, out var retEncoderCtor, out _);
            retEncoderCtor.Void();

            metadataBuilder.AddMethodDefinition(
                MethodAttributes.Public | MethodAttributes.RTSpecialName | MethodAttributes.SpecialName,
                MethodImplAttributes.IL,
                methodName,
                metadataBuilder.GetOrAddBlob(methodSigBuilder),
                bodyOffset: 0,
                parameterList: default(ParameterHandle));

            var il = new BlobBuilder();
            int value = cValue.Value;
            if (value >= -1 && value <= 8)
            {
                il.WriteByte((byte)(0x16 + value));
            }
            else if (value >= sbyte.MinValue && value <= sbyte.MaxValue)
            {
                il.WriteByte(0x1F);
                il.WriteSByte((sbyte)value);
            }
            else
            {
                il.WriteByte(0x20);
                il.WriteInt32(value);
            }
            int memberRefToken = MetadataTokens.GetToken(memberRef);
            il.WriteByte(0x28);
            il.WriteInt32(memberRefToken);
            il.WriteByte(0x2A);

            var methodBody = new BlobBuilder();
            if (il.Count < 64)
            {
                byte header = (byte)((il.Count << 2) | 0x2);
                methodBody.WriteByte(header);
                methodBody.WriteBytes(il.ToArray());
            }
            else
            {
                throw new InvalidOperationException("IL too large for tiny header in PoC");
            }

            var peHeaders = new PEHeaderBuilder(
                imageCharacteristics: Characteristics.Dll,
                imageBase: 0x00400000,
                fileAlignment: 0x200,
                sectionAlignment: 0x1000,
                subsystem: Subsystem.WindowsCui);

            var contentBuilder = new ManagedPEBuilder(
                peHeaders,
                new MetadataRootBuilder(metadataBuilder),
                ilStream: methodBody);
            var peBlob = new BlobBuilder();
            contentBuilder.Serialize(peBlob);
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            {
                peBlob.WriteContentTo(fs);
            }

            Console.WriteLine($"**[ OK ] SRM PE (Builtin Print) emitted to: {outputPath}");
        }
        }
           
    }
}
