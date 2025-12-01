using System;

namespace OCompiler.Lexer;

/// <summary>
/// O language token
/// </summary>
public readonly struct Token(TokenType type, string value, Position position) : IEquatable<Token>
{
    public TokenType Type { get; } = type;
    public string Value { get; } = value ?? string.Empty;
    public Position Position { get; } = position;

    public Token(TokenType type, Position position) : this(type, string.Empty, position)
    {
    }

    public override string ToString()
    {
        if (string.IsNullOrEmpty(Value))
        {
            return $"{Type} @ {Position}";
        }
        return $"{Type}('{Value}') @ {Position}";
    }

    public string ToDetailedString()
    {
        return $"[{Type}] '{Value}' at {Position.ToFullString()}";
    }

    /// <summary>
    /// Checks if the token is a keyword
    /// </summary>
    public bool IsKeyword()
    {
        return Type >= TokenType.CLASS && Type <= TokenType.WHILE;
    }

    /// <summary>
    /// Checks if the token is a literal
    /// </summary>
    public bool IsLiteral()
    {
        return Type == TokenType.INTEGER_LITERAL || 
               Type == TokenType.REAL_LITERAL || 
               Type == TokenType.BOOLEAN_LITERAL;
    }

    /// <summary>
    /// Checks if the token is an operator
    /// </summary>
    public bool IsOperator()
    {
        return Type == TokenType.ASSIGN || 
               Type == TokenType.ARROW ||
               Type == TokenType.DOT;
    }

    /// <summary>
    /// Checks if the token is a separator
    /// </summary>
    public bool IsSeparator()
    {
        return Type == TokenType.COLON ||
               Type == TokenType.COMMA ||
               Type == TokenType.LPAREN ||
               Type == TokenType.RPAREN ||
               Type == TokenType.LBRACKET ||
               Type == TokenType.RBRACKET;
    }

    public bool Equals(Token other)
    {
        return Type == other.Type && 
               Value == other.Value && 
               Position.Equals(other.Position);
    }

    public override bool Equals(object? obj)
    {
        return obj is Token other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Type, Value, Position);
    }

    public static bool operator ==(Token left, Token right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Token left, Token right)
    {
        return !left.Equals(right);
    }
}