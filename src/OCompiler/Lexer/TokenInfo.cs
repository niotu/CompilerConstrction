using OCompiler.Lexer;

namespace OCompiler.Lexer;
public class TokenValue
{
    public string Str;
    public int Integer;
    public double Real;
    public bool Boolean;
}

public class TokenInfo(Token token, TokenValue value, Position position)
{
    public Token Token { get; } = token;
    public TokenValue Value { get; } = value;
    public Position Position { get; } = position;
}
