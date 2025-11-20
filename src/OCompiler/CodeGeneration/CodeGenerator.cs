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
        private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _typeFields;
        private readonly ClassHierarchy _hierarchy;
        private readonly string _assemblyName;

        public CodeGenerator(string assemblyName, ClassHierarchy hierarchy)
        {
            _assemblyName = assemblyName;
            _hierarchy = hierarchy;
            _typeBuilders = new Dictionary<string, TypeBuilder>();
            _completedTypes = new Dictionary<string, Type>();
            _typeFields = new Dictionary<string, Dictionary<string, FieldBuilder>>();

            var assemblyNameObj = new AssemblyName(assemblyName);
            
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
            Console.WriteLine("**[ INFO ] Phase 1: Declaring types, fields, constructors and methods...");
            
            // Передаём TypeBuilders в MethodGenerator для использования во время генерации IL
            _methodGenerator.SetTypeBuilders(_typeBuilders);
            
            // Фаза 1: Все определения и IL генерирование ДО CreateType
            foreach (var classDecl in program.Classes)
            {
                GenerateTypeComplete(classDecl);
            }

            Console.WriteLine($"**[ INFO ] Declared and generated {_typeBuilders.Count} user types");
            Console.WriteLine("**[ INFO ] Phase 2: Creating runtime types...");

            // Фаза 2: CreateType для всех
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
                    _typeMapper.RegisterUserType(kvp.Key, completedType);
                    
                    Console.WriteLine($"**[ DEBUG ]   Created type: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to create type '{kvp.Key}': {ex.Message}", ex);
                }
            }

            Console.WriteLine($"**[ OK ] Successfully generated {_completedTypes.Count} types");
            Console.WriteLine($"**[ INFO ] Built-in types provided by BuiltinTypes runtime");

            return _assemblyBuilder;
        }

        /// <summary>
        /// Полная генерация типа: всё ДО CreateType (DefineType, DefineField, DefineConstructor, DefineMethod, IL).
        /// </summary>
        private void GenerateTypeComplete(ClassDeclaration classDecl)
        {
            // Пропускаем встроенные типы
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

            // Создаём TypeBuilder
            TypeBuilder typeBuilder;
            
            if (!string.IsNullOrEmpty(classDecl.GenericParameter))
            {
                typeBuilder = _moduleBuilder.DefineType(
                    classDecl.Name,
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);

                var genericParams = typeBuilder.DefineGenericParameters(classDecl.GenericParameter);
                Console.WriteLine($"**[ DEBUG ]   Declared generic type: {classDecl.Name}<{classDecl.GenericParameter}>");
            }
            else
            {
                typeBuilder = _moduleBuilder.DefineType(
                    classDecl.Name,
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);
                
                Console.WriteLine($"**[ DEBUG ]   Declared type: {classDecl.Name}");
            }

            _typeBuilders[classDecl.Name] = typeBuilder;
            _typeMapper.RegisterUserType(classDecl.Name, typeBuilder);

            // Добавляем поля
            var fields = new Dictionary<string, FieldBuilder>();
            foreach (var member in classDecl.Members.OfType<VariableDeclaration>())
            {
                Type fieldType = _typeMapper.InferType(member.Expression);
                
                var fieldBuilder = typeBuilder.DefineField(
                    member.Identifier,
                    fieldType,
                    FieldAttributes.Private);

                fields[member.Identifier] = fieldBuilder;
                Console.WriteLine($"**[ DEBUG ]   Field: {member.Identifier} : {fieldType.Name}");
            }
            
            _typeFields[classDecl.Name] = fields;

            // Генерируем конструкторы
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

            // Генерируем методы
            foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
            {
                try
                {
                    if (member.Header == null)
                    {
                        Console.WriteLine($"**[ WARN ] Method has null header, skipping");
                        continue;
                    }
                    if (member.Body == null)
                    {
                        Console.WriteLine($"**[ DEBUG ]   Skipping method '{member.Header.Name}' (no body)");
                        continue;
                    }
                    _methodGenerator.GenerateMethod(typeBuilder, member, fields);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"**[ ERR ] Failed to generate method '{member.Header?.Name}': {ex.Message}");
                    throw;
                }
            }
        }


        /// <summary>
        /// Генерирует конструктор по умолчанию (всё ДО CreateType).
        /// </summary>
        private void GenerateDefaultConstructor(TypeBuilder typeBuilder)
        {
            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                Type.EmptyTypes);

            // Сохраняем конструктор
            _methodGenerator.RegisterConstructor(typeBuilder.Name, Type.EmptyTypes, ctorBuilder);

            var il = ctorBuilder.GetILGenerator();

            // Вызов конструктора базового класса
            il.Emit(OpCodes.Ldarg_0); // this

            ConstructorInfo? baseConstructor = null;

            if (typeBuilder.BaseType == typeof(object))
            {
                baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
            }
            else
            {
                try
                {
                    baseConstructor = typeBuilder.BaseType!.GetConstructor(Type.EmptyTypes);
                }
                catch (NotSupportedException)
                {
                    baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
                }
            }

            if (baseConstructor == null)
            {
                throw new InvalidOperationException(
                    $"Parameterless constructor not found for base type '{typeBuilder.BaseType?.Name}'");
            }

            il.Emit(OpCodes.Call, baseConstructor);
            il.Emit(OpCodes.Ret);

            Console.WriteLine($"**[ DEBUG ]   Generated default constructor");
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
