using OCompiler.Lexer;
using OCompiler.Utils;
using OCompiler.Parser;
using OCompiler.Semantic;
using System.Reflection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OCompiler.CodeGeneration;
using OCompiler.Runtime;
using System.Reflection.Emit;  // НОВОЕ: Для работы со сборками

namespace OCompiler;

/// <summary>
/// O language compiler main program
/// Dmitriy Lukiyanov (SD-03), Ramil Aminov (SD-01)
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        PrintHeader();

        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        string fileName = args[0];
        
        if (!File.Exists(fileName))
        {
            Console.WriteLine($"**[ ERR ] Error '{fileName}' not found");
            return;
        }

        try
        {
            Console.WriteLine($"**[ INFO ] File compiling: {fileName}...");
            CompileFile(fileName);
        }
        catch (CompilerException ex)
        {
            Console.WriteLine($"**[ ERR ] Compilation error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"**[ ERR ] Internal error: {ex.Message}");
            if (args.Contains("--debug"))
            {
                Console.WriteLine($"**[ INFO ] Stack trace:\n{ex.StackTrace}");
            }
            Environment.Exit(1);
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine("╔═══════════════════════════════════╗");
        Console.WriteLine("║         O Language Compiler       ║");
        Console.WriteLine("║             LI7 Team              ║");
        Console.WriteLine("║    with IL Code Generation        ║"); // НОВОЕ
        Console.WriteLine("╚═══════════════════════════════════╝");
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("** Usage:");
        Console.WriteLine("  OCompiler <file.o> [options]");
        Console.WriteLine();
        Console.WriteLine("** Options:");
        Console.WriteLine("  --debug              Show all debug information");
        Console.WriteLine("  --tokens-only        Lexical analysis only (tokens output)");
        Console.WriteLine("  --tokens-to-file     Save tokens to file");
        Console.WriteLine("  --ast                Print Abstract Syntax Tree");
        Console.WriteLine("  --semantic-only      Stop after semantic analysis");
        Console.WriteLine("  --no-optimize        Skip AST optimizations");
        
        // НОВЫЕ опции для генерации кода
        Console.WriteLine("  --no-codegen         Skip code generation phase");
        Console.WriteLine("  --run                Execute generated assembly after compilation");
        Console.WriteLine("  --save-assembly <path>  Save generated assembly to file (.dll)");
        Console.WriteLine("  --emit-dll <path>    Save as executable (.dll) with entry point");
        Console.WriteLine("  --emit-il            Print generated IL instructions (debug)");
        Console.WriteLine("  --entry-point <Class> Specify the class to use as program entry point (this() will be invoked)");
        
        Console.WriteLine();
        Console.WriteLine("** Test examples:");
        Console.WriteLine("  tests/01_Hello.o");
        Console.WriteLine("  tests/03_ArraySquare.o");
        Console.WriteLine("  tests/04_InheritanceValid.o");
        Console.WriteLine();
        Console.WriteLine("** Code generation examples:");
        Console.WriteLine("  OCompiler tests/01_Hello.o --run");
        Console.WriteLine("  OCompiler tests/08_RecursiveFactorial.o --save-assembly factorial.dll");
        Console.WriteLine();
    }

    private static void CompileFile(string fileName)
    {
        // ============================================================
        // ФАЗА 1: ЛЕКСИЧЕСКИЙ АНАЛИЗ
        // ============================================================
        string sourceCode = File.ReadAllText(fileName);
        Console.WriteLine($"**[ INFO ] Symbols read: {sourceCode.Length} ");

        Console.WriteLine("**[ INFO ] Starting lexical analysis...");
        var lexer = new OLexer(sourceCode, fileName);
        var tokens = lexer.Tokenize();
        
        Console.WriteLine($"**[ OK ] Lexical analysis finished. Detected {tokens.Count} tokens.");

        // Показываем токены, если это запрошено
        if (Environment.GetCommandLineArgs().Contains("--tokens-only"))
        {
            PrintTokens(tokens);
            return; // Останавливаемся после лексического анализа
        }

        if (Environment.GetCommandLineArgs().Contains("--tokens-to-file"))
        {
            PrintTokensToFile(tokens);
        }

        // ============================================================
        // ФАЗА 2: СИНТАКСИЧЕСКИЙ АНАЛИЗ
        // ============================================================
        var ast = SyntaxAnalysis(tokens);
        if (ast == null)
        {
            Console.WriteLine("**[ ERR ] Syntax analysis failed");
            Environment.Exit(1);
        }
        
        // ============================================================
        // ФАЗЫ 3-5: СЕМАНТИКА, ОПТИМИЗАЦИЯ, ГЕНЕРАЦИЯ КОДА
        // ============================================================
        SemanticAnalysisAndCodeGen(ast, fileName);
    }

    private static ProgramNode? SyntaxAnalysis(List<Token> tokens)
    {
        Console.WriteLine("**[ INFO ] Starting syntax analysis...");
        var scanner = new ManualLexerAdapter(tokens);
        var parser = new OCompiler.Parser.Parser(scanner);

        bool success = parser.Parse();

        if (!success)
        {
            Console.WriteLine("**[ ERR ] Parsing failed");
            return null;
        }
        
        var ast = (ProgramNode)parser.CurrentSemanticValue.ast;

        if (Environment.GetCommandLineArgs().Contains("--ast"))
        {
            Console.WriteLine("**[ DEBUG ] Abstract Syntax Tree:");
            ast.Print();
        }

        Console.WriteLine("**[ OK ] Syntax analysis completed successfully!");
        return ast;
    }
    
    private static void SemanticAnalysisAndCodeGen(ProgramNode ast, string sourceFileName)
    {
        // ============================================================
        // ФАЗА 3: СЕМАНТИЧЕСКИЙ АНАЛИЗ
        // ============================================================
        Console.WriteLine("**[ INFO ] Starting semantic analysis...");
        
        // Создаем иерархию классов
        var classHierarchy = new ClassHierarchy();
        
        // Регистрируем стандартные классы
        RegisterStandardClasses(classHierarchy);
        
        // Регистрируем пользовательские классы
        foreach (var classDecl in ast.Classes)
        {
            classHierarchy.AddClass(classDecl);
        }
        
        // Семантические проверки
        var semanticChecker = new SemanticChecker(classHierarchy);
        semanticChecker.Check(ast);
        
        if (semanticChecker.Errors.Any())
        {
            Console.WriteLine("**[ ERR ] Semantic errors found:");
            foreach (var error in semanticChecker.Errors)
            {
                Console.WriteLine($"  - {error}");
            }
            throw new CompilerException("Semantic analysis failed");
        }
        
        Console.WriteLine($"**[ OK ] Semantic checks passed. No errors found.");
        
        // Если запрошено только семантический анализ - останавливаемся здесь
        if (Environment.GetCommandLineArgs().Contains("--semantic-only"))
        {
            Console.WriteLine("**[ INFO ] Stopping after semantic analysis (--semantic-only flag)");
            return;
        }
        
        if (!Environment.GetCommandLineArgs().Contains("--no-optimize"))
        {
            Console.WriteLine("**[ INFO ] Starting AST optimizations...");
            var optimizer = new Optimizer();
            ast = optimizer.Optimize(ast);
            
            if (Environment.GetCommandLineArgs().Contains("--ast"))
            {
                Console.WriteLine("**[ DEBUG ] Optimized Abstract Syntax Tree:");
                ast.Print();
            }
            
            Console.WriteLine("**[ OK ] AST optimizations completed.");
        }
    
        if (!Environment.GetCommandLineArgs().Contains("--no-codegen"))
        {
            GenerateCode(ast, classHierarchy, sourceFileName);
        }
        else
        {
            Console.WriteLine("**[ INFO ] Code generation skipped (--no-codegen flag)");
        }
    }

    private static void GenerateCode(ProgramNode ast, ClassHierarchy hierarchy, string sourceFileName)
    {
        Console.WriteLine("**[ INFO ] Starting code generation...");
        
        try
        {
            // Получаем имя для сборки из имени файла
            string assemblyName = Path.GetFileNameWithoutExtension(sourceFileName);
            
            // Создаем генератор кода
            var codeGenerator = new CodeGenerator(assemblyName, hierarchy);
            // Генерируем сборку
            Assembly generatedAssembly = codeGenerator.Generate(ast);

            // // НОВОЕ: Проверка корректности типов
            if (Environment.GetCommandLineArgs().Contains("--debug"))
            {
                codeGenerator.ValidateTypes();
            }

            Console.WriteLine("**[ OK ] Code generation completed successfully!");
            var args = Environment.GetCommandLineArgs();
            // Parse optional entry point class name
            string? entryPoint = null;
            int entryIdx = Array.IndexOf(args, "--entry-point");
            if (entryIdx >= 0 && entryIdx + 1 < args.Length)
            {
                entryPoint = args[entryIdx + 1];
                Console.WriteLine($"**[ INFO ] Entry point class requested: {entryPoint}");
            }
            
            // Опционально: вывод сгенерированного IL
            if (args.Contains("--emit-il"))
            {
                PrintILAsText(codeGenerator);
            }

            if (args.Contains("--emit-assembly"))
            {
                codeGenerator.SaveToFile("OCompilerOutput.dll");
            }

            if (args.Contains("--emit-dll"))
            {
                int index = Array.IndexOf(args, "--emit-dll");
                if (index + 1 < args.Length)
                {
                    Console.WriteLine($"**[ INFO ] --emit-dll flag detected");
                    string outputPath = args[index + 1];
                
                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        
                    }
                    Console.WriteLine($"**[ INFO ] --emit-dll flag: {outputPath}");
                    try
                    {
                        // Генерируем точку входа перед сохранением
                        codeGenerator.GenerateEntryPoint();
                        // Emit into a fresh 'build' directory with a stable name 'output.dll'
                        EnsureFreshBuildDir();
                        string dllPath = Path.Combine(Directory.GetCurrentDirectory(), "build", $"{outputPath}.dll");
                        SaveAssemblyToFile(codeGenerator, dllPath, asExecutable: true);

                        // Create runtimeconfig.json next to the DLL
                        string configPath = Path.Combine(Directory.GetCurrentDirectory(), "build", $"{outputPath}.runtimeconfig.json");
                        CreateRuntimeConfig(configPath);
                        
                        Console.WriteLine();
                        Console.WriteLine("**[ INFO ] ========================================");
                        Console.WriteLine("**[ INFO ] Executable created successfully!");
                        Console.WriteLine("**[ INFO ] ========================================");
                        Console.WriteLine($"**[ INFO ] To run the program, use:");
                        Console.WriteLine($"**[ INFO ]   dotnet build/{Path.GetFileName(dllPath)}");
                        Console.WriteLine($"**[ INFO ] Or reference it from C#/.NET code");
                        Console.WriteLine("**[ INFO ] ========================================");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"**[ ERR ] Failed to save executable: {ex.Message}");
                    }
                } else
                {
                    Console.WriteLine("**[ ERR ] --emit-dll flag requires an output path argument");
                }
            }

        }
        catch (Exception ex)
        {
            Console.WriteLine($"**[ ERR ] Code generation failed: {ex.Message}");
            if (Environment.GetCommandLineArgs().Contains("--debug"))
            {
                Console.WriteLine($"**[ DEBUG ] Stack trace:\n{ex.StackTrace}");
            }
            throw new CompilerException("Code generation failed", ex);
        }
    }
        private static void PrintILAsText(CodeGenerator codeGenerator)
        {
            var ilEmitter = new OCompiler.CodeGeneration.ILEmitter();
            
            // Генерируем директивы сборки
            ilEmitter.EmitAssemblyDirective("GeneratedAssembly");
            
            var allTypes = codeGenerator.GetAllTypes();
            
            foreach (var kvp in allTypes)
            {
                var typeName = kvp.Key;
                var type = kvp.Value;
                
                // Пропускаем встроенные типы и Program
                if (typeName is "Program" or "Integer" or "Real" or "Boolean")
                    continue;
                
                ilEmitter.EmitClassStart(typeName);
                
                // Выводим поля
                var fields = type.GetFields(
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.DeclaredOnly);
                
                foreach (var field in fields)
                {
                    ilEmitter.EmitFieldDeclaration(
                        field.FieldType.Name.ToLower(),
                        field.Name,
                        field.IsPrivate);
                }
                
                // Выводим конструкторы
                var constructors = type.GetConstructors(
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance);
                
                foreach (var ctor in constructors)
                {
                    var parameters = ctor.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name.ToLower()} {p.Name}"));
                    
                    ilEmitter.EmitConstructorStart(paramStr);
                    ilEmitter.EmitInstruction("ldarg.0");
                    ilEmitter.EmitInstruction("call instance void [mscorlib]System.Object::.ctor()");
                    ilEmitter.EmitConstructorEnd();
                }
                
                // Выводим методы
                var methods = type.GetMethods(
                    System.Reflection.BindingFlags.Public | 
                    System.Reflection.BindingFlags.NonPublic | 
                    System.Reflection.BindingFlags.Instance | 
                    System.Reflection.BindingFlags.DeclaredOnly);
                
                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name.ToLower()} {p.Name}"));
                    
                    ilEmitter.EmitMethodStart(
                        method.Name,
                        method.ReturnType.Name.ToLower(),
                        paramStr);
                    
                    ilEmitter.EmitInstruction(".maxstack 8");
                    ilEmitter.EmitReturn();
                    
                    ilEmitter.EmitMethodEnd();
                }
                
                ilEmitter.EmitClassEnd();
            }
            
            Console.WriteLine("\n**[ DEBUG ] Generated MSIL Code:");
            Console.WriteLine("========================================");
            Console.WriteLine(ilEmitter.GetILCode());
            Console.WriteLine("========================================");
        }
        // ============================================================
        // ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ ДЛЯ ГЕНЕРАЦИИ КОДА
        // ============================================================
        
        private static void PrintGeneratedIL(Assembly assembly, CodeGenerator codeGenerator)
        {
            Console.WriteLine("\n**[ DEBUG ] Generated IL Instructions:");
            Console.WriteLine("========================================");

            try
            {
                // Получаем типы из CodeGenerator, а не из Assembly
                var allTypes = codeGenerator.GetAllTypes();
                
                if (allTypes.Count == 0)
                {
                    Console.WriteLine("**[ INFO ] No user-defined types generated.");
                    return;
                }

                foreach (var kvp in allTypes)
                {
                    var typeName = kvp.Key;
                    var type = kvp.Value;

                    Console.WriteLine($"\n--- Type: {typeName} ---");
                    
                    var constructors = type.GetConstructors(
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.NonPublic | 
                        System.Reflection.BindingFlags.Instance);

                    if (constructors.Length > 0)
                    {
                        Console.WriteLine($"Constructors:");
                        foreach (var ctor in constructors)
                        {
                            var parameters = ctor.GetParameters();
                            var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Console.WriteLine($"  - .ctor({paramStr})");
                        }
                    }

                    var methods = type.GetMethods(
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.NonPublic | 
                        System.Reflection.BindingFlags.Instance | 
                        System.Reflection.BindingFlags.DeclaredOnly);

                    if (methods.Length > 0)
                    {
                        Console.WriteLine($"Methods:");
                        foreach (var method in methods)
                        {
                            var parameters = method.GetParameters();
                            var paramStr = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Console.WriteLine($"  - {method.ReturnType.Name} {method.Name}({paramStr})");
                        }
                    }

                    var fields = type.GetFields(
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.NonPublic | 
                        System.Reflection.BindingFlags.Instance | 
                        System.Reflection.BindingFlags.DeclaredOnly);

                    if (fields.Length > 0)
                    {
                        Console.WriteLine($"Fields:");
                        foreach (var field in fields)
                        {
                            Console.WriteLine($"  - {field.FieldType.Name} {field.Name}");
                        }
                    }
                }

                Console.WriteLine("\n========================================");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"**[ WARN ] Could not retrieve IL information: {ex.Message}");
            }
        }

    private static void SaveAssemblyToFile(CodeGenerator codeGenerator, string outputPath, bool asExecutable = false)
    {
        Console.WriteLine($"**[ INFO ] Saving assembly to: {outputPath}");
        
        try
        {
            // ПРИМЕЧАНИЕ: В .NET Core/.NET 5+ прямое сохранение AssemblyBuilder недоступно
            // Требуется использовать:
            // - .NET 9+: PersistedAssemblyBuilder
            // - Альтернатива: MetadataLoadContext или сторонние библиотеки
            
            codeGenerator.SaveToFile(outputPath, asExecutable);
            Console.WriteLine($"**[ OK ] Assembly saved successfully!");
        }
        catch (NotImplementedException)
        {
            Console.WriteLine("**[ WARN ] Assembly saving not implemented yet.");
            Console.WriteLine("**[ INFO ] Use .NET 9+ with PersistedAssemblyBuilder or MetadataLoadContext.");
            Console.WriteLine("**[ INFO ] For now, assembly exists only in memory (use --run to execute).");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"**[ ERR ] Failed to save assembly: {ex.Message}");
        }
    }

    private static void CreateRuntimeConfig(string configPath)
    {
        try
        {
            string json = @"{
  ""runtimeOptions"": {
    ""tfm"": ""net9.0"",
    ""framework"": {
      ""name"": ""Microsoft.NETCore.App"",
      ""version"": ""9.0.0""
    }
  }
}";
            
            File.WriteAllText(configPath, json);
            Console.WriteLine($"**[ OK ] Runtime config created: {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"**[ WARN ] Failed to create runtime config: {ex.Message}");
        }
    }

private static void ExecuteGeneratedAssembly(Assembly assembly)
{
    Console.WriteLine("\n**[ INFO ] Executing generated code...");
    Console.WriteLine("========================================");
    
    try
    {
        // ИСПРАВЛЕНИЕ: Получаем типы из CodeGenerator, а не из Assembly
        // Для динамических сборок Assembly.GetTypes() не работает
        
        // Вместо этого используем рефлексию для получения завершённых типов
        var assemblyBuilder = assembly as AssemblyBuilder;
        if (assemblyBuilder == null)
        {
            Console.WriteLine("**[ ERR ] Assembly is not an AssemblyBuilder");
            return;
        }

        // Получаем модуль
        var modules = assemblyBuilder.GetLoadedModules();
        Console.WriteLine($"**[ INFO ] Modules found: {modules.Length}");
        if (modules.Length == 0)
        {
            Console.WriteLine("**[ WARN ] No modules found in assembly");
            return;
        }

        var module = modules[0];
        bool executed = false;

        // Пытаемся найти типы через ModuleBuilder
        if (module is ModuleBuilder moduleBuilder)
        {
            // Для динамических модулей нужно вручную отслеживать созданные типы
            // Используем рефлексию для доступа к внутренним структурам
            var types = GetTypesFromModuleBuilder(moduleBuilder);
            
            if (types.Count == 0)
            {
                Console.WriteLine("**[ WARN ] No types found in module");
                return;
            }

            foreach (var type in types)
            {
                // Пропускаем встроенные типы компилятора
                if (type.Name.StartsWith("<") || type.IsAbstract || type.IsInterface)
                    continue;
                
                var constructor = type.GetConstructor(Type.EmptyTypes);
                if (constructor != null)
                {
                    Console.WriteLine($"**[ INFO ] Creating instance of class: {type.Name}");
                    
                    try
                    {
                        var instance = Activator.CreateInstance(type);
                        Console.WriteLine($"**[ OK ] Instance created successfully!");
                        
                        // Опционально: вызов методов, если они есть
                        var mainMethod = type.GetMethod("Main", BindingFlags.Public | BindingFlags.Instance);
                        if (mainMethod != null)
                        {
                            Console.WriteLine($"**[ INFO ] Calling method: Main()");
                            mainMethod.Invoke(instance, null);
                        }
                        
                        executed = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"**[ ERR ] Runtime error: {ex.InnerException?.Message ?? ex.Message}");
                        if (Environment.GetCommandLineArgs().Contains("--debug"))
                        {
                            Console.WriteLine($"**[ DEBUG ] Stack trace:\n{ex.InnerException?.StackTrace ?? ex.StackTrace}");
                        }
                    }
                }
            }
        }
        
        if (!executed)
        {
            Console.WriteLine("**[ WARN ] No executable entry point found.");
            Console.WriteLine("**[ INFO ] Tried to find a class with parameterless constructor.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"**[ ERR ] Execution failed: {ex.Message}");
        if (Environment.GetCommandLineArgs().Contains("--debug"))
        {
            Console.WriteLine($"**[ DEBUG ] Stack trace:\n{ex.StackTrace}");
        }
    }
    
    Console.WriteLine("========================================\n");
}

// НОВАЯ ВСПОМОГАТЕЛЬНАЯ ФУНКЦИЯ
private static List<Type> GetTypesFromModuleBuilder(ModuleBuilder moduleBuilder)
{
    var types = new List<Type>();
    
    try
    {
        // Используем рефлексию для доступа к внутреннему полю _typeBuilderDict
        var typeBuilderDictField = typeof(ModuleBuilder).GetField(
            "_typeBuilderDict",
            BindingFlags.NonPublic | BindingFlags.Instance);
        
        if (typeBuilderDictField != null)
        {
            var typeBuilderDict = typeBuilderDictField.GetValue(moduleBuilder);
            if (typeBuilderDict is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    if (entry.Value is TypeBuilder tb)
                    {
                        // Проверяем, что тип завершён
                        try
                        {
                            var createdType = tb.CreateType();
                            if (createdType != null)
                            {
                                types.Add(createdType);
                            }
                        }
                        catch
                        {
                            // Тип уже был создан, пытаемся получить его через рефлексию
                            var createdTypeField = typeof(TypeBuilder).GetField(
                                "_bakedRuntimeType",
                                BindingFlags.NonPublic | BindingFlags.Instance);
                            
                            if (createdTypeField != null)
                            {
                                var createdType = createdTypeField.GetValue(tb) as Type;
                                if (createdType != null)
                                {
                                    types.Add(createdType);
                                }
                            }
                        }
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"**[ DEBUG ] Failed to get types via reflection: {ex.Message}");
    }
    
    return types;
}


    // ============================================================
    // СУЩЕСТВУЮЩИЕ ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ (БЕЗ ИЗМЕНЕНИЙ)
    // ============================================================

    private static void RegisterStandardClasses(ClassHierarchy hierarchy)
    {
        // Создаем минимальные объявления для стандартных классов
        var standardClasses = new[]
        {
            ("Class", ""),
            ("AnyValue", "Class"),
            ("Integer", "AnyValue"), 
            ("Real", "AnyValue"),
            ("Boolean", "AnyValue"),
            ("AnyRef", "Class"),
            ("Array", "AnyRef"),
            ("List", "AnyRef")
        };
        
        foreach (var (className, baseClass) in standardClasses)
        {
            var classDecl = new ClassDeclaration(className, "", baseClass, new List<MemberDeclaration>());
            hierarchy.AddClass(classDecl);
        }
        
    }

    private static void PrintTokens(List<Token> tokens)
    {
        Console.WriteLine("\n**[ INFO ] Tokens detected:\n");

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.EOF) break;

            string value = string.IsNullOrEmpty(token.Value) ? "" : $"'{token.Value}'";
            Console.WriteLine($"  {i + 1,3}: {token.Type,-18} {value,-15} @ {token.Position.Line}:{token.Position.Column}");
        }

        Console.WriteLine($"\n**[ INFO ] Tokens calculated: {tokens.Count - 1} (except EOF)");
    }
    
    private static void PrintTokensToFile(List<Token> tokens)
    {
        string fileName = $"parsing_result{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string filePath = Path.Combine(Directory.GetCurrentDirectory(), fileName);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("**[ INFO ] Tokens detected:\n");

        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.EOF) break;

            string value = string.IsNullOrEmpty(token.Value) ? "" : $"'{token.Value}'";
            sb.AppendLine($"  {i+1,3}: {token.Type,-18} {value,-15} @ {token.Position.Line}:{token.Position.Column}");
        }

        sb.AppendLine($"\n**[ INFO ] Tokens calculated: {Math.Max(0, tokens.Count - 1)} (except EOF)");

        File.WriteAllText(filePath, sb.ToString());

        Console.WriteLine($"**[ INFO ] Tokens written to: {filePath}");
    }
}
