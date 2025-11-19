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
        private readonly MethodGenerator _methodGenerator;
        private readonly Dictionary<string, TypeBuilder> _typeBuilders;
        private readonly Dictionary<string, Type> _completedTypes;
        private readonly Dictionary<string, ConstructorBuilder> _defaultConstructors; // НОВОЕ
        private readonly ClassHierarchy _hierarchy;
        private readonly string _assemblyName;

        public CodeGenerator(string assemblyName, ClassHierarchy hierarchy)
        {
            _assemblyName = assemblyName;
            _hierarchy = hierarchy;
            _typeBuilders = new Dictionary<string, TypeBuilder>();
            _completedTypes = new Dictionary<string, Type>();

            var assemblyNameObj = new AssemblyName(assemblyName);
            
            // Используем AssemblyBuilderAccess.Run для возможности выполнения
            // Это даёт нам настоящие runtime типы, а не TypeBuilder
            _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
                assemblyNameObj,
                AssemblyBuilderAccess.Run);

            _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
            _typeMapper = new TypeMapper(_moduleBuilder, _hierarchy);
            _methodGenerator = new MethodGenerator(_typeMapper, _hierarchy);

            Console.WriteLine($"**[ INFO ] Code generator initialized for assembly: {assemblyName}");
            Console.WriteLine($"**[ INFO ] Using BuiltinTypes for runtime support");
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

            Console.WriteLine($"**[ INFO ] Declared {_typeBuilders.Count} user types");
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
                    // CreateType() возвращает RuntimeType в AssemblyBuilder с Access.Run
                    Type completedType = kvp.Value.CreateType()!;
                    
                    if (completedType == null)
                    {
                        throw new InvalidOperationException($"CreateType() returned null for '{kvp.Key}'");
                    }
                    
                    // Проверяем, что получили runtime тип
                    if (completedType.GetType().Name.Contains("Builder"))
                    {
                        Console.WriteLine($"**[ WARN ] Type '{kvp.Key}' is still a builder, attempting recovery...");
                        
                        // Пытаемся загрузить через Assembly.GetType()
                        var runtimeType = _assemblyBuilder.GetType(kvp.Key);
                        if (runtimeType != null && !runtimeType.GetType().Name.Contains("Builder"))
                        {
                            completedType = runtimeType;
                            Console.WriteLine($"**[ INFO ] Successfully recovered runtime type for '{kvp.Key}'");
                        }
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

        /// <summary>
        /// Объявляет тип класса (создаёт TypeBuilder).
        /// </summary>
        private void DeclareType(ClassDeclaration classDecl)
        {
            // Пропускаем встроенные типы - они реализованы в BuiltinTypes
            if (_typeMapper.IsBuiltInType(classDecl.Name))
            {
                Console.WriteLine($"**[ DEBUG ]   Skipping built-in type: {classDecl.Name} (provided by BuiltinTypes)");
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
                var defaultCtor = GenerateDefaultConstructor(typeBuilder);
                _defaultConstructors[classDecl.Name] = defaultCtor; // НОВОЕ: Сохраняем
            }

            // Генерация методов
            foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
            {
                _methodGenerator.GenerateMethod(typeBuilder, member, fields);
            }
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
        private ConstructorBuilder GenerateDefaultConstructor(TypeBuilder typeBuilder)
        {
            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                Type.EmptyTypes);

            var il = ctorBuilder.GetILGenerator();

            // Вызов конструктора базового класса
            il.Emit(OpCodes.Ldarg_0); // this

            ConstructorInfo? baseConstructor = null;

            // ИСПРАВЛЕНИЕ: Получаем конструктор базового класса
            if (typeBuilder.BaseType == typeof(object))
            {
                // Базовый класс - object, используем его конструктор
                baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
            }
            else if (typeBuilder.BaseType is TypeBuilder baseTypeBuilder)
            {
                // Базовый класс - ещё не завершённый TypeBuilder
                // Ищем его сохранённый конструктор
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
                // Базовый класс - обычный Type
                baseConstructor = typeBuilder.BaseType.GetConstructor(Type.EmptyTypes);
            }

            if (baseConstructor == null)
            {
                throw new InvalidOperationException(
                    $"Parameterless constructor not found for base type '{typeBuilder.BaseType?.Name}'");
            }

            il.Emit(OpCodes.Call, baseConstructor);
            il.Emit(OpCodes.Ret);

            Console.WriteLine($"**[ DEBUG ]   Generated default constructor");

            return ctorBuilder;
        }


        /// <summary>
        /// Сохраняет сборку в файл (.exe или .dll).
        /// ПРИМЕЧАНИЕ: В режиме AssemblyBuilderAccess.Run сохранение в файл невозможно.
        /// </summary>
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

        /// <summary>
        /// Проверяет корректность всех сгенерированных типов.
        /// Используется для отладки.
        /// </summary>
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
                
                // Проверяем конструкторы
                var ctors = type.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                Console.WriteLine($"**[ DEBUG ]   - Constructors: {ctors.Length}");
                foreach (var ctor in ctors)
                {
                    var parameters = ctor.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    Console.WriteLine($"**[ DEBUG ]     - {ctor.Name}({paramStr})");
                }
                
                // Проверяем методы
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
                
                // Проверяем поля
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
        /// Получает информацию о сборке для диагностики.
        /// </summary>
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

    /// <summary>
    /// Информация о сгенерированной сборке.
    /// </summary>
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
