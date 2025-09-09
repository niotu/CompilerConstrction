namespace OCompiler.Lexer;

/// <summary>
/// Типы токенов языка O
/// Соответствует спецификации Project O
/// </summary>
public enum TokenType
{
    // Конец файла
    EOF,
    
    // Ключевые слова (в порядке алфавита)
    CLASS,      // class
    ELSE,       // else  
    END,        // end
    EXTENDS,    // extends
    IF,         // if
    IS,         // is
    LOOP,       // loop
    METHOD,     // method
    RETURN,     // return
    THEN,       // then
    THIS,       // this
    VAR,        // var
    WHILE,      // while
    
    // Литералы
    IDENTIFIER,         // имена переменных, классов, методов
    INTEGER_LITERAL,    // 123, 0, -456
    REAL_LITERAL,       // 3.14, 0.5, -2.71
    BOOLEAN_LITERAL,    // true, false
    
    // Операторы и разделители
    ASSIGN,         // :=
    ARROW,          // => (для коротких методов)
    COLON,          // :
    DOT,            // .
    COMMA,          // ,
    
    // Скобки
    LPAREN,         // (
    RPAREN,         // )
    LBRACKET,       // [
    RBRACKET,       // ]
    
    // Комментарии (обычно игнорируются)
    COMMENT,        // // однострочный комментарий
    
    // Ошибка
    UNKNOWN         // неизвестный символ
}