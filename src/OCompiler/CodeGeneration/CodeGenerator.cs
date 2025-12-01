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
        private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _classFields;
        
        private static bool IsDebugMode => Environment.GetCommandLineArgs().Contains("--debug");
        
        private static void DebugLog(string message)
        {
            if (IsDebugMode)
            {
                Console.WriteLine(message);
            }
        }

        public CodeGenerator(string assemblyName, ClassHierarchy hierarchy)
        {
            _assemblyName = assemblyName;
            _hierarchy = hierarchy;
            _typeBuilders = new Dictionary<string, TypeBuilder>();
            _completedTypes = new Dictionary<string, Type>();
            _classFields = new Dictionary<string, Dictionary<string, FieldBuilder>>();

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
            DebugLog("**[ DEBUG ] Phase 1: Declaring types...");
            
            // Фаза 1: Объявление всех типов (без тел методов)
            foreach (var classDecl in program.Classes)
            {
                DeclareType(classDecl);
            }

            DebugLog($"**[ DEBUG ] Declared {_typeBuilders.Count} types");
            DebugLog("**[ DEBUG ] Phase 2: Generating type members...");

            // Фаза 2: Генерация членов классов (поля, конструкторы, методы)
            foreach (var classDecl in program.Classes)
            {
                GenerateClassMembers(classDecl);
            }

            DebugLog("**[ DEBUG ] Phase 3: Finalizing types...");

            // Фаза 3: Завершение создания типов
            foreach (var kvp in _typeBuilders)
            {
                try
                {
                    // Use CreateTypeInfo().AsType() to obtain a runtime Type usable with Activator
                    var completedType = kvp.Value.CreateTypeInfo().AsType();
                    _completedTypes[kvp.Key] = completedType!;
                    DebugLog($"**[ DEBUG ]   Finalized type: {kvp.Key}");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to finalize type '{kvp.Key}': {ex.Message}", ex);
                }
            }

            DebugLog($"**[ DEBUG ] Successfully generated {_completedTypes.Count} types");

            return _assemblyBuilder;
        }

        /// <summary>
        /// Generates a static entry point that invokes Main.this() constructor.
        /// Required for creating standalone .exe files.
        /// </summary>
        public void GenerateEntryPoint(string? entryPointClassName = null)
        {
            // По умолчанию используем класс Main
            string entryClassName = entryPointClassName ?? "Main";
            
            if (!_completedTypes.ContainsKey(entryClassName))
            {
                Console.WriteLine($"**[ WARN ] No '{entryClassName}' class found; skipping entry point generation");
                return;
            }

            Console.WriteLine($"**[ INFO ] Generating entry point for executable (entry class: {entryClassName})...");

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

            // Get the user's entry class type
            var entryClassType = _completedTypes[entryClassName];

            // Точка входа - это конструктор {entryClassName}.this()
            // Просто создаём экземпляр класса, конструктор выполнится автоматически
            var entryConstructor = entryClassType.GetConstructor(Type.EmptyTypes);

            if (entryConstructor == null)
            {
                Console.WriteLine($"**[ WARN ] No parameterless constructor found in class {entryClassName}; entry point will be empty");
                il.Emit(OpCodes.Ret);
            }
            else
            {
                // Создаём экземпляр entry класса, конструктор this() выполнится автоматически
                il.Emit(OpCodes.Newobj, entryConstructor);
                il.Emit(OpCodes.Pop); // Убираем созданный экземпляр со стека
                il.Emit(OpCodes.Ret);
            }

            // Finalize the Program type
            var programTypeCreated = programType.CreateTypeInfo().AsType();

            // Store the entry point method for later use in SaveToFile
            // (PersistedAssemblyBuilder doesn't have SetEntryPoint, we'll handle this during Save)
            _entryPointMethod = mainMethod;
            Console.WriteLine($"**[ OK ] Entry point method registered: <Program>.Main(string[]) → {entryClassName}.this()");
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
                DebugLog($"**[ DEBUG ]   Skipping built-in type: {classDecl.Name}");
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
                DebugLog($"**[ DEBUG ]   Declared generic type: {classDecl.Name}<{classDecl.GenericParameter}>");
            }
            else
            {
                // Обычный класс
                typeBuilder = _moduleBuilder.DefineType(
                    classDecl.Name,
                    TypeAttributes.Public | TypeAttributes.Class,
                    baseType);
                
                DebugLog($"**[ DEBUG ]   Declared type: {classDecl.Name}" + 
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

            DebugLog($"**[ DEBUG ] Generating members for: {classDecl.Name}");

            // Генерация полей
            var fields = new Dictionary<string, FieldBuilder>();
            foreach (var member in classDecl.Members.OfType<VariableDeclaration>())
            {
                var field = GenerateField(typeBuilder, member);
                fields[member.Identifier] = field;
            }
            
            // Сохраняем поля текущего класса для использования в производных классах
            _classFields[classDecl.Name] = fields;
            
            // Собираем поля из базовых классов для доступа в методах производного класса
            var allAccessibleFields = CollectAllAccessibleFields(classDecl.Name, fields);
            
            // Регистрируем все доступные поля (включая из базовых классов)
            _methodGenerator.RegisterFields(classDecl.Name, allAccessibleFields);

            // Сначала регистрируем все методы (создаём сигнатуры)
            // чтобы они были доступны при генерации конструкторов
            // Пропускаем forward declarations (без тела)
            foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
            {
                if (member.Body != null)
                {
                    _methodGenerator.DeclareMethod(typeBuilder, member, classDecl.Name);
                }
            }

            // Объявляем конструкторы (создаём ConstructorBuilder, регистрируем, но не генерируем тела)
            var constructorBuilders = new List<(ConstructorBuilder, ConstructorDeclaration)>();
            foreach (var member in classDecl.Members.OfType<ConstructorDeclaration>())
            {
                var ctorBuilder = _methodGenerator.DeclareConstructor(typeBuilder, member, classDecl.Name);
                constructorBuilders.Add((ctorBuilder, member));
            }

            // Если нет явного конструктора, создаём конструктор по умолчанию
            if (constructorBuilders.Count == 0)
            {
                GenerateDefaultConstructor(typeBuilder, classDecl, allAccessibleFields);
            }
            else
            {
                // Генерируем тела конструкторов
                foreach (var (ctorBuilder, ctorDecl) in constructorBuilders)
                {
                    _methodGenerator.GenerateConstructorBody(typeBuilder, ctorBuilder, ctorDecl, classDecl, allAccessibleFields);
                }
            }

            // Генерация тел методов (пропускаем forward declarations без тела)
            foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
            {
                if (member.Body != null)
                {
                    _methodGenerator.GenerateMethodBody(typeBuilder, member, allAccessibleFields, classDecl.Name);
                }
            }
        }

        public void ValidateTypes()
        {
            Console.WriteLine("\n**[ DEBUG ] ========== TYPE VALIDATION ==========");
            
            foreach (var kvp in _completedTypes)
            {
                var type = kvp.Value;
                var typeName = kvp.Key;
                
                DebugLog($"**[ DEBUG ] Type: {typeName}");
                DebugLog($"**[ DEBUG ]   - Full name: {type.FullName}");
                DebugLog($"**[ DEBUG ]   - Type class: {type.GetType().Name}");
                // DebugLog($"**[ DEBUG ]   - Is runtime type: {type is RuntimeType}");
                DebugLog($"**[ DEBUG ]   - Assembly: {type.Assembly.GetName().Name}");
                DebugLog($"**[ DEBUG ]   - Base type: {type.BaseType?.Name ?? "none"}");
                
                var ctors = type.GetConstructors(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                
                DebugLog($"**[ DEBUG ]   - Constructors: {ctors.Length}");
                foreach (var ctor in ctors)
                {
                    var parameters = ctor.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                    DebugLog($"**[ DEBUG ]     - {ctor.Name}({paramStr})");
                }
                
                var methods = type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                
                if (methods.Length > 0)
                {
                    DebugLog($"**[ DEBUG ]   - Methods: {methods.Length}");
                    foreach (var method in methods)
                    {
                        var parameters = method.GetParameters();
                        var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        DebugLog($"**[ DEBUG ]     - {method.ReturnType.Name} {method.Name}({paramStr})");
                    }
                }
                
                var fields = type.GetFields(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                
                if (fields.Length > 0)
                {
                    DebugLog($"**[ DEBUG ]   - Fields: {fields.Length}");
                    foreach (var field in fields)
                    {
                        DebugLog($"**[ DEBUG ]     - {field.FieldType.Name} {field.Name}");
                    }
                }
                
                Console.WriteLine();
            }
            
            DebugLog("**[ DEBUG ] ========== BUILTIN TYPES ==========");
            DebugLog("**[ DEBUG ] The following types are provided by BuiltinTypes runtime:");
            DebugLog("**[ DEBUG ]   - Integer  → BuiltinTypes.OInteger");
            DebugLog("**[ DEBUG ]   - Real     → BuiltinTypes.OReal");
            DebugLog("**[ DEBUG ]   - Boolean  → BuiltinTypes.OBoolean");
            DebugLog("**[ DEBUG ]   - Array[T] → BuiltinTypes.OArray");
            DebugLog("**[ DEBUG ]   - List[T]  → BuiltinTypes.OList");
            DebugLog("**[ DEBUG ] =======================================\n");
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
                FieldAttributes.Public); // Поля публичные для доступа на чтение

            DebugLog($"**[ DEBUG ]   Field: {varDecl.Identifier} : {fieldType.Name}");

            return fieldBuilder;
        }

        /// <summary>
        /// Собирает все доступные поля включая поля базовых классов
        /// </summary>
        private Dictionary<string, FieldBuilder> CollectAllAccessibleFields(string className, Dictionary<string, FieldBuilder> ownFields)
        {
            var allFields = new Dictionary<string, FieldBuilder>(ownFields);
            
            // Получаем базовый класс
            var classDecl = _hierarchy.GetClass(className);
            if (classDecl != null && !string.IsNullOrEmpty(classDecl.Extension))
            {
                var baseClassName = classDecl.Extension;
                DebugLog($"**[ DEBUG ]   Collecting fields from base class: {baseClassName}");
                
                // Рекурсивно собираем поля базовых классов
                if (_classFields.TryGetValue(baseClassName, out var baseFields))
                {
                    DebugLog($"**[ DEBUG ]   Found {baseFields.Count} fields in base class {baseClassName}");
                    foreach (var kvp in baseFields)
                    {
                        // Не перезаписываем, если в производном классе есть поле с таким же именем
                        if (!allFields.ContainsKey(kvp.Key))
                        {
                            allFields[kvp.Key] = kvp.Value;
                            DebugLog($"**[ DEBUG ]   Added base field: {kvp.Key}");
                        }
                    }
                }
                else
                {
                    Console.WriteLine($"**[ WARN ]   Base class {baseClassName} has no registered fields");
                }
            }
            
            return allFields;
        }

        /// <summary>
        /// Генерирует конструктор по умолчанию с инициализацией полей.
        /// </summary>
        private void GenerateDefaultConstructor(TypeBuilder typeBuilder, ClassDeclaration classDecl, Dictionary<string, FieldBuilder> fields)
        {
            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                Type.EmptyTypes);

            var il = ctorBuilder.GetILGenerator();

            // Вызов конструктора базового класса
            il.Emit(OpCodes.Ldarg_0); // this
            
            ConstructorInfo? baseConstructor = null;
            
            // Если базовый тип - TypeBuilder, ищем зарегистрированный конструктор
            if (typeBuilder.BaseType is TypeBuilder baseTypeBuilder)
            {
                baseConstructor = _methodGenerator.GetRegisteredConstructor(baseTypeBuilder.Name, Type.EmptyTypes);
            }
            else
            {
                // Иначе используем обычную рефлексию
                baseConstructor = typeBuilder.BaseType!.GetConstructor(Type.EmptyTypes);
            }
            
            if (baseConstructor == null)
            {
                Console.WriteLine($"**[ WARN ] Cannot get base constructor, using object()");
                baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
            }
            
            il.Emit(OpCodes.Call, baseConstructor!);

            // ИСПРАВЛЕНИЕ: Инициализация полей класса (используем MethodGenerator для генерации выражений)
            var tempFields = new Dictionary<string, FieldBuilder>();
            var tempLocals = new Dictionary<string, LocalBuilder>();
            var tempLocalTypes = new Dictionary<string, Type>();
            var tempParams = new Dictionary<string, int>();
            var tempParamTypes = new Dictionary<string, Type>();
            
            _methodGenerator.SetContext(il, tempLocals, tempLocalTypes, fields, tempParams, tempParamTypes, classDecl.Name);
            
            foreach (var varDecl in classDecl.Members.OfType<VariableDeclaration>())
            {
                if (fields.TryGetValue(varDecl.Identifier, out var field))
                {
                    il.Emit(OpCodes.Ldarg_0); // this
                    _methodGenerator.GenerateExpression(varDecl.Expression);
                    il.Emit(OpCodes.Stfld, field);
                }
            }

            il.Emit(OpCodes.Ret);
            
            // Регистрируем конструктор по умолчанию
            _methodGenerator.RegisterConstructor(typeBuilder.Name, Type.EmptyTypes, ctorBuilder);

            DebugLog($"**[ DEBUG ]   Generated default constructor");
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
                        Console.WriteLine($"**[ OK ] Entry point set in PE header");
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
                    DebugLog($"**[ DEBUG ] Stack trace:\n{ex.StackTrace}");
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
