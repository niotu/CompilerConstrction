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
        public void SaveToFile(string outputPath)
        {
            #if NET9_0_OR_GREATER
            Console.WriteLine($"**[ INFO ] Saving assembly to: {outputPath}");
            
            try
            {
                // В .NET 9 PersistedAssemblyBuilder имеет метод Save
                var persistedBuilder = (PersistedAssemblyBuilder)_assemblyBuilder;
                
                using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
                
                // Простой метод сохранения
                persistedBuilder.Save(stream);

                Console.WriteLine($"**[ OK ] Assembly saved successfully: {outputPath}");
                Console.WriteLine($"**[ INFO ] File size: {new FileInfo(outputPath).Length} bytes");
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
