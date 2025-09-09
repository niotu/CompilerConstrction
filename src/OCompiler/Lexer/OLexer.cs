using System.Text;

namespace OCompiler.Lexer;

/// <summary>
/// Лексический анализатор для языка O
/// Handwritten lexer с поддержкой всех конструкций языка O
/// </summary>
public class OLexer
{
    private readonly string _sourceCode;
    private readonly string _fileName;
    private int _position;
    private int _line;
    private int _column;
    
    // Словарь ключевых слов языка O
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
        
        // Булевы литералы тоже считаются ключевыми словами
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
    /// Токенизирует весь исходный код
    /// </summary>
    public List<Token> Tokenize()
    {
        var tokens = new List<Token>();
        
        while (!IsAtEnd())
        {
            try
            {
                var token = NextToken();
                if (token.Type != TokenType.COMMENT) // Игнорируем комментарии
                {
                    tokens.Add(token);
                }
            }
            catch (LexerException)
            {
                // Перебрасываем лексические ошибки как есть
                throw;
            }
            catch (Exception ex)
            {
                // Все остальные ошибки оборачиваем
                throw new LexerException($"Внутренняя ошибка лексера: {ex.Message}", 
                    CurrentPosition(), ex);
            }
        }
        
        // Добавляем токен конца файла
        tokens.Add(new Token(TokenType.EOF, CurrentPosition()));
        
        return tokens;
    }

    /// <summary>
    /// Получает следующий токен из потока
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

        // Комментарии
        if (current == '/' && PeekNext() == '/')
        {
            return ReadComment(startPosition);
        }

        // Двухсимвольные операторы
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

        // Односимвольные операторы и разделители
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

        // Числовые литералы
        if (char.IsDigit(current))
        {
            return ReadNumber(startPosition);
        }

        // Идентификаторы и ключевые слова
        if (char.IsLetter(current) || current == '_')
        {
            return ReadIdentifierOrKeyword(startPosition);
        }

        // Неизвестный символ - ошибка
        throw new LexerException($"Неожиданный символ: '{current}' (код: {(int)current})", 
            startPosition);
    }

    #region Чтение конкретных типов токенов

    private Token ReadComment(Position startPosition)
    {
        var comment = new StringBuilder("//");
        
        // Пропускаем "//"
        Advance(2);
        
        // Читаем до конца строки
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
        
        // Читаем целую часть
        while (!IsAtEnd() && char.IsDigit(Peek()))
        {
            number.Append(Peek());
            Advance();
        }
        
        // Проверяем наличие десятичной точки
        if (!IsAtEnd() && Peek() == '.' && char.IsDigit(PeekNext()))
        {
            isReal = true;
            number.Append(Peek()); // добавляем точку
            Advance();
            
            // Читаем дробную часть
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
        
        // Первый символ - буква или подчеркивание
        // Далее - буквы, цифры или подчеркивания
        while (!IsAtEnd() && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
        {
            identifier.Append(Peek());
            Advance();
        }
        
        string value = identifier.ToString();
        
        // Проверяем, является ли идентификатор ключевым словом
        if (Keywords.TryGetValue(value, out var keywordType))
        {
            return new Token(keywordType, value, startPosition);
        }
        
        // Обычный идентификатор
        return new Token(TokenType.IDENTIFIER, value, startPosition);
    }

    #endregion

    #region Вспомогательные методы

    private void SkipWhitespace()
    {
        while (!IsAtEnd() && char.IsWhiteSpace(Peek()))
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 0; // Будет увеличено в Advance()
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

    #region Отладочные методы

    /// <summary>
    /// Получает контекст вокруг текущей позиции для отладки
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