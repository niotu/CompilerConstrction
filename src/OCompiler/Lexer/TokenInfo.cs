using OCompiler.Lexer;

namespace OCompiler.Lexer;
public class TokenValue
{
    public string Str;
    public int Integer;
    public double Real;
    public bool Boolean;
}

public class TokenInfo
{
    public Token Token { get; }
    public TokenValue Value { get; }
    public Position Position { get; }

    public TokenInfo(Token token, TokenValue value, Position position)
    {
        Token = token;
        Value = value;
        Position = position;
    }
}
