namespace OCompiler.Lexer;

/// <summary>
/// Токен языка O
/// </summary>
public readonly struct Token : IEquatable<Token>
{
    public TokenType Type { get; }
    public string Value { get; }
    public Position Position { get; }

    public Token(TokenType type, string value, Position position)
    {
        Type = type;
        Value = value ?? string.Empty;
        Position = position;
    }

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
    /// Проверяет, является ли токен ключевым словом
    /// </summary>
    public bool IsKeyword()
    {
        return Type >= TokenType.CLASS && Type <= TokenType.WHILE;
    }

    /// <summary>
    /// Проверяет, является ли токен литералом
    /// </summary>
    public bool IsLiteral()
    {
        return Type == TokenType.INTEGER_LITERAL || 
               Type == TokenType.REAL_LITERAL || 
               Type == TokenType.BOOLEAN_LITERAL;
    }

    /// <summary>
    /// Проверяет, является ли токен оператором
    /// </summary>
    public bool IsOperator()
    {
        return Type == TokenType.ASSIGN || 
               Type == TokenType.ARROW ||
               Type == TokenType.DOT;
    }

    /// <summary>
    /// Проверяет, является ли токен разделителем
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