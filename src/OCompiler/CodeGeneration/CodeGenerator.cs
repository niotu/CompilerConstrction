using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OCompiler.Parser;
using OCompiler.Semantic;
using System.IO;


#if NET9_0_OR_GREATER
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
#endif

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Главный класс генератора кода для языка O.
    /// Генерирует .NET сборки из AST с использованием System.Reflection.Emit.
    /// </summary>
    public class CodeGenerator
    {
        private readonly AssemblyBuilder _assemblyBuilder;
        private readonly ModuleBuilder _moduleBuilder;
        private readonly TypeMapper _typeMapper;
        private readonly MethodGenerator _methodGenerator;
        private readonly Dictionary<string, TypeBuilder> _typeBuilders;
        private readonly Dictionary<string, Type> _completedTypes;
        private readonly ClassHierarchy _hierarchy;
        private readonly string _assemblyName;

        public CodeGenerator(string assemblyName, ClassHierarchy hierarchy)
        {
            _assemblyName = assemblyName;
            _hierarchy = hierarchy;
            _typeBuilders = new Dictionary<string, TypeBuilder>();
            _completedTypes = new Dictionary<string, Type>();

            // Создаём динамическую сборку
            var assemblyNameObj = new AssemblyName(assemblyName);
            
            #if NET9_0_OR_GREATER
            // .NET 9+: Используем PersistedAssemblyBuilder для сохранения в файл
            _assemblyBuilder = new PersistedAssemblyBuilder(
                assemblyNameObj,
                typeof(object).Assembly);
            #else
            // .NET 8 и ниже: Только выполнение в памяти
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                assemblyNameObj,
                AssemblyBuilderAccess.RunAndCollect);
            #endif

            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
            _typeMapper = new TypeMapper(_moduleBuilder, _hierarchy);
            _methodGenerator = new MethodGenerator(_typeMapper, _hierarchy);

            Console.WriteLine($"**[ INFO ] Code generator initialized for assembly: {assemblyName}");
        }

        public Assembly GetAssembly()
        {
            return _assemblyBuilder;
        }

        /// <summary>
        /// Генерирует .NET сборку из AST программы на языке O.
        /// </summary>
        public Assembly Generate(ProgramNode program)
        {
            Console.WriteLine("**[ INFO ] Phase 1: Declaring types...");
            
            // Фаза 1: Объявление всех типов (без тел методов)
            foreach (var classDecl in program.Classes)
            {
                DeclareType(classDecl);
            }

            Console.WriteLine($"**[ INFO ] Declared {_typeBuilders.Count} types");
            Console.WriteLine("**[ INFO ] Phase 2: Generating type members...");

            // Фаза 2: Генерация членов классов (поля, конструкторы, методы)
            foreach (var classDecl in program.Classes)
            {
                GenerateClassMembers(classDecl);
            }

            Console.WriteLine("**[ INFO ] Phase 3: Finalizing types...");

            // Фаза 3: Завершение создания типов
            foreach (var kvp in _typeBuilders)
            {
                try
                {
                    // Use CreateTypeInfo().AsType() to obtain a runtime Type usable with Activator
                    var completedType = kvp.Value.CreateTypeInfo().AsType();
                    _completedTypes[kvp.Key] = completedType!;
                    Console.WriteLine($"**[ DEBUG ]   Finalized type: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to finalize type '{kvp.Key}': {ex.Message}", ex);
                }
            }

            Console.WriteLine($"**[ OK ] Successfully generated {_completedTypes.Count} types");

            return _assemblyBuilder;
        }

        /// <summary>
        /// Generates a static entry point that invokes Main.this() constructor.
        /// Required for creating standalone .exe files.
        /// </summary>
        public void GenerateEntryPoint()
        {
            if (!_completedTypes.ContainsKey("Main"))
            {
                Console.WriteLine("**[ WARN ] No 'Main' class found; skipping entry point generation");
                return;
            }

            Console.WriteLine("**[ INFO ] Generating entry point for executable...");

            // Create a Program type to hold the static Main method
            var programType = _moduleBuilder.DefineType(
                "<Program>",
                TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Abstract,
                typeof(object));

            // Define static Main(string[] args) method
            var mainMethod = programType.DefineMethod(
                "Main",
                MethodAttributes.Public | MethodAttributes.Static,
                CallingConventions.Standard,
                typeof(void),
                new[] { typeof(string[]) });

            var il = mainMethod.GetILGenerator();

            // Get the user's Main class type
            var mainClassType = _completedTypes["Main"];

            // IL: new Main() -> calls constructor (this())
            var mainConstructor = mainClassType.GetConstructor(Type.EmptyTypes);
            if (mainConstructor != null)
            {
                il.Emit(OpCodes.Newobj, mainConstructor);
                il.Emit(OpCodes.Pop); // Discard the instance
            }
            else
            {
                Console.WriteLine("**[ WARN ] Could not find parameterless constructor for Main");
            }

            il.Emit(OpCodes.Ret);

            // Finalize the Program type
            var programTypeCreated = programType.CreateTypeInfo().AsType();

            // Store the entry point method for later use in SaveToFile
            // (PersistedAssemblyBuilder doesn't have SetEntryPoint, we'll handle this during Save)
            _entryPointMethod = mainMethod;
            Console.WriteLine("**[ OK ] Entry point method registered: <Program>.Main(string[])");
        }

        private MethodBuilder? _entryPointMethod = null;

        /// <summary>
        /// Объявляет тип класса (создаёт TypeBuilder).
        /// </summary>
        private void DeclareType(ClassDeclaration classDecl)
        {
            // Пропускаем встроенные типы - они уже существуют в .NET
            if (_typeMapper.IsBuiltInType(classDecl.Name))
            {
                Console.WriteLine($"**[ DEBUG ]   Skipping built-in type: {classDecl.Name}");
                return;
            }

            // Определяем базовый тип
            Type? baseType = null;
            if (!string.IsNullOrEmpty(classDecl.Extension))
            {
                baseType = _typeMapper.GetNetType(classDecl.Extension);
            }
            else
            {
                baseType = typeof(object);
            }

            // Создаём TypeBuilder для класса
            TypeBuilder typeBuilder;
            
            if (!string.IsNullOrEmpty(classDecl.GenericParameter))
            {
                // Обобщённый класс (например, Box[T])
                typeBuilder = _moduleBuilder.DefineType(
                    classDecl.Name,
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);

                var genericParams = typeBuilder.DefineGenericParameters(classDecl.GenericParameter);
                Console.WriteLine($"**[ DEBUG ]   Declared generic type: {classDecl.Name}<{classDecl.GenericParameter}>");
            }
            else
            {
                // Обычный класс
                typeBuilder = _moduleBuilder.DefineType(
                    classDecl.Name,
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);
                
                Console.WriteLine($"**[ DEBUG ]   Declared type: {classDecl.Name}" + 
                    (baseType != typeof(object) ? $" : {baseType.Name}" : ""));
            }

            _typeBuilders[classDecl.Name] = typeBuilder;
            _typeMapper.RegisterUserType(classDecl.Name, typeBuilder);
        }

        /// <summary>
        /// Генерирует члены класса (поля, конструкторы, методы).
        /// </summary>
        private void GenerateClassMembers(ClassDeclaration classDecl)
        {
            // Пропускаем встроенные типы
            if (_typeMapper.IsBuiltInType(classDecl.Name))
                return;

            if (!_typeBuilders.TryGetValue(classDecl.Name, out var typeBuilder))
            {
                throw new InvalidOperationException($"Type '{classDecl.Name}' not found in type builders");
            }

            Console.WriteLine($"**[ DEBUG ] Generating members for: {classDecl.Name}");

            // Генерация полей
            var fields = new Dictionary<string, FieldBuilder>();
            foreach (var member in classDecl.Members.OfType<VariableDeclaration>())
            {
                var field = GenerateField(typeBuilder, member);
                fields[member.Identifier] = field;
            }

            // Генерация конструкторов
            var hasConstructor = false;
            foreach (var member in classDecl.Members.OfType<ConstructorDeclaration>())
            {
                _methodGenerator.GenerateConstructor(typeBuilder, member, classDecl, fields);
                hasConstructor = true;
            }

            // Если нет явного конструктора, создаём конструктор по умолчанию
            if (!hasConstructor)
            {
                GenerateDefaultConstructor(typeBuilder);
            }

            // Генерация методов
            foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
            {
                _methodGenerator.GenerateMethod(typeBuilder, member, fields, classDecl.Name);
            }
        }

        public void ValidateTypes()
        {
            Console.WriteLine("\n**[ DEBUG ] ========== TYPE VALIDATION ==========");
            
            foreach (var kvp in _completedTypes)
            {
                var type = kvp.Value;
                var typeName = kvp.Key;
                
                Console.WriteLine($"**[ DEBUG ] Type: {typeName}");
                Console.WriteLine($"**[ DEBUG ]   - Full name: {type.FullName}");
                Console.WriteLine($"**[ DEBUG ]   - Type class: {type.GetType().Name}");
                // Console.WriteLine($"**[ DEBUG ]   - Is runtime type: {type is RuntimeType}");
                Console.WriteLine($"**[ DEBUG ]   - Assembly: {type.Assembly.GetName().Name}");
                Console.WriteLine($"**[ DEBUG ]   - Base type: {type.BaseType?.Name ?? "none"}");
                
                var ctors = type.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                Console.WriteLine($"**[ DEBUG ]   - Constructors: {ctors.Length}");
                foreach (var ctor in ctors)
                {
                    var parameters = ctor.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    Console.WriteLine($"**[ DEBUG ]     - {ctor.Name}({paramStr})");
                }
                
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                
                if (methods.Length > 0)
                {
                    Console.WriteLine($"**[ DEBUG ]   - Methods: {methods.Length}");
                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        Console.WriteLine($"**[ DEBUG ]     - {method.ReturnType.Name} {method.Name}({paramStr})");
                    }
                }
                
                var fields = type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                
                if (fields.Length > 0)
                {
                    Console.WriteLine($"**[ DEBUG ]   - Fields: {fields.Length}");
                    foreach (var field in fields)
                    {
                        Console.WriteLine($"**[ DEBUG ]     - {field.FieldType.Name} {field.Name}");
                    }
                }
                
                Console.WriteLine();
            }
            
            Console.WriteLine("**[ DEBUG ] ========== BUILTIN TYPES ==========");
            Console.WriteLine("**[ DEBUG ] The following types are provided by BuiltinTypes runtime:");
            Console.WriteLine("**[ DEBUG ]   - Integer  → BuiltinTypes.OInteger");
            Console.WriteLine("**[ DEBUG ]   - Real     → BuiltinTypes.OReal");
            Console.WriteLine("**[ DEBUG ]   - Boolean  → BuiltinTypes.OBoolean");
            Console.WriteLine("**[ DEBUG ]   - Array[T] → BuiltinTypes.OArray");
            Console.WriteLine("**[ DEBUG ]   - List[T]  → BuiltinTypes.OList");
            Console.WriteLine("**[ DEBUG ] =======================================\n");
        }

        /// <summary>
        /// Генерирует поле класса.
        /// </summary>
        private FieldBuilder GenerateField(TypeBuilder typeBuilder, VariableDeclaration varDecl)
        {
            Type fieldType = _typeMapper.InferType(varDecl.Expression);
            
            var fieldBuilder = typeBuilder.DefineField(
                varDecl.Identifier,
                fieldType,
                FieldAttributes.Private);

            Console.WriteLine($"**[ DEBUG ]   Field: {varDecl.Identifier} : {fieldType.Name}");

            return fieldBuilder;
        }

        /// <summary>
        /// Генерирует конструктор по умолчанию.
        /// </summary>
        private void GenerateDefaultConstructor(TypeBuilder typeBuilder)
        {
            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                Type.EmptyTypes);

            var il = ctorBuilder.GetILGenerator();

            // Вызов конструктора базового класса
            il.Emit(OpCodes.Ldarg_0); // this
            var baseConstructor = typeBuilder.BaseType!.GetConstructor(Type.EmptyTypes);
            il.Emit(OpCodes.Call, baseConstructor!);

            il.Emit(OpCodes.Ret);

            Console.WriteLine($"**[ DEBUG ]   Generated default constructor");
        }

        /// <summary>
        /// Сохраняет сборку в файл (.exe или .dll).
        /// </summary>
        public void SaveToFile(string outputPath, bool asExecutable = false)
        {
            #if NET9_0_OR_GREATER
            Console.WriteLine($"**[ INFO ] Saving assembly to: {outputPath}");
            
            try
            {
                // В .NET 9 PersistedAssemblyBuilder имеет метод Save
                var persistedBuilder = (PersistedAssemblyBuilder)_assemblyBuilder;
                
                var dllPath = Path.ChangeExtension(outputPath, ".dll");
                
                // If this is executable and we have an entry point, we need to set it
                if (asExecutable && _entryPointMethod != null)
                {
                    Console.WriteLine("**[ INFO ] Creating executable with entry point...");

                    // Save the assembly first
                    using (var stream = new FileStream(dllPath, FileMode.Create, FileAccess.Write))
                    {
                        persistedBuilder.Save(stream);
                    }

                    // Try to post-process the saved DLL using Mono.Cecil to set the CLR entry point
                    try
                    {
                        CecilHelpers.SetEntryPointWithCecil(dllPath);
                        Console.WriteLine($"**[ OK ] Entry point set in PE header (via Cecil)");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"**[ WARN ] Could not set entry point via Cecil: {ex.Message}");
                        Console.WriteLine($"**[ INFO ] Assembly saved but entry point not set in PE header");
                        Console.WriteLine($"**[ INFO ] You can still invoke <Program>.Main() via reflection");
                    }
                }
                else
                {
                    using var stream = new FileStream(dllPath, FileMode.Create, FileAccess.Write);
                    persistedBuilder.Save(stream);
                    Console.WriteLine($"**[ OK ] Assembly saved successfully: {dllPath}");
                }

                Console.WriteLine($"**[ INFO ] File size: {new FileInfo(dllPath).Length} bytes");
            }
            catch (NotSupportedException ex)
            {
                Console.WriteLine($"**[ WARN ] {ex.Message}");
                Console.WriteLine($"**[ INFO ] PersistedAssemblyBuilder.Save() may not be fully implemented yet.");
                Console.WriteLine($"**[ INFO ] Try using --run flag to execute in-memory instead.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"**[ ERR ] Failed to save assembly: {ex.Message}");
                if (Environment.GetCommandLineArgs().Contains("--debug"))
                {
                    Console.WriteLine($"**[ DEBUG ] Stack trace:\n{ex.StackTrace}");
                }
                throw;
            }
            #else
            throw new NotSupportedException(
                "Saving assemblies to file requires .NET 9 or higher. " +
                "Current runtime: .NET " + Environment.Version + ". " +
                "Use --run flag to execute in-memory, or upgrade to .NET 9.");
            #endif
        }

        /// <summary>
        /// Post-processes a PE file to set the entry point in the header.
        /// This is needed because PersistedAssemblyBuilder doesn't provide a direct way to set the entry point.
        /// </summary>
        private void SetEntryPointInPEFile(string peFilePath, MethodBuilder entryPointMethod)
        {
            // The entry point in a managed executable is set via the CLR Runtime Header
            // within the PE file. For a managed method, we need to:
            // 1. Get the method's metadata token
            // 2. Write that token to the appropriate location in the PE file
            
            // Get the method's metadata token
            int methodToken = entryPointMethod.MetadataToken;
            Console.WriteLine($"**[ DEBUG ] Entry point method token: 0x{methodToken:X8}");
            
            // Read the current PE file
            byte[] peData = File.ReadAllBytes(peFilePath);
            
            // PE header structure:
            // Offset 0x3C: Offset to PE header (int32)
            // At PE header: "PE\0\0" signature
            // After signature: COFF header
            // After COFF header: Optional header
            
            try
            {
                // Parse PE headers manually (we already have the full bytes in peData)
                int peHeaderOffset = BitConverter.ToInt32(peData, 0x3C);
                // COFF header starts at peHeaderOffset + 4
                int numberOfSections = BitConverter.ToUInt16(peData, peHeaderOffset + 6);
                int sizeOfOptionalHeader = BitConverter.ToUInt16(peData, peHeaderOffset + 20);
                int optionalHeaderStart = peHeaderOffset + 4 + 20;

                // Determine PE magic (PE32 = 0x10b, PE32+ = 0x20b)
                ushort magic = BitConverter.ToUInt16(peData, optionalHeaderStart);
                int dataDirectoryStart = optionalHeaderStart + (magic == 0x10b ? 96 : 112);
                const int IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR = 14;
                int comDirOffset = dataDirectoryStart + IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR * 8; // each dir is 8 bytes
                if (comDirOffset + 8 > peData.Length)
                {
                    Console.WriteLine("**[ WARN ] PE seems malformed; cannot locate COM descriptor");
                    return;
                }

                int corRva = BitConverter.ToInt32(peData, comDirOffset);
                int corSize = BitConverter.ToInt32(peData, comDirOffset + 4);

                // Section table starts after optional header
                int sectionTableOffset = optionalHeaderStart + sizeOfOptionalHeader;
                int corFileOffset = -1;
                for (int i = 0; i < numberOfSections; i++)
                {
                    int secOffset = sectionTableOffset + i * 40;
                    int virtualSize = BitConverter.ToInt32(peData, secOffset + 8);
                    int virtualAddress = BitConverter.ToInt32(peData, secOffset + 12);
                    int sizeOfRaw = BitConverter.ToInt32(peData, secOffset + 16);
                    int pointerToRaw = BitConverter.ToInt32(peData, secOffset + 20);

                    int sectionVirtualSize = Math.Max(virtualSize, sizeOfRaw);
                    if (corRva >= virtualAddress && corRva < virtualAddress + sectionVirtualSize)
                    {
                        corFileOffset = pointerToRaw + (corRva - virtualAddress);
                        break;
                    }
                }

                if (corFileOffset < 0)
                {
                    Console.WriteLine("**[ WARN ] Could not map CLR header RVA to file offset");
                    return;
                }

                // IMAGE_COR20_HEADER: EntryPoint token is at offset 0x18 from start of COR header
                int entryPointOffset = corFileOffset + 0x18;
                if (entryPointOffset + 4 > peData.Length)
                {
                    Console.WriteLine("**[ WARN ] CLR header appears truncated; cannot write entry point");
                    return;
                }

                // Use PEReader/MetadataReader to find the MethodDef token for Main/main
                using var ms2 = new MemoryStream(peData, writable: false);
                using var peReader2 = new System.Reflection.PortableExecutable.PEReader(ms2);
                var mdReader = peReader2.GetMetadataReader();

                int metadataMethodToken = 0;

                foreach (var typeHandle in mdReader.TypeDefinitions)
                {
                    var typeDef = mdReader.GetTypeDefinition(typeHandle);
                    var typeName = mdReader.GetString(typeDef.Name);
                    if (typeName == "<Program>" || typeName == "Program" || typeName == "Main")
                    {
                        foreach (var mh in typeDef.GetMethods())
                        {
                            var methodDef = mdReader.GetMethodDefinition(mh);
                            var name = mdReader.GetString(methodDef.Name);
                            if (name == "Main" || name == "main")
                            {
                                metadataMethodToken = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(mh);
                                break;
                            }
                        }
                    }

                    if (metadataMethodToken != 0)
                        break;
                }

                if (metadataMethodToken == 0)
                {
                    // fallback: search all methods
                    foreach (var mh in mdReader.MethodDefinitions)
                    {
                        var methodDef = mdReader.GetMethodDefinition(mh);
                        var name = mdReader.GetString(methodDef.Name);
                        if (name == "Main" || name == "main")
                        {
                            metadataMethodToken = System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(mh);
                            break;
                        }
                    }
                }

                if (metadataMethodToken == 0)
                {
                    Console.WriteLine("**[ WARN ] Could not find Main/main method in metadata; skipping entry point write");
                    return;
                }

                Console.WriteLine($"**[ DEBUG ] Writing entry point method token: 0x{metadataMethodToken:X8} at offset 0x{entryPointOffset:X}");
                var tokenBytes = BitConverter.GetBytes(metadataMethodToken);
                Array.Copy(tokenBytes, 0, peData, entryPointOffset, 4);
                File.WriteAllBytes(peFilePath, peData);
                Console.WriteLine("**[ OK ] Entry point token written to PE file");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"**[ WARN ] Error modifying PE header: {ex.Message}");
            }
        }

        /// <summary>
        /// Получает завершённый тип по имени.
        /// </summary>
        public Type? GetCompletedType(string typeName)
        {
            return _completedTypes.TryGetValue(typeName, out var type) ? type : null;
        }

        /// <summary>
        /// Возвращает все сгенерированные типы.
        /// </summary>
        public IReadOnlyDictionary<string, Type> GetAllTypes()
        {
            return _completedTypes;
        }
    }
}
