namespace OCompiler.Parser
{
    public abstract class AstNode
    {
        public virtual void Print(string indent = "")
        {
            Console.WriteLine(indent + GetType().Name);
        }
    }

    public class ProgramNode : AstNode
    {
        public List<ClassDeclaration> Classes { get; }
        public ProgramNode(List<ClassDeclaration> classes) => Classes = classes;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ProgramNode");
            foreach (var cls in Classes)
                cls.Print(indent + "  ");
        }
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

        public override void Print(string indent = "")
        {
            Console.WriteLine($"{indent}Class: {Name}, Extends: {Extension ?? "null"}");
            foreach (var member in Members)
                member.Print(indent + "  ");
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
        public override void Print(string indent = "")
        {
            Console.WriteLine($"{indent}VariableDeclaration: {Identifier}");
            Expression?.Print(indent + "  ");
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
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "MethodDeclaration:");
            Header.Print(indent + "  ");
            Body?.Print(indent + "  ");
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
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ConstructorDeclaration:");
            Console.WriteLine(indent + "  Parameters:");
            foreach (var param in Parameters)
                param.Print(indent + "    ");
            Console.WriteLine(indent + "  Body:");
            foreach (var stmt in Body)
                stmt.Print(indent + "    ");
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
        public override void Print(string indent = "")
        {
            Console.WriteLine($"{indent}Assignment: {Identifier}");
            Expression?.Print(indent + "  ");
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
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "WhileLoop:");
            Condition.Print(indent + "  ");
            Console.WriteLine(indent + "  Body:");
            foreach (var stmt in Body)
                stmt.Print(indent + "    ");
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
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "IfStatement:");
            Condition.Print(indent + "  ");
            Console.WriteLine(indent + "  ThenBody:");
            foreach (var stmt in ThenBody)
                stmt.Print(indent + "    ");
            if (ElseBody != null)
            {
                Console.WriteLine(indent + "  ElseBody:");
                ElseBody.Print(indent + "    ");
            }
        }
    }

    public class ElsePart : AstNode
    {
        public List<Statement> Statements { get; }
        public ElsePart(List<Statement> stmts) => Statements = stmts;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ElsePart:");
            foreach (var stmt in Statements)
                stmt.Print(indent + "  ");
        }
    }

    public class ReturnStatement : Statement
    {
        public ExpressionNode Expression { get; }
        public ReturnStatement(ExpressionNode expr) => Expression = expr;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ReturnStatement:");
            Expression?.Print(indent + "  ");
        }
    }

    public abstract class ExpressionNode : AstNode { }

    public class IntegerLiteral : ExpressionNode
    {
        public int Value { get; }
        public IntegerLiteral(int value) => Value = value;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"IntegerLiteral: {Value}");
        }
    }

    public class RealLiteral : ExpressionNode
    {
        public double Value { get; }
        public RealLiteral(double value) => Value = value;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"RealLiteral: {Value}");
        }
    }

    public class BooleanLiteral : ExpressionNode
    {
        public bool Value { get; }
        public BooleanLiteral(bool value) => Value = value;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"BooleanLiteral: {Value}");
        }
    }

    public class ThisExpression : ExpressionNode
    {
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ThisExpression");
        }
    }

    public class IdentifierExpression : ExpressionNode
    {
        public string Name { get; }
        public IdentifierExpression(string name) => Name = name;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"IdentifierExpression: {Name}");
        }
    }

    public class ParameterDeclaration : AstNode
    {
        public string Identifier { get; }
        public string TypeName { get; }
        public ParameterDeclaration(string id, string typeName)
        {
            Identifier = id;
            TypeName = typeName;
        }

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"Parameter: {Identifier} : {TypeName}");
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

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"Method: {Name}, Returns: {ReturnType ?? "void"}");
            foreach(var param in Parameters)
                param.Print(indent + "  ");
        }
    }

    public class MethodBodyNode : AstNode
    {
        public List<Statement> Statements { get; }
        public MethodBodyNode(List<Statement> statements) => Statements = statements;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "MethodBody:");
            foreach(var stmt in Statements)
                stmt.Print(indent + "  ");
        }
    }

    public class ExpressionDotSequence : ExpressionNode
    {
        public List<ExpressionNode> Expressions { get; }
        public ExpressionDotSequence(List<ExpressionNode> expressions) => Expressions = expressions;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ExpressionDotSequence:");
            foreach(var expr in Expressions)
                expr.Print(indent + "  ");
        }
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

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"ConstructorInvocation: {ClassName}");
            foreach(var arg in Arguments)
                arg.Print(indent + "  ");
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

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "FunctionalCall:");
            Function.Print(indent + "  ");
            foreach(var arg in Arguments)
                arg.Print(indent + "  ");
        }
    }
}
