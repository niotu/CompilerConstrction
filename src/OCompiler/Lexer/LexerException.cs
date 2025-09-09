using OCompiler.Utils;

namespace OCompiler.Lexer;

/// <summary>
/// Исключение лексического анализатора
/// </summary>
public class LexerException : CompilerException
{
    public Position Position { get; }

    public LexerException(string message, Position position) 
        : base($"Лексическая ошибка в {position}: {message}")
    {
        Position = position;
    }

    public LexerException(string message, Position position, Exception innerException) 
        : base($"Лексическая ошибка в {position}: {message}", innerException)
    {
        Position = position;
    }

    public override string ToString()
    {
        return Message;
    }
}