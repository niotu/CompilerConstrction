using OCompiler.Lexer;
using OCompiler.Utils;
using OCompiler.Parser;
using OCompiler.Semantic;
using OCompiler.CodeGen; // НОВОЕ: Импорт модуля генерации кода
using System.Reflection;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;  // НОВОЕ: Для работы со сборками

namespace OCompiler;

/// <summary>
/// O language compiler main program
/// Part of Compiler Construction course at Innopolis University
/// Dmitriy Lukiyanov (SD-03), Ramil Aminov (SD-01)
/// 
/// CHANGELOG:
/// - Added code generation phase using System.Reflection.Emit
/// - Added --codegen, --run, --save-assembly command line options
/// - Integrated CodeGenerator into compilation pipeline
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
        Console.WriteLine("  --emit-il            Print generated IL instructions (debug)");
        
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
            return;
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
        
        // ============================================================
        // ФАЗА 4: ОПТИМИЗАЦИЯ AST
        // ============================================================
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
        
        // ============================================================
        // ФАЗА 5: ГЕНЕРАЦИЯ КОДА (НОВАЯ ФАЗА)
        // ============================================================
        if (!Environment.GetCommandLineArgs().Contains("--no-codegen"))
        {
            GenerateCode(ast, classHierarchy, sourceFileName);
        }
        else
        {
            Console.WriteLine("**[ INFO ] Code generation skipped (--no-codegen flag)");
        }

        Console.WriteLine("**[ OK ] Compilation completed successfully!");
    }

    // ============================================================
    // НОВАЯ ФУНКЦИЯ: ГЕНЕРАЦИЯ КОДА
    // ============================================================
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
            
            Console.WriteLine("**[ OK ] Code generation completed successfully!");
            
            // Опционально: вывод сгенерированного IL
            if (Environment.GetCommandLineArgs().Contains("--emit-il"))
            {
                PrintGeneratedIL(generatedAssembly);
            }
            
            // Опционально: сохранение сборки в файл
            var saveAssemblyArgs = Environment.GetCommandLineArgs();
            int saveIndex = Array.IndexOf(saveAssemblyArgs, "--save-assembly");
            if (saveIndex >= 0 && saveIndex + 1 < saveAssemblyArgs.Length)
            {
                string outputPath = saveAssemblyArgs[saveIndex + 1];
                SaveAssemblyToFile(codeGenerator, outputPath);
            }
            
            // Опционально: выполнение сгенерированного кода
            if (Environment.GetCommandLineArgs().Contains("--run"))
            {
                ExecuteGeneratedAssembly(generatedAssembly);
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

    // ============================================================
    // ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ ДЛЯ ГЕНЕРАЦИИ КОДА
    // ============================================================
    
    private static void PrintGeneratedIL(Assembly assembly)
    {
        Console.WriteLine("\n**[ DEBUG ] Generated IL Instructions:");
        Console.WriteLine("========================================");
        
        foreach (var type in assembly.GetTypes())
        {
            Console.WriteLine($"\nClass: {type.Name}");
            
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Console.WriteLine($"  Method: {method.Name}");
                
                try
                {
                    var methodBody = method.GetMethodBody();
                    if (methodBody != null)
                    {
                        var il = methodBody.GetILAsByteArray();
                        Console.WriteLine($"    IL Size: {il?.Length ?? 0} bytes");
                        // Полная дизассемблировка IL требует дополнительной библиотеки
                        // Например: ILSpy.Decompiler или System.Reflection.Metadata
                    }
                }
                catch
                {
                    // Некоторые методы могут быть недоступны для получения тела
                }
            }
        }
        
        Console.WriteLine("========================================\n");
    }

    private static void SaveAssemblyToFile(CodeGenerator codeGenerator, string outputPath)
    {
        Console.WriteLine($"**[ INFO ] Saving assembly to: {outputPath}");
        
        try
        {
            // ПРИМЕЧАНИЕ: В .NET Core/.NET 5+ прямое сохранение AssemblyBuilder недоступно
            // Требуется использовать:
            // - .NET 9+: PersistedAssemblyBuilder
            // - Альтернатива: MetadataLoadContext или сторонние библиотеки
            
            codeGenerator.SaveToFile(outputPath);
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

    private static void ExecuteGeneratedAssembly(Assembly assembly)
    {
        Console.WriteLine("\n**[ INFO ] Executing generated code...");
        Console.WriteLine("========================================");
        
        try
        {
            // Ищем entry point для выполнения
            // Стратегия: ищем первый класс с конструктором без параметров
            
            var types = assembly.GetTypes();
            bool executed = false;
            
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
                        // Например, можно искать метод main() или run()
                        var mainMethod = type.GetMethod("main", BindingFlags.Public | BindingFlags.Instance);
                        if (mainMethod != null)
                        {
                            Console.WriteLine($"**[ INFO ] Calling method: main()");
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
            
            if (!executed)
            {
                Console.WriteLine("**[ WARN ] No executable entry point found.");
                Console.WriteLine("**[ INFO ] Looking for a class with parameterless constructor...");
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

    // ============================================================
    // СУЩЕСТВУЮЩИЕ ВСПОМОГАТЕЛЬНЫЕ ФУНКЦИИ (БЕЗ ИЗМЕНЕНИЙ)
    // ============================================================

    private static void RegisterStandardClasses(ClassHierarchy hierarchy)
    {
        // Создаем минимальные объявления для стандартных классов
        var standardClasses = new[]
        {
            ("Class", null),
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
            var classDecl = new ClassDeclaration(className, null, baseClass, new List<MemberDeclaration>());
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
