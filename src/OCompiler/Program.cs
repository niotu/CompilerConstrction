using OCompiler.Lexer;
using OCompiler.Utils;
using OCompiler.Parser;
using OCompiler.Semantic;
namespace OCompiler;

/// <summary>
/// O language compiler main program
/// Part of Compiler Construction course at Innopolis University
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
        Console.WriteLine("╚═══════════════════════════════════╝");
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("** Usage:");
        Console.WriteLine("  OCompiler <file.o> [options]");
        Console.WriteLine();
        Console.WriteLine("** Options:");
        Console.WriteLine("  --debug        Show all debug information");
        Console.WriteLine("  --tokens-only  Lexical analysis only(tokens output)");
        Console.WriteLine();
        Console.WriteLine("** Test examples:");
        Console.WriteLine("  tests/01_Hello.o");
        Console.WriteLine("  tests/03_ArraySquare.o");
        Console.WriteLine("  tests/04_InheritanceValid.o");
        Console.WriteLine();
    }

    private static void CompileFile(string fileName)
    {
        // Чтение исходного кода
        string sourceCode = File.ReadAllText(fileName);
        Console.WriteLine($"**[ INFO ] Symbols read: {sourceCode.Length} ");

        // Лексический анализ
        Console.WriteLine("**[ INFO ] Starting lexical analysis...");
        var lexer = new OLexer(sourceCode, fileName);
        var tokens = lexer.Tokenize();
        
        Console.WriteLine($"**[ OK ] Lexical analysis finished. Detected {tokens.Count} tokens.");

        // Показываем токены, если это запрошено
        if (Environment.GetCommandLineArgs().Contains("--tokens-only"))
        {
            PrintTokens(tokens);
        }

        if (Environment.GetCommandLineArgs().Contains("--tokens-to-file"))
        {
            PrintTokensToFile(tokens);
        }

        SyntaxAnalysis(tokens);
        
    }

    private static void SyntaxAnalysis(List<Token> tokens)
    {
        Console.WriteLine("**[ INFO ] Starting syntax analysis...");
        var scanner = new ManualLexerAdapter(tokens);
        // Console.WriteLine("Type of output: " ,scanner.GetType());
        var parser = new OCompiler.Parser.Parser(scanner);

        bool success = parser.Parse();

        if (!success)
        {
            Console.WriteLine("Parsing Error");
            return;
        }
        var ast = (ProgramNode)parser.CurrentSemanticValue.ast;
        ast.Print();

       

        // TODO: Semantic Analysis
        Console.WriteLine("** Semantic analysis (TODO)");
        // var analyzer = new SemanticAnalyzer();
        // analyzer.Analyze(ast);
        SemanticAnalysis(ast);

        Console.WriteLine("**[ OK ] Compilation completed successfully!");
    }
    private static void SemanticAnalysis(ProgramNode ast)
    {
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
        
        // 1. Семантические проверки
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
        
        // 2. Оптимизации
        if (!Environment.GetCommandLineArgs().Contains("--no-optimize"))
        {
            Console.WriteLine("**[ INFO ] Starting AST optimizations...");
            var optimizer = new Optimizer();
            var optimizedAst = optimizer.Optimize(ast);
            
            if (Environment.GetCommandLineArgs().Contains("--debug"))
            {
                Console.WriteLine("**[ DEBUG ] Optimized Abstract Syntax Tree:");
                optimizedAst.Print();
            }
            
            ast = optimizedAst;
            Console.WriteLine("**[ OK ] AST optimizations completed.");
        }
        
        // // 3. Генерация кода
        // GenerateCode(ast);
    }

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
        
        Console.WriteLine("**[ INFO ] Standard class hierarchy initialized");
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
    private static void PrintTokensToFile(List<Token> tokens) {
        // Write the tokens output to a file in the current directory
        string fileName = $"parsing_result{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        string filePath = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), fileName);

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

        System.IO.File.WriteAllText(filePath, sb.ToString());

        Console.WriteLine($"**[ INFO ] Tokens written to: {filePath}");
    }
}