namespace OCompiler.Parser
{
    public abstract class AstNode
    {
        public virtual void Print(string indent = "")
        {
            Console.WriteLine(indent + GetType().Name);
        }
    }

    // Базовый класс для всех элементов внутри Body
    public abstract class BodyElement : AstNode { }

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
        public string GenericParameter { get; }
        public string Extension { get; }
        public List<MemberDeclaration> Members { get; }

        public ClassDeclaration(string name, string genericParam, string extension, List<MemberDeclaration> members)
        {
            Name = name;
            GenericParameter = genericParam;
            Extension = extension;
            Members = members;
        }

        public override void Print(string indent = "")
        {
            if (GenericParameter != null)
                Console.WriteLine($"{indent}Class: {Name}[{GenericParameter}], Extends: {Extension ?? "null"}");
            else
                Console.WriteLine($"{indent}Class: {Name}, Extends: {Extension ?? "null"}");
                
            foreach (var member in Members)
                member.Print(indent + "  ");
        }
    }

    public abstract class MemberDeclaration : BodyElement { }

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
            Console.WriteLine(indent+"  " + "MethodBody:");
            Body?.Print(indent + "  ");
        }
    }

    public class ConstructorDeclaration : MemberDeclaration
    {
        public List<ParameterDeclaration> Parameters { get; }
        public MethodBodyNode Body { get; }
        public ConstructorDeclaration(List<ParameterDeclaration> parameters, MethodBodyNode body)
        {
            Parameters = parameters;
            Body = body;
        }
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ConstructorDeclaration:");
            if (Parameters.Count == 0){
                Console.WriteLine(indent+ "  " + "Parameters: empty");
            } else {
                Console.WriteLine(indent+ "  " + "Parameters:");
                foreach(var param in Parameters)
                    param.Print(indent + "    ");
            }
            Console.WriteLine(indent + "  ConstructorBody:");
            Body?.Print(indent + "    ");
        }
    }

    public abstract class Statement : BodyElement { }

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
        public MethodBodyNode Body { get; }
        public WhileLoop(ExpressionNode cond, MethodBodyNode body)
        {
            Condition = cond;
            Body = body;
        }
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "WhileLoop:");
            Condition.Print(indent + "  ");
            Console.WriteLine(indent + "  WhileLoopBody:");
            Body?.Print(indent + "    ");
        }
    }

    public class IfStatement : Statement
    {
        public ExpressionNode Condition { get; }
        public MethodBodyNode ThenBody { get; }
        public ElsePart ElseBody { get; }
        public IfStatement(ExpressionNode cond, MethodBodyNode thenBody, ElsePart elseBody)
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
            ThenBody?.Print(indent + "    ");
            if (ElseBody != null)
            {
                Console.WriteLine(indent + "  ElseBody:");
                ElseBody.Print(indent + "    ");
            }
        }
    }

    public class ElsePart : AstNode
    {
        public MethodBodyNode Body { get; }
        public ElsePart(MethodBodyNode body) => Body = body;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ElsePart:");
            Body?.Print(indent + "  ");
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

    public class MethodBodyNode : AstNode
    {
        public List<BodyElement> Elements { get; }
        public MethodBodyNode(List<BodyElement> elements) => Elements = elements;

        public override void Print(string indent = "")
        {
            foreach(var elem in Elements)
                elem.Print(indent + "  ");
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

    public class MemberAccessExpression : ExpressionNode
    {
        public ExpressionNode Target { get; }
        public ExpressionNode Member { get; }
        
        public MemberAccessExpression(ExpressionNode target, ExpressionNode member)
        {
            Target = target;
            Member = member;
        }

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "MemberAccessExpression:");
            Console.WriteLine(indent + "  Target:");
            Target.Print(indent + "    ");
            Console.WriteLine(indent + "  Member:");
            Member.Print(indent + "    ");
        }
    }
    public class ExpressionStatement : BodyElement
    {
        public ExpressionNode Expression { get; }
        public ExpressionStatement(ExpressionNode expr) => Expression = expr;
        
        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ExpressionStatement:");
            Expression.Print(indent + "  ");
        }
    }
    public class ParameterDeclaration : AstNode
    {
        public string Identifier { get; }
        public ClassNameNode Type { get; }
        public ParameterDeclaration(string id, ClassNameNode type)
        {
            Identifier = id;
            Type = type;
        }

        public override void Print(string indent = "")
    {
        if (Type.GenericParameter != null)
            Console.WriteLine(indent + $"Parameter: {Identifier} : {Type.Name}[{Type.GenericParameter}]");
        else
            Console.WriteLine(indent + $"Parameter: {Identifier} : {Type.Name}");
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
    public class ClassNameNode : AstNode
    {
        public string Name { get; }
        public string GenericParameter { get; }
        
        public ClassNameNode(string name, string genericParam)
        {
            Name = name;
            GenericParameter = genericParam;
        }

        public override void Print(string indent = "")
        {
            if (GenericParameter != null)
                Console.WriteLine(indent + $"ClassName: {Name}[{GenericParameter}]");
            else
                Console.WriteLine(indent + $"ClassName: {Name}");
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
        public string GenericParameter { get; }
        public List<ExpressionNode> Arguments { get; }
        
        public ConstructorInvocation(string className, string genericParam, List<ExpressionNode> args)
        {
            ClassName = className;
            GenericParameter = genericParam;
            Arguments = args;
        }

        public override void Print(string indent = "")
        {
            if (GenericParameter != null)
                Console.WriteLine(indent + $"ConstructorInvocation: {ClassName}[{GenericParameter}]");
            else
                Console.WriteLine(indent + $"ConstructorInvocation: {ClassName}");
            
            if (Arguments.Count == 0){
                Console.WriteLine(indent+ "  " + "Arguments: empty");
            } else {
                Console.WriteLine(indent+ "  " + "Arguments:");
                foreach(var arg in Arguments)
                    arg.Print(indent + "    ");
            }
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
            if (Arguments.Count == 0){
                Console.WriteLine(indent+ "  " + "Arguments: empty");
            } else {
                Console.WriteLine(indent+ "  " + "Arguments:");
                foreach(var arg in Arguments)
                    arg.Print(indent + "    ");
            }
        }
    }
}