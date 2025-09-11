using OCompiler.Lexer;
using OCompiler.Utils;

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
            Console.WriteLine($"** Error '{fileName}' not found");
            return;
        }

        try
        {
            Console.WriteLine($"** File compiling: {fileName}...");
            CompileFile(fileName);
        }
        catch (CompilerException ex)
        {
            Console.WriteLine($"** Compilation error: {ex.Message}");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"** Internal error: {ex.Message}");
            if (args.Contains("--debug"))
            {
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
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
        Console.WriteLine($"** Symbols read: {sourceCode.Length} ");

        // Лексический анализ
        Console.WriteLine("** Starting lexical analysis...");
        var lexer = new OLexer(sourceCode, fileName);
        var tokens = lexer.Tokenize();
        
        Console.WriteLine($"** Lexical analysis finished. Detected {tokens.Count} tokens.");

        // Показываем токены, если это запрошено
        if (Environment.GetCommandLineArgs().Contains("--tokens-only"))
        {
            PrintTokens(tokens);
            return;
        }

        // TODO: Syntax Analysis
        Console.WriteLine("** Syntax analysis (TODO)");
        // var parser = new OParser(tokens);
        // var ast = parser.Parse();

        // TODO: Semantic Analysis
        Console.WriteLine("** Semantic analysis (TODO)");
        // var analyzer = new SemanticAnalyzer();
        // analyzer.Analyze(ast);

        Console.WriteLine("** Compilation completed successfully!");
    }

    private static void PrintTokens(List<Token> tokens)
    {
        Console.WriteLine("\n** Tokens detected:\n");
        
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.EOF) break;
            
            string value = string.IsNullOrEmpty(token.Value) ? "" : $"'{token.Value}'";
            Console.WriteLine($"  {i+1,3}: {token.Type,-18} {value,-15} @ {token.Position.Line}:{token.Position.Column}");
        }
        
        Console.WriteLine($"\n** Tokens calculated: {tokens.Count - 1} (except EOF)");
    }
}