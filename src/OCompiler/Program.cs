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
using System.Reflection.Emit;

namespace OCompiler;

/// <summary>
/// O language compiler main program
/// Dmitriy Lukiyanov (SD-03), Ramil Aminov (SD-01)
/// </summary>
public class Program
{
    private static bool IsDebugMode => Environment.GetCommandLineArgs().Contains("--debug");
    
    private static void DebugLog(string message)
    {
        if (IsDebugMode)
        {
            Console.WriteLine(message);
        }
    }
    
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
        
        Console.WriteLine("  --no-codegen         Skip code generation phase");
        Console.WriteLine("  --emit-dll <path>    Save as executable (.dll) with entry point");
        Console.WriteLine("  --entry-point <Class> Specify the class to use as program entry point (this() will be invoked)");
        
        Console.WriteLine();
        Console.WriteLine("** Test examples:");
        Console.WriteLine("  tests/01_Hello.o");
        Console.WriteLine("  tests/03_ArraySquare.o");
        Console.WriteLine("  tests/04_InheritanceValid.o");
        Console.WriteLine();
    }

    private static void CompileFile(string fileName)
    {
        // Parse optional entry point class name
        var args = Environment.GetCommandLineArgs();
        string? entryPoint = null;
        int entryIdx = Array.IndexOf(args, "--entry-point");
        if (entryIdx >= 0 && entryIdx + 1 < args.Length)
        {
            entryPoint = args[entryIdx + 1];
        }
        
        // ============================================================
        // PHASE 1: LEXICAL ANALYSIS
        // ============================================================
        string sourceCode = File.ReadAllText(fileName);
        Console.WriteLine($"**[ INFO ] Symbols read: {sourceCode.Length} ");

        Console.WriteLine("**[ INFO ] Starting lexical analysis...");
        var lexer = new OLexer(sourceCode, fileName);
        var tokens = lexer.Tokenize();
        
        Console.WriteLine($"**[ OK ] Lexical analysis finished. Detected {tokens.Count} tokens.");

        // Show tokens if requested
        if (Environment.GetCommandLineArgs().Contains("--tokens-only"))
        {
            PrintTokens(tokens);
            return; // Stop after lexical analysis
        }

        if (Environment.GetCommandLineArgs().Contains("--tokens-to-file"))
        {
            PrintTokensToFile(tokens);
        }

        // ============================================================
        // PHASE 2: SYNTAX ANALYSIS
        // ============================================================
        var ast = SyntaxAnalysis(tokens);
        if (ast == null)
        {
            Console.WriteLine("**[ ERR ] Syntax analysis failed");
            Environment.Exit(1);
        }
        
        // ============================================================
        // PHASE 3: SEMANTIC ANALYSIS
        // ============================================================
        var classHierarchy = SemanticAnalysis(ast);
        
        // ============================================================
        // PHASE 4: OPTIMIZATION
        // ============================================================
        ast = OptimizeAST(ast, entryPoint);
        
        // ============================================================
        // PHASE 5: CODE GENERATION
        // ============================================================
        if (!Environment.GetCommandLineArgs().Contains("--no-codegen"))
        {
            GenerateCode(ast, classHierarchy, fileName, entryPoint);
        }
        else
        {
            Console.WriteLine("**[ INFO ] Code generation skipped (--no-codegen flag)");
        }
        
        Console.WriteLine("**[ OK ] Compilation completed successfully!");
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
    
    private static ClassHierarchy SemanticAnalysis(ProgramNode ast)
    {
        Console.WriteLine("**[ INFO ] Starting semantic analysis...");
        
        // Create class hierarchy
        var classHierarchy = new ClassHierarchy();
        
        // Register standard classes
        RegisterStandardClasses(classHierarchy);
        
        // Register user-defined classes
        foreach (var classDecl in ast.Classes)
        {
            classHierarchy.AddClass(classDecl);
        }
        
        // Perform semantic checks
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
        
        // If semantic-only analysis requested, stop here
        if (Environment.GetCommandLineArgs().Contains("--semantic-only"))
        {
            Console.WriteLine("**[ INFO ] Stopping after semantic analysis (--semantic-only flag)");
            Environment.Exit(0);
        }
        
        return classHierarchy;
    }
    
    private static ProgramNode OptimizeAST(ProgramNode ast, string? entryPoint)
    {
        if (!Environment.GetCommandLineArgs().Contains("--no-optimize"))
        {
            Console.WriteLine("**[ INFO ] Starting AST optimizations...");
            var optimizer = new Optimizer();
            ast = optimizer.Optimize(ast, entryPoint);
            
            if (Environment.GetCommandLineArgs().Contains("--ast"))
            {
                Console.WriteLine("**[ DEBUG ] Optimized Abstract Syntax Tree:");
                ast.Print();
            }
            
            Console.WriteLine("**[ OK ] AST optimizations completed.");
        }
        
        return ast;
    }

    private static void GenerateCode(ProgramNode ast, ClassHierarchy hierarchy, string sourceFileName, string? entryPoint)
    {
        Console.WriteLine("**[ INFO ] Starting code generation...");
        
        try
        {
            // Get assembly name from file name
            string assemblyName = Path.GetFileNameWithoutExtension(sourceFileName);
            
            // Create code generator
            var codeGenerator = new CodeGenerator(assemblyName, hierarchy);
            // Generate assembly
            Assembly generatedAssembly = codeGenerator.Generate(ast);

            // Type correctness validation
            if (Environment.GetCommandLineArgs().Contains("--debug"))
            {
                codeGenerator.ValidateTypes();
            }

            Console.WriteLine("**[ OK ] Code generation completed successfully!");
            var args = Environment.GetCommandLineArgs();
            
            if (entryPoint != null)
            {
                Console.WriteLine($"**[ INFO ] Entry point class requested: {entryPoint}");
            }

            // Check if --emit-dll flag is present or if no flags are present (default behavior)
            bool shouldEmitDll = args.Contains("--emit-dll");
            bool hasNoCodegenFlags = !args.Contains("--emit-dll") && 
                                     !args.Contains("--no-codegen") && 
                                     !args.Contains("--semantic-only") && 
                                     !args.Contains("--tokens-only");
            
            if (shouldEmitDll || hasNoCodegenFlags)
            {
                string outputPath = "output"; // Default name
                
                if (shouldEmitDll)
                {
                    int index = Array.IndexOf(args, "--emit-dll");
                    if (index + 1 < args.Length && !args[index + 1].StartsWith("--"))
                    {
                        outputPath = args[index + 1];
                        DebugLog($"**[ DEBUG ] Output file: {outputPath}.dll");
                    }
                    else
                    {
                        DebugLog($"**[ DEBUG ] Using default output: {outputPath}.dll");
                    }
                }
                else
                {
                    DebugLog($"**[ DEBUG ] Using default output: {outputPath}.dll");
                }

                try
                {
                    // Generate entry point before saving (pass the custom entry point if specified)
                    codeGenerator.GenerateEntryPoint(entryPoint);
                    DebugLog("**[ DEBUG ] Generated entry point");
                    // Emit into a fresh 'build' directory
                    EnsureFreshBuildDir();
                    string dllPath = Path.Combine(Directory.GetCurrentDirectory(), "build", $"{outputPath}.dll");
                    SaveAssemblyToFile(codeGenerator, dllPath, asExecutable: true);

                    // Create runtimeconfig.json next to the DLL
                    string configPath = Path.Combine(Directory.GetCurrentDirectory(), "build", $"{outputPath}.runtimeconfig.json");
                    CreateRuntimeConfig(configPath);

                    // Copy OCompiler.dll to build directory (contains runtime classes)
                    CopyOCompilerDll();
                    
                    Console.WriteLine($"**[ OK ] Created: build/{Path.GetFileName(dllPath)}");
                    Console.WriteLine($"**[ INFO ] Run with: dotnet build/{Path.GetFileName(dllPath)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"**[ ERR ] Failed to save executable: {ex.Message}");
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

    private static void EnsureFreshBuildDir()
    {
        try
        {
            var buildDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
            if (Directory.Exists(buildDir))
            {
                Directory.Delete(buildDir, true);
            }
            Directory.CreateDirectory(buildDir);
            DebugLog($"**[ DEBUG ] Build directory prepared: {buildDir}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ WARN ] Could not prepare build directory: {ex.Message}");
        }
    }
    private static void SaveAssemblyToFile(CodeGenerator codeGenerator, string outputPath, bool asExecutable = false)
    {
        DebugLog($"**[ DEBUG ] Saving assembly to: {outputPath}");
        
        try
        {
            codeGenerator.SaveToFile(outputPath, asExecutable);
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
            string json = 
            @"{
                ""runtimeOptions"": {
                    ""tfm"": ""net9.0"",
                    ""framework"": {
                    ""name"": ""Microsoft.NETCore.App"",
                    ""version"": ""9.0.0""
                    }
                }
            }";
            
            File.WriteAllText(configPath, json);
            DebugLog($"**[ DEBUG ] Runtime config created: {configPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"**[ WARN ] Failed to create runtime config: {ex.Message}");
        }
    }

    private static void CopyOCompilerDll()
    {
        try
        {
            // Get the path to OCompiler.dll (the compiler itself, which contains BuiltinTypes)
            string ocompilerDll = typeof(Program).Assembly.Location;
            if (string.IsNullOrEmpty(ocompilerDll))
            {
                Console.WriteLine($"**[ WARN ] Could not locate OCompiler.dll");
                return;
            }

            string buildDir = Path.Combine(Directory.GetCurrentDirectory(), "build");
            string destPath = Path.Combine(buildDir, "OCompiler.dll");
            
            File.Copy(ocompilerDll, destPath, overwrite: true);
            DebugLog($"**[ DEBUG ] Copied OCompiler.dll to build directory");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"**[ WARN ] Failed to copy OCompiler.dll: {ex.Message}");
        }
    }

    // ============================================================
    // HELPER FUNCTIONS
    // ============================================================

    private static void RegisterStandardClasses(ClassHierarchy hierarchy)
    {
        // Create minimal declarations for standard classes
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
