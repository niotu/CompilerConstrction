using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OCompiler.Parser;
using OCompiler.Semantic;

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Главный класс генератора кода для языка O.
    /// Генерирует .NET сборки из AST с использованием System.Reflection.Emit.
    /// Использует BuiltinTypes для реализации встроенных методов.
    /// </summary>
    public class CodeGenerator
    {
        private readonly AssemblyBuilder _assemblyBuilder;
        private readonly ModuleBuilder _moduleBuilder;
        private readonly TypeMapper _typeMapper;
        private MethodGenerator _methodGenerator;
        private readonly Dictionary<string, TypeBuilder> _typeBuilders;
        private readonly Dictionary<string, Type> _completedTypes;
        private readonly Dictionary<string, ConstructorBuilder> _defaultConstructors;
        private readonly ClassHierarchy _hierarchy;
        private readonly string _assemblyName;

        public CodeGenerator(string assemblyName, ClassHierarchy hierarchy)
        {
            _assemblyName = assemblyName;
            _hierarchy = hierarchy;
            _typeBuilders = new Dictionary<string, TypeBuilder>();
            _completedTypes = new Dictionary<string, Type>();
            _defaultConstructors = new Dictionary<string, ConstructorBuilder>();

            var assemblyNameObj = new AssemblyName(assemblyName);
            
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                assemblyNameObj,
                AssemblyBuilderAccess.Run);

            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
            _typeMapper = new TypeMapper(_moduleBuilder, _hierarchy);
            _methodGenerator = null!; // Инициализируется в Generate()

            Console.WriteLine($"**[ INFO ] Code generator initialized for assembly: {assemblyName}");
            Console.WriteLine($"**[ INFO ] Using BuiltinTypes for runtime support");
        }

        public Assembly Generate(ProgramNode program)
        {
            // Инициализируем MethodGenerator с AST
            _methodGenerator = new MethodGenerator(_typeMapper, _hierarchy, this, program);

            Console.WriteLine("**[ INFO ] Phase 1: Declaring types, fields, constructors and methods...");
            
            var sortedClasses = SortByInheritance(program.Classes);

            foreach (var classDecl in sortedClasses)
            {
                GenerateTypeComplete(classDecl);
            }

            Console.WriteLine("**[ INFO ] Phase 2: Finalizing types...");

            foreach (var kvp in _typeBuilders)
            {
                try
                {
                    Type completedType = kvp.Value.CreateType()!;
                    
                    if (completedType == null)
                    {
                        throw new InvalidOperationException($"CreateType() returned null for '{kvp.Key}'");
                    }
                    
                    _completedTypes[kvp.Key] = completedType;
                    
                    Console.WriteLine($"**[ DEBUG ]   Finalized type: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to finalize type '{kvp.Key}': {ex.Message}", ex);
                }
            }

            Console.WriteLine($"**[ OK ] Successfully generated {_completedTypes.Count} types");
            Console.WriteLine($"**[ INFO ] Built-in types provided by BuiltinTypes runtime");

            return _assemblyBuilder;
        }

        private List<ClassDeclaration> SortByInheritance(List<ClassDeclaration> classes)
        {
            var sorted = new List<ClassDeclaration>();
            var visited = new HashSet<string>();

            void Visit(ClassDeclaration classDecl)
            {
                if (visited.Contains(classDecl.Name))
                    return;

                if (!string.IsNullOrEmpty(classDecl.Extension))
                {
                    var baseClass = classes.FirstOrDefault(c => c.Name == classDecl.Extension);
                    if (baseClass != null)
                    {
                        Visit(baseClass);
                    }
                }

                sorted.Add(classDecl);
                visited.Add(classDecl.Name);
            }

            foreach (var classDecl in classes)
            {
                Visit(classDecl);
            }

            return sorted;
        }

        private void GenerateTypeComplete(ClassDeclaration classDecl)
        {
            if (_typeMapper.IsBuiltInType(classDecl.Name))
            {
                Console.WriteLine($"**[ DEBUG ]   Skipping built-in type: {classDecl.Name} (provided by BuiltinTypes)");
                return;
            }

            Type? baseType = null;
            if (!string.IsNullOrEmpty(classDecl.Extension))
            {
                baseType = _typeMapper.GetNetType(classDecl.Extension);
            }
            else
            {
                baseType = typeof(object);
            }

            TypeBuilder typeBuilder;
            
            if (!string.IsNullOrEmpty(classDecl.GenericParameter))
            {
                typeBuilder = _moduleBuilder.DefineType(
                    classDecl.Name,
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);

                typeBuilder.DefineGenericParameters(classDecl.GenericParameter);
                Console.WriteLine($"**[ DEBUG ]   Declared type: {classDecl.Name}<{classDecl.GenericParameter}>");
            }
            else
            {
                typeBuilder = _moduleBuilder.DefineType(
                    classDecl.Name,
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);
                
                Console.WriteLine($"**[ DEBUG ]   Declared type: {classDecl.Name}" + 
                    (baseType != typeof(object) ? $" : {baseType.Name}" : ""));
            }

            _typeBuilders[classDecl.Name] = typeBuilder;
            _typeMapper.RegisterUserType(classDecl.Name, typeBuilder);

            var fields = new Dictionary<string, FieldBuilder>();
            foreach (var member in classDecl.Members.OfType<VariableDeclaration>())
            {
                var field = GenerateField(typeBuilder, member);
                fields[member.Identifier] = field;
            }

            var hasConstructor = false;
            foreach (var member in classDecl.Members.OfType<ConstructorDeclaration>())
            {
                _methodGenerator.GenerateConstructor(typeBuilder, member, classDecl, fields);
                hasConstructor = true;
            }

            if (!hasConstructor)
            {
                var defaultCtor = GenerateDefaultConstructor(typeBuilder);
                _defaultConstructors[classDecl.Name] = defaultCtor;
            }

            foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
            {
                _methodGenerator.GenerateMethod(typeBuilder, member, fields, classDecl.Name);
            }
        }

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

        private ConstructorBuilder GenerateDefaultConstructor(TypeBuilder typeBuilder)
        {
            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                Type.EmptyTypes);

            var il = ctorBuilder.GetILGenerator();

            il.Emit(OpCodes.Ldarg_0);

            ConstructorInfo? baseConstructor = null;

            if (typeBuilder.BaseType == typeof(object))
            {
                baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
            }
            else if (typeBuilder.BaseType is TypeBuilder baseTypeBuilder)
            {
                var baseTypeName = baseTypeBuilder.Name;
                
                if (_defaultConstructors.TryGetValue(baseTypeName, out var savedBaseCtor))
                {
                    baseConstructor = savedBaseCtor;
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Default constructor for base type '{baseTypeName}' not found. " +
                        $"Ensure base classes are generated before derived classes.");
                }
            }
            else
            {
                baseConstructor = typeBuilder.BaseType.GetConstructor(Type.EmptyTypes);
            }

            if (baseConstructor == null)
            {
                throw new InvalidOperationException(
                    $"Parameterless constructor not found for base type '{typeBuilder.BaseType?.Name}'");
            }

            il.Emit(OpCodes.Call, baseConstructor);
            il.Emit(OpCodes.Ret);

            Console.WriteLine($"**[ DEBUG ]   Constructor: this()");

            return ctorBuilder;
        }

        public void SaveToFile(string outputPath)
        {
            throw new NotSupportedException(
                "Saving assemblies to file is not supported in Run mode.\n" +
                "AssemblyBuilderAccess.Run allows execution but not persistence.\n" +
                "To save assemblies:\n" +
                "  1. Use .NET 9+ with PersistedAssemblyBuilder (currently in preview)\n" +
                "  2. Or use Roslyn (Microsoft.CodeAnalysis.CSharp) to generate C# code\n" +
                "  3. Or use MetadataLoadContext for low-level PE file generation\n" +
                "\n" +
                "Current mode supports --run flag for in-memory execution.");
        }

        public Type? GetCompletedType(string typeName)
        {
            return _completedTypes.TryGetValue(typeName, out var type) ? type : null;
        }

        public IReadOnlyDictionary<string, Type> GetAllTypes()
        {
            return _completedTypes;
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

        public AssemblyInfo GetAssemblyInfo()
        {
            return new AssemblyInfo
            {
                Name = _assemblyName,
                TypeCount = _completedTypes.Count,
                BuiltInTypesUsed = true,
                IsExecutable = true,
                IsPersistable = false,
                RuntimeMode = "AssemblyBuilderAccess.Run"
            };
        }
    }

    public class AssemblyInfo
    {
        public string Name { get; set; } = "";
        public int TypeCount { get; set; }
        public bool BuiltInTypesUsed { get; set; }
        public bool IsExecutable { get; set; }
        public bool IsPersistable { get; set; }
        public string RuntimeMode { get; set; } = "";

        public override string ToString()
        {
            return $"Assembly: {Name}\n" +
                   $"  Types: {TypeCount}\n" +
                   $"  Built-in types: {(BuiltInTypesUsed ? "Yes (BuiltinTypes)" : "No")}\n" +
                   $"  Executable: {(IsExecutable ? "Yes (--run)" : "No")}\n" +
                   $"  Persistable: {(IsPersistable ? "Yes (can save to file)" : "No (memory only)")}\n" +
                   $"  Mode: {RuntimeMode}";
        }
    }
}
