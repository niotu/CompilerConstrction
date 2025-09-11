namespace OCompiler.Lexer;

/// <summary>
/// O language token types
/// Corresponds to Project O specification
/// </summary>
public enum TokenType
{
    // End of file
    EOF,
    
    // Keywords (in alphabetical order)
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

    // Literals
    IDENTIFIER,         // variable, class, method names
    INTEGER_LITERAL,    // 123, 0, -456
    REAL_LITERAL,       // 3.14, 0.5, -2.71
    BOOLEAN_LITERAL,    // true, false

    // Operators and separators
    ASSIGN,         // :=
    ARROW,          // => (for short methods)
    COLON,          // :
    DOT,            // .
    COMMA,          // ,
    
    // Brackets
    LPAREN,         // (
    RPAREN,         // )
    LBRACKET,       // [
    RBRACKET,       // ]

    // Comments (usually ignored)
    COMMENT,        // // single-line comment

    // Error
    UNKNOWN         // unknown character
}