using OCompiler.Lexer;
using OCompiler.Utils;
using OCompiler.Parser;

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

        // TODO: Syntax Analysis

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
        // ast.Print();

        // TODO: Semantic Analysis
        // Console.WriteLine("** Semantic analysis (TODO)");
        // var analyzer = new SemanticAnalyzer();
        // analyzer.Analyze(ast);

        Console.WriteLine("**[ OK ] Compilation completed successfully!");
    }

    private static void PrintTokens(List<Token> tokens)
    {
        Console.WriteLine("\n**[ INFO ] Tokens detected:\n");
        
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.EOF) break;
            
            string value = string.IsNullOrEmpty(token.Value) ? "" : $"'{token.Value}'";
            Console.WriteLine($"  {i+1,3}: {token.Type,-18} {value,-15} @ {token.Position.Line}:{token.Position.Column}");
        }
        
        Console.WriteLine($"\n**[ INFO ] Tokens calculated: {tokens.Count - 1} (except EOF)");
    }
}