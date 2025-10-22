%using OCompiler.Lexer
%namespace OCompiler.Parser


%union {
    public string str;
    public int integer;
    public double real;
    public bool boolean;
    public object ast;
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
    { 
        $$.ast = new ProgramNode((List<ClassDeclaration>)$1.ast); 
    }
    ;

ClassDeclarations
    : ClassDeclaration
    { 
        $$.ast = new List<ClassDeclaration> { (ClassDeclaration)$1.ast }; 
    }
    | ClassDeclaration ClassDeclarations
    { 
        var list = (List<ClassDeclaration>)$2.ast;
        list.Insert(0, (ClassDeclaration)$1.ast);
        $$.ast = list;
    }
    ;

ClassDeclaration
    : CLASS ClassName Extension IS ClassBody END
    { 
        var classNameNode = (ClassNameNode)$2.ast;
        $$.ast = new ClassDeclaration(classNameNode.Name, classNameNode.GenericParameter, (string)$3.str, (List<MemberDeclaration>)$5.ast);
    }
    ;

ClassName 
    : IDENTIFIER Generic
    {
        $$.ast = new ClassNameNode((string)$1, (string)$2.str);
    }
    ;

Generic 
    : /* empty*/
    {
        $$.str = null;
    }
    | LBRACKET ClassName RBRACKET
    {
        $$.str = $2.str;
    }
    ;

Extension 
    : /* empty */
    {
        $$.str = null;
    }
    | EXTENDS IDENTIFIER
    {
        $$.str = $2;
    }
    ;

ClassBody
    : MemberDeclaration
    {
        $$.ast = new List<MemberDeclaration> { (MemberDeclaration)$1.ast };
    }
    | MemberDeclaration ClassBody
    {
        var list = (List<MemberDeclaration>)$2.ast;
        list.Insert(0, (MemberDeclaration)$1.ast);
        $$.ast = list;
    }
    ;

MemberDeclaration
    : VariableDeclaration
    {
        $$.ast = $1.ast;
    }
    | MethodDeclaration
    {
        $$.ast = $1.ast;
    }
    | ConstructorDeclaration
    {
        $$.ast = $1.ast;
    }
    ;

VariableDeclaration
    : VAR IDENTIFIER COLON Expression
    {
        $$.ast = new VariableDeclaration((string)$2, (ExpressionNode)$4.ast);
    }
    ;

MethodDeclaration
    : MethodHeader OptionalMethodBody
    {
        $$.ast = new MethodDeclaration((MethodHeaderNode)$1.ast, (MethodBodyNode)$2.ast);
    }
    ;

OptionalMethodBody
    : /* empty */
    {
        $$.ast = null;
    }
    | MethodBody
    {
        $$.ast = $1.ast;
    }
    ;

MethodHeader
    : METHOD IDENTIFIER OptionalParameters ReturnType
    {
        $$.ast = new MethodHeaderNode((string)$2, (List<ParameterDeclaration>)$3.ast, (string)$4.str);
    }
    ;

OptionalParameters
    : /* empty */
    {
        $$.ast = new List<ParameterDeclaration>();
    }
    | Parameters
    {
        $$.ast = $1.ast;
    }
    ;

ReturnType
    : /* empty */
    {
        $$.str = null;
    }
    | COLON IDENTIFIER
    {
        $$.str = $2;
    }
    ;

MethodBody
    : IS Body END
    {
        $$.ast = $2.ast;
    }
    | ARROW Expression
    {
        $$.ast = $2.ast;
    }
    ;

Parameters
    : LPAREN ParameterDeclarations RPAREN
    {
        $$.ast = $2.ast;
    }
    ;

ParameterDeclarations
    : /* empty */
    {
        $$.ast = new List<ParameterDeclaration>();
    }
    | ParameterDeclaration
    {
        $$.ast = new List<ParameterDeclaration>{ (ParameterDeclaration)$1.ast };
    }
    | ParameterDeclaration COMMA ParameterDeclarations
    {
        var list = (List<ParameterDeclaration>)$3.ast;
        list.Insert(0, (ParameterDeclaration)$1.ast);
        $$.ast = list;
    }
    ;

ParameterDeclaration
    : IDENTIFIER COLON ClassName
    {
        $$.ast = new ParameterDeclaration((string)$1, (ClassNameNode)$3.ast);
    }
    ;

Body 
    : /* empty */
    {
        $$.ast = new MethodBodyNode(new List<BodyElement>());
    }
    | BodyElement
    {
        $$.ast = new MethodBodyNode(new List<BodyElement> { (BodyElement)$1.ast });
    }
    | Body BodyElement
    {
        var body = (MethodBodyNode)$1.ast;
        body.Elements.Add((BodyElement)$2.ast);
        $$.ast = body;
    }
    ;

BodyElement
    : VariableDeclaration
    {
        $$.ast = $1.ast;
    }
    | Statement
    {
        $$.ast = $1.ast;
    }
    | Expression 
    {
        $$.ast = new ExpressionStatement((ExpressionNode)$1.ast);
    }
    ;

ConstructorDeclaration
    : THIS OptionalParameters IS Body END
    {
        $$.ast = new ConstructorDeclaration((List<ParameterDeclaration>)$2.ast, (MethodBodyNode)$4.ast);
    }
    ;

Statement
    : Assignment
    {
        $$.ast = $1.ast;
    }
    | WhileLoop
    {
        $$.ast = $1.ast;
    }
    | IfStatement
    {
        $$.ast = $1.ast;
    }
    | ReturnStatement
    {
        $$.ast = $1.ast;
    }
    ;

Assignment
    : IDENTIFIER ASSIGN Expression
    {
        $$.ast = new Assignment((string)$1, (ExpressionNode)$3.ast);
    }
    ;

WhileLoop
    : WHILE Expression LOOP Body END
    {
        $$.ast = new WhileLoop((ExpressionNode)$2.ast, (MethodBodyNode)$4.ast);
    }
    ;

IfStatement
    : IF Expression THEN Body ElsePart END
    {
        $$.ast = new IfStatement((ExpressionNode)$2.ast, (MethodBodyNode)$4.ast, (ElsePart)$5.ast);
    }
    ;

ElsePart
    : /* empty */
    {
        $$.ast = null;
    }
    | ELSE Body
    {
        $$.ast = new ElsePart((MethodBodyNode)$2.ast);
    }
    ;

ReturnStatement
    : RETURN ReturningExpression
    {
        $$.ast = new ReturnStatement((ExpressionNode)$2.ast);
    }
    ;

ReturningExpression
    : /* empty */
    {
        $$.ast = null;
    }
    | Expression
    {
        $$.ast = $1.ast;
    }
    ;

Expression
    : Primary
    {
        $$.ast = $1.ast;
    }
    | ConstructorInvocation
    {
        $$.ast = $1.ast;
    }
    | FunctionalCall
    {
        $$.ast = $1.ast;
    }
    | MemberAccess
    {
        $$.ast = $1.ast;
    }
    | ExpressionDotSequence
    {
        $$.ast = $1.ast;
    }
    ;

MemberAccess
    : Expression DOT IDENTIFIER
    {
        $$.ast = new MemberAccessExpression((ExpressionNode)$1.ast, new IdentifierExpression((string)$3));
    }
    | ExpressionDotSequence DOT IDENTIFIER  
    {
        $$.ast = new MemberAccessExpression((ExpressionNode)$1.ast, new IdentifierExpression((string)$3));
    }
    ;

ExpressionDotSequence
    : MemberAccess DOT ExpressionDotSequence
    {
        $$.ast = new MemberAccessExpression((ExpressionNode)$1.ast, (ExpressionNode)$3.ast);
    }
    | MemberAccess
    {
        $$.ast = $1.ast;
    }
    | FunctionalCall
    {
        $$.ast = $1.ast;
    }
    ;

ConstructorInvocation
    : ClassName Arguments
    {
        var classNameNode = (ClassNameNode)$1.ast;
        $$.ast = new ConstructorInvocation(classNameNode.Name, classNameNode.GenericParameter, (List<ExpressionNode>)$2.ast);
    }
    ;

FunctionalCall
    : MemberAccess Arguments
    {
        $$.ast = new FunctionalCall((ExpressionNode)$1.ast, (List<ExpressionNode>)$2.ast);
    }
    | ExpressionDotSequence Arguments
    {
        $$.ast = new FunctionalCall((ExpressionNode)$1.ast, (List<ExpressionNode>)$2.ast);
    }
    | IDENTIFIER Arguments 
    {
        $$.ast = new FunctionalCall(new IdentifierExpression((string)$1), (List<ExpressionNode>)$2.ast);
    }
    ;

Arguments
    : LPAREN RPAREN
    {
        $$.ast = new List<ExpressionNode>();
    }
    | LPAREN ExpressionCommaSequence RPAREN
    {
        $$.ast = $2.ast;
    }
    ;

ExpressionCommaSequence
    : Expression
    {
        $$.ast = new List<ExpressionNode>{ (ExpressionNode)$1.ast };
    }
    | Expression COMMA ExpressionCommaSequence
    {
        var list = (List<ExpressionNode>)$3.ast;
        list.Insert(0, (ExpressionNode)$1.ast);
        $$.ast = list;
    }
    ;

Primary
    : INTEGER_LITERAL
    {
        $$.ast = new IntegerLiteral((int)$1);
    }
    | REAL_LITERAL
    {
        $$.ast = new RealLiteral((double)$1);
    }
    | BOOLEAN_LITERAL
    {
        $$.ast = new BooleanLiteral((bool)$1);
    }
    | THIS
    {
        $$.ast = new ThisExpression();
    }
    | IDENTIFIER
    {
        $$.ast = new IdentifierExpression((string)$1);
    }
    ;

%%

