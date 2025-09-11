using System.Text;

namespace OCompiler.Lexer;

public class OLexer
{
    private readonly string _sourceCode;
    private readonly string _fileName;
    private int _position;
    private int _line;
    private int _column;
    
    // Keywords mapping to token types 
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "class", TokenType.CLASS },
        { "else", TokenType.ELSE },
        { "end", TokenType.END },
        { "extends", TokenType.EXTENDS },
        { "if", TokenType.IF },
        { "is", TokenType.IS },
        { "loop", TokenType.LOOP },
        { "method", TokenType.METHOD },
        { "return", TokenType.RETURN },
        { "then", TokenType.THEN },
        { "this", TokenType.THIS },
        { "var", TokenType.VAR },
        { "while", TokenType.WHILE },
        
        // Boolean literals are treated as keywords too
        { "true", TokenType.BOOLEAN_LITERAL },
        { "false", TokenType.BOOLEAN_LITERAL }
    };

    public OLexer(string sourceCode, string fileName)
    {
        _sourceCode = sourceCode ?? throw new ArgumentNullException(nameof(sourceCode));
        _fileName = fileName ?? "<unknown>";
        _position = 0;
        _line = 1;
        _column = 1;
    }

    /// <summary>
    /// Tokenize whole source code
    /// </summary>
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        
        while (!IsAtEnd())
        {
            try
            {
                var token = NextToken();
                if (token.Type != TokenType.COMMENT) // Ignore comments
                {
                    tokens.Add(token);
                }
            }
            catch (LexerException)
            {
                // Re-post lexical errors as they are
                throw;
            }
            catch (Exception ex)
            {
                //Wrap up all other errors
                throw new LexerException($"Internal lexer error: {ex.Message}", 
                    CurrentPosition(), ex);
            }
        }
        
        // Add token of file ending
        tokens.Add(new Token(TokenType.EOF, CurrentPosition()));
        
        return tokens;
    }

    /// <summary>
    /// Get next token from stream
    /// </summary>
    public Token NextToken()
    {
        SkipWhitespace();
        
        if (IsAtEnd())
        {
            return new Token(TokenType.EOF, CurrentPosition());
        }

        var startPosition = CurrentPosition();
        char current = Peek();

        // Comments
        if (current == '/' && PeekNext() == '/')
        {
            return ReadComment(startPosition);
        }

        // Two-symbol operators
        if (current == ':' && PeekNext() == '=')
        {
            Advance(2);
            return new Token(TokenType.ASSIGN, ":=", startPosition);
        }
        
        if (current == '=' && PeekNext() == '>')
        {
            Advance(2);
            return new Token(TokenType.ARROW, "=>", startPosition);
        }

        // Single-character operators and delimiters
        switch (current)
        {
            case ':':
                Advance();
                return new Token(TokenType.COLON, ":", startPosition);
            case '.':
                Advance();
                return new Token(TokenType.DOT, ".", startPosition);
            case ',':
                Advance();
                return new Token(TokenType.COMMA, ",", startPosition);
            case '(':
                Advance();
                return new Token(TokenType.LPAREN, "(", startPosition);
            case ')':
                Advance();
                return new Token(TokenType.RPAREN, ")", startPosition);
            case '[':
                Advance();
                return new Token(TokenType.LBRACKET, "[", startPosition);
            case ']':
                Advance();
                return new Token(TokenType.RBRACKET, "]", startPosition);
        }

        // Numeric
        if (char.IsDigit(current))
        {
            return ReadNumber(startPosition);
        }

        // Identificators and keywords
        if (char.IsLetter(current) || current == '_')
        {
            return ReadIdentifierOrKeyword(startPosition);
        }

        // Unknown symbol - error
        throw new LexerException($"Unexpected symbol: '{current}' (code: {(int)current})", 
            startPosition);
    }

    #region Reading specific token types

    private Token ReadComment(Position startPosition)
    {
        var comment = new StringBuilder("//");
        
        // Skip "//"
        Advance(2);
        
        // Read to the end of the line
        while (!IsAtEnd() && Peek() != '\n' && Peek() != '\r')
        {
            comment.Append(Peek());
            Advance();
        }
        
        return new Token(TokenType.COMMENT, comment.ToString(), startPosition);
    }

    private Token ReadNumber(Position startPosition)
    {
        var number = new StringBuilder();
        bool isReal = false;
        
        // Read integer part
        while (!IsAtEnd() && char.IsDigit(Peek()))
        {
            number.Append(Peek());
            Advance();
        }
        
        // Check for fractional part with separator '.'
        if (!IsAtEnd() && Peek() == '.' && char.IsDigit(PeekNext()))
        {
            isReal = true;
            number.Append(Peek()); // добавляем точку
            Advance();
            
            // Read fractional part
            while (!IsAtEnd() && char.IsDigit(Peek()))
            {
                number.Append(Peek());
                Advance();
            }
        }
        
        string value = number.ToString();
        TokenType type = isReal ? TokenType.REAL_LITERAL : TokenType.INTEGER_LITERAL;
        
        return new Token(type, value, startPosition);
    }

    private Token ReadIdentifierOrKeyword(Position startPosition)
    {
        var identifier = new StringBuilder();

        // First character - letter or underscore
        // Next - letters, digits or underscores
        while (!IsAtEnd() && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
        {
            identifier.Append(Peek());
            Advance();
        }
        
        string value = identifier.ToString();

        // Check if the identifier is a keyword
        if (Keywords.TryGetValue(value, out var keywordType))
        {
            return new Token(keywordType, value, startPosition);
        }
        
        // Default identifier
        return new Token(TokenType.IDENTIFIER, value, startPosition);
    }

    #endregion

    #region Auxiliary methods

    private void SkipWhitespace()
    {
        while (!IsAtEnd() && char.IsWhiteSpace(Peek()))
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 0; // Will be incremented in Advance()
            }
            Advance();
        }
    }

    private bool IsAtEnd()
    {
        return _position >= _sourceCode.Length;
    }

    private char Peek()
    {
        return IsAtEnd() ? '\0' : _sourceCode[_position];
    }

    private char PeekNext()
    {
        return _position + 1 >= _sourceCode.Length ? '\0' : _sourceCode[_position + 1];
    }

    private char PeekAhead(int offset)
    {
        int pos = _position + offset;
        return pos >= _sourceCode.Length ? '\0' : _sourceCode[pos];
    }

    private void Advance()
    {
        if (!IsAtEnd())
        {
            _position++;
            _column++;
        }
    }
    
    private void Advance(int count)
    {
        for (int i = 0; i < count && !IsAtEnd(); i++)
        {
            Advance();
        }
    }

    private Position CurrentPosition()
    {
        return new Position(_line, _column, _fileName);
    }

    #endregion

    #region Debugging methods

    /// <summary>
    /// Get context around current position for error reporting
    /// </summary>
    public string GetContext(int contextSize = 20)
    {
        if (IsAtEnd()) return "<EOF>";
        
        int start = Math.Max(0, _position - contextSize);
        int end = Math.Min(_sourceCode.Length, _position + contextSize);
        
        var context = _sourceCode.Substring(start, end - start);
        var markerPosition = _position - start;
        
        var lines = context.Split('\n');
        var result = new StringBuilder();
        
        for (int i = 0; i < lines.Length; i++)
        {
            result.AppendLine(lines[i]);
            if (i == 0 && markerPosition < lines[i].Length)
            {
                result.AppendLine(new string(' ', markerPosition) + "^");
            }
        }
        
        return result.ToString();
    }

    public override string ToString()
    {
        return $"OLexer at {CurrentPosition()}";
    }

    #endregion
}