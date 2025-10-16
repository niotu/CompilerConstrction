namespace OCompiler.Parser
{
    public abstract class AstNode { }

    public class ProgramNode : AstNode
    {
        public List<ClassDeclaration> Classes { get; }
        public ProgramNode(List<ClassDeclaration> classes) => Classes = classes;
    }

    public class ClassDeclaration : AstNode
    {
        public string Name { get; }
        public string Extension { get; }
        public List<MemberDeclaration> Members { get; }
        public ClassDeclaration(string name, string extension, List<MemberDeclaration> members)
        {
            Name = name;
            Extension = extension;
            Members = members;
        }
    }

    public abstract class MemberDeclaration : AstNode { }

    public class VariableDeclaration : MemberDeclaration
    {
        public string Identifier { get; }
        public ExpressionNode Expression { get; }
        public VariableDeclaration(string identifier, ExpressionNode expr)
        {
            Identifier = identifier;
            Expression = expr;
        }
    }

    public class MethodDeclaration : MemberDeclaration
    {
        public MethodHeaderNode Header { get; }
        public MethodBodyNode Body { get; }
        public MethodDeclaration(MethodHeaderNode header, MethodBodyNode body)
        {
            Header = header;
            Body = body;
        }
    }

    public class ConstructorDeclaration : MemberDeclaration
    {
        public List<ParameterDeclaration> Parameters { get; }
        public List<Statement> Body { get; }
        public ConstructorDeclaration(List<ParameterDeclaration> parameters, List<Statement> body)
        {
            Parameters = parameters;
            Body = body;
        }
    }

    public abstract class Statement : AstNode { }

    public class Assignment : Statement
    {
        public string Identifier { get; }
        public ExpressionNode Expression { get; }
        public Assignment(string id, ExpressionNode expr)
        {
            Identifier = id;
            Expression = expr;
        }
    }

    public class WhileLoop : Statement
    {
        public ExpressionNode Condition { get; }
        public List<Statement> Body { get; }
        public WhileLoop(ExpressionNode cond, List<Statement> body)
        {
            Condition = cond;
            Body = body;
        }
    }

    public class IfStatement : Statement
    {
        public ExpressionNode Condition { get; }
        public List<Statement> ThenBody { get; }
        public ElsePart ElseBody { get; }
        public IfStatement(ExpressionNode cond, List<Statement> thenBody, ElsePart elseBody)
        {
            Condition = cond;
            ThenBody = thenBody;
            ElseBody = elseBody;
        }
    }

    public class ElsePart : AstNode
    {
        public List<Statement> Statements { get; }
        public ElsePart(List<Statement> stmts) => Statements = stmts;
    }

    public class ReturnStatement : Statement
    {
        public ExpressionNode Expression { get; }
        public ReturnStatement(ExpressionNode expr) => Expression = expr;
    }

    public abstract class ExpressionNode : AstNode { }

    public class IntegerLiteral : ExpressionNode
    {
        public int Value { get; }
        public IntegerLiteral(int value) => Value = value;
    }

    public class RealLiteral : ExpressionNode
    {
        public double Value { get; }
        public RealLiteral(double value) => Value = value;
    }

    public class BooleanLiteral : ExpressionNode
    {
        public bool Value { get; }
        public BooleanLiteral(bool value) => Value = value;
    }

    public class ThisExpression : ExpressionNode { }

    public class IdentifierExpression : ExpressionNode
    {
        public string Name { get; }
        public IdentifierExpression(string name) => Name = name;
    }

    // Параметры, метод, тело и др. классы по аналогии
    public class ParameterDeclaration : AstNode
    {
        public string Identifier { get; }
        public string TypeName { get; }
        public ParameterDeclaration(string id, string typeName)
        {
            Identifier = id;
            TypeName = typeName;
        }
    }

    public class MethodHeaderNode : AstNode
    {
        public string Name { get; }
        public List<ParameterDeclaration> Parameters { get; }
        public string ReturnType { get; }
        public MethodHeaderNode(string name, List<ParameterDeclaration> parameters, string returnType)
        {
            Name = name;
            Parameters = parameters;
            ReturnType = returnType;
        }
    }

    public class MethodBodyNode : AstNode
    {
        public List<Statement> Statements { get; }
        public MethodBodyNode(List<Statement> statements) => Statements = statements;
    }

    public class ExpressionDotSequence : ExpressionNode
    {
        public List<ExpressionNode> Expressions { get; }
        public ExpressionDotSequence(List<ExpressionNode> expressions) => Expressions = expressions;
    }

    public class ConstructorInvocation : ExpressionNode
    {
        public string ClassName { get; }
        public List<ExpressionNode> Arguments { get; }
        public ConstructorInvocation(string className, List<ExpressionNode> args)
        {
            ClassName = className;
            Arguments = args;
        }
    }

    public class FunctionalCall : ExpressionNode
    {
        public ExpressionNode Function { get; }
        public List<ExpressionNode> Arguments { get; }
        public FunctionalCall(ExpressionNode function, List<ExpressionNode> args)
        {
            Function = function;
            Arguments = args;
        }
    }
}
