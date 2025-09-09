using OCompiler.Lexer;
using OCompiler.Utils;

namespace OCompiler;

/// <summary>
/// Компилятор языка O - L17 Team
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
            Console.WriteLine($"❌ Ошибка: файл '{fileName}' не найден.");
            return;
        }

        try
        {
            Console.WriteLine($"🔍 Компиляция файла: {fileName}");
            CompileFile(fileName);
        }
        catch (CompilerException ex)
        {
            Console.WriteLine($"❌ Ошибка компиляции: {ex.Message}");
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 Внутренняя ошибка: {ex.Message}");
            if (args.Contains("--debug"))
            {
                Console.WriteLine($"Stack trace:\n{ex.StackTrace}");
            }
            Environment.Exit(1);
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine("╔══════════════════════════════════════════════╗");
        Console.WriteLine("║          Компилятор языка O v1.0             ║");
        Console.WriteLine("║              L17 Team                        ║");
        Console.WriteLine("║  Dmitriy Lukiyanov (SD-03)                   ║");
        Console.WriteLine("║  Ramil Aminov (SD-01)                        ║");
        Console.WriteLine("╚══════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("📋 Использование:");
        Console.WriteLine("  OCompiler <file.o> [options]");
        Console.WriteLine();
        Console.WriteLine("🎯 Опции:");
        Console.WriteLine("  --debug        Показать подробную отладочную информацию");
        Console.WriteLine("  --tokens-only  Только лексический анализ (вывод токенов)");
        Console.WriteLine();
        Console.WriteLine("📁 Примеры тестовых файлов:");
        Console.WriteLine("  tests/01_Hello.o");
        Console.WriteLine("  tests/03_ArraySquare.o");
        Console.WriteLine("  tests/04_InheritanceValid.o");
        Console.WriteLine();
    }

    private static void CompileFile(string fileName)
    {
        // Чтение исходного кода
        string sourceCode = File.ReadAllText(fileName);
        Console.WriteLine($"📖 Прочитано {sourceCode.Length} символов");

        // Лексический анализ
        Console.WriteLine("🔤 Запуск лексического анализа...");
        var lexer = new OLexer(sourceCode, fileName);
        var tokens = lexer.Tokenize();
        
        Console.WriteLine($"✅ Лексический анализ завершен. Найдено {tokens.Count} токенов.");

        // Показываем токены, если это запрошено
        if (Environment.GetCommandLineArgs().Contains("--tokens-only"))
        {
            PrintTokens(tokens);
            return;
        }

        // TODO: Синтаксический анализ
        Console.WriteLine("🔧 Синтаксический анализ (TODO)");
        // var parser = new OParser(tokens);
        // var ast = parser.Parse();
        
        // TODO: Семантический анализ
        Console.WriteLine("🧠 Семантический анализ (TODO)");
        // var analyzer = new SemanticAnalyzer();
        // analyzer.Analyze(ast);
        
        // TODO: Генерация кода
        Console.WriteLine("⚡ Генерация кода (TODO)");
        // var generator = new CodeGenerator();
        // generator.Generate(ast);
        
        Console.WriteLine("🎉 Компиляция завершена успешно!");
    }

    private static void PrintTokens(List<Token> tokens)
    {
        Console.WriteLine("\n📝 Найденные токены:");
        Console.WriteLine("╔════════════════════════════════════════════════════╗");
        
        for (int i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.Type == TokenType.EOF) break;
            
            string value = string.IsNullOrEmpty(token.Value) ? "" : $"'{token.Value}'";
            Console.WriteLine($"║ {i+1,3}: {token.Type,-18} {value,-15} @ {token.Position.Line}:{token.Position.Column}");
        }
        
        Console.WriteLine("╚════════════════════════════════════════════════════╝");
        Console.WriteLine($"Всего токенов: {tokens.Count - 1} (без EOF)");
    }
}