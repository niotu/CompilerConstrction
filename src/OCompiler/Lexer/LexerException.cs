using System;
using OCompiler.Utils;

namespace OCompiler.Lexer;

/// <summary>
/// Lexer exception (lexical error)
/// </summary>
public class LexerException : CompilerException
{
    public Position Position { get; }

    public LexerException(string message, Position position) 
        : base($"Lexical error at {position}: {message}")
    {
        Position = position;
    }

    public LexerException(string message, Position position, Exception innerException) 
        : base($"Lexical error at {position}: {message}", innerException)
    {
        Position = position;
    }

    public override string ToString()
    {
        return Message;
    }
}