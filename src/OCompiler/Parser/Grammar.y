%using OCompiler.Lexer
%namespace OCompiler.Parser

%union {
    string str;
    int integer;
    double real;
    bool boolean;
}


%token <str> IDENTIFIER
%token <integer> INTEGER_LITERAL
%token <real> REAL_LITERAL
%token <boolean> BOOLEAN_LITERAL

%token 
    // End of file
    EOF
    
    // Keywords (in alphabetical order)
    CLASS      // class
    ELSE       // else  
    END        // end
    EXTENDS    // extends
    IF         // if
    IS         // is
    LOOP       // loop
    METHOD     // method
    RETURN     // return
    THEN       // then
    THIS       // this
    VAR        // var
    WHILE      // while

    // Operators and separators
    ASSIGN         // :=
    ARROW          // => (for short methods)
    COLON          // :
    DOT            // .
    COMMA          // ,
    
    // Brackets
    LPAREN         // (
    RPAREN         // )
    LBRACKET       // [
    RBRACKET       // ]

    // Comments (usually ignored)
    COMMENT        // // single-line comment

    // Error
    UNKNOWN         // unknown character


%start Program

%%

Program
    : ClassDeclarations EOF
    ;

ClassDeclarations
    : ClassDeclaration
    | ClassDeclaration ClassDeclarations
    ;

ClassDeclaration
    : CLASS ClassName Extension IS ClassBody END
    ;

ClassName 
    : IDENTIFIER Generic
    ;

Generic 
    : /* empty*/
    | LBRACKET ClassName RBRACKET
    ;

Extension 
    : /* empty */
    | EXTENDS IDENTIFIER
    ; 

ClassBody
    : MemberDeclaration
    | MemberDeclaration ClassBody

MemberDeclaration
    : VariableDeclaration
    | MethodDeclaration
    | ConstructorDeclaration

VariableDeclaration
    : VAR IDENTIFIER COLON Expression
    ;

MethodDeclaration
    : MethodHeader OptionalMethodBody
    ;

OptionalMethodBody
    : /* empty */
    | MethodBody
    ;
    
MethodHeader
    : METHOD IDENTIFIER OptionalParameters ReturnType
    ;

OptionalParameters
    : /* empty */
    | Parameters
    ;

ReturnType
    : /* empty */
    | COLON IDENTIFIER
    ;
    

MethodBody
    : IS Body END
    | ARROW Expression
    ;

Parameters
    : LPAREN ParameterDeclarations RPAREN
    ;

ParameterDeclarations
    : ParameterDeclaration
    | ParameterDeclaration COMMA ParameterDeclarations
    ;

ParameterDeclaration
    : IDENTIFIER COLON ClassName
    ;

Body 
    : VariableDeclaration
    | Statement
    ;

ConstructorDeclaration
    : THIS OptionalParameters IS Body END
    ;

Statement 
    : Assignment
    | WhileLoop
    | IfStatement
    | ReturnStatement
    ;

Assignment
    : IDENTIFIER ASSIGN Expression
    ;

WhileLoop
    : WHILE Expression LOOP Body END
    ;

IfStatement
    : IF Expression THEN Body ElsePart END
    ;

ElsePart
    : /* empty */
    | ELSE Body
    ;

ReturnStatement
    : RETURN ReturningExpression
    ;

ReturningExpression
    : /* empty */
    | Expression
    ;

Expression 
    : Primary
    | ConstructorInvocation
    | FunctionalCall
    | ExpressionDotSequence
    ;

ExpressionDotSequence
    : Expression
    | Expression DOT ExpressionDotSequence
    ;

ConstructorInvocation
    : ClassName Arguments // Тут вопрос: опциональные аргументы и еще в объявлении аргумента 
                        //   есть пустые скобки (то есть в моем понимании тут тоже аргументы опциональны) 
                        //это разве не двойная работа?
    ;

FunctionalCall
    : Expression Arguments
    ;

Arguments
    : LPAREN RPAREN
    | LPAREN ExpressionCommaSequence RPAREN
    ;

ExpressionCommaSequence
    : Expression
    | Expression DOT ExpressionCommaSequence
    ;

Primary
    : INTEGER_LITERAL
    | REAL_LITERAL
    | BOOLEAN_LITERAL
    | THIS
    ;



%%