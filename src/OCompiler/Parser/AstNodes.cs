using System;
using System.Collections.Generic;

namespace OCompiler.Parser
{
    public abstract class AstNode
    {
        public virtual void Print(string indent = "")
        {
            Console.WriteLine(indent + GetType().Name);
        }
    }


    public abstract class BodyElement : AstNode { }

    public class ProgramNode(List<ClassDeclaration> classes) : AstNode
    {
        public List<ClassDeclaration> Classes { get; } = classes;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ProgramNode");
            foreach (var cls in Classes)
                cls.Print(indent + "  ");
        }
    }

    public class ClassDeclaration(string name, string genericParam, string extension, List<MemberDeclaration> members) : AstNode
    {
        public string Name { get; } = name;
        public string GenericParameter { get; } = genericParam;
        public string Extension { get; } = extension;
        public List<MemberDeclaration> Members { get; } = members;

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

    public class VariableDeclaration(string identifier, ExpressionNode expr) : MemberDeclaration
    {
        public string Identifier { get; } = identifier;
        public ExpressionNode Expression { get; set; } = expr;

        public override void Print(string indent = "")
        {
            Console.WriteLine($"{indent}VariableDeclaration: {Identifier}");
            Expression?.Print(indent + "  ");
        }
    }

    public class MethodDeclaration(MethodHeaderNode header, MethodBodyNode body) : MemberDeclaration
    {
        public MethodHeaderNode Header { get; } = header;
        public MethodBodyNode Body { get; } = body;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "MethodDeclaration:");
            Header.Print(indent + "  ");
            Console.WriteLine(indent+"  " + "MethodBody:");
            Body?.Print(indent + "  ");
        }
    }

    public class ConstructorDeclaration(List<ParameterDeclaration> parameters, MethodBodyNode body) : MemberDeclaration
    {
        public List<ParameterDeclaration> Parameters { get; } = parameters;
        public MethodBodyNode Body { get; } = body;

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

    public class Assignment(string id, ExpressionNode expr) : Statement
    {
        public string Identifier { get; } = id;
        public ExpressionNode Expression { get; set; } = expr;

        public override void Print(string indent = "")
        {
            Console.WriteLine($"{indent}Assignment: {Identifier}");
            Expression?.Print(indent + "  ");
        }
    }

    public class WhileLoop(ExpressionNode cond, MethodBodyNode body) : Statement
    {
        public ExpressionNode Condition { get; set; } = cond;
        public MethodBodyNode Body { get; set; } = body;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "WhileLoop:");
            Condition.Print(indent + "  ");
            Console.WriteLine(indent + "  WhileLoopBody:");
            Body?.Print(indent + "    ");
        }
    }

    public class IfStatement(ExpressionNode cond, MethodBodyNode thenBody, ElsePart elseBody) : Statement
    {
        public ExpressionNode Condition { get; set; } = cond;
        public MethodBodyNode ThenBody { get; set; } = thenBody;
        public ElsePart ElseBody { get; set; } = elseBody;

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

    public class ElsePart(MethodBodyNode body) : AstNode
    {
        public MethodBodyNode Body { get; } = body;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ElsePart:");
            Body?.Print(indent + "  ");
        }
    }

    public class ReturnStatement(ExpressionNode expr) : Statement
    {
        public ExpressionNode Expression { get; set; } = expr;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ReturnStatement:");
            Expression?.Print(indent + "  ");
        }
    }

    public class MethodBodyNode(List<BodyElement> elements) : AstNode
    {
        public List<BodyElement> Elements { get; } = elements;

        public override void Print(string indent = "")
        {
            foreach(var elem in Elements)
                elem.Print(indent + "  ");
        }
    }

    public abstract class ExpressionNode : AstNode { }

    public class IntegerLiteral(int value) : ExpressionNode
    {
        public int Value { get; } = value;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"IntegerLiteral: {Value}");
        }
    }

    public class RealLiteral(double value) : ExpressionNode
    {
        public double Value { get; } = value;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"RealLiteral: {Value}");
        }
    }

    public class BooleanLiteral(bool value) : ExpressionNode
    {
        public bool Value { get; } = value;

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

    public class IdentifierExpression(string name) : ExpressionNode
    {
        public string Name { get; } = name;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"IdentifierExpression: {Name}");
        }
    }

    public class MemberAccessExpression(ExpressionNode target, ExpressionNode member) : ExpressionNode
    {
        public ExpressionNode Target { get; set; } = target;
        public ExpressionNode Member { get; set; } = member;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "MemberAccessExpression:");
            Console.WriteLine(indent + "  Target:");
            Target.Print(indent + "    ");
            Console.WriteLine(indent + "  Member:");
            Member.Print(indent + "    ");
        }
    }
    public class ExpressionStatement(ExpressionNode expr) : BodyElement
    {
        public ExpressionNode Expression { get; set; } = expr;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ExpressionStatement:");
            Expression.Print(indent + "  ");
        }
    }
    public class ParameterDeclaration(string id, ClassNameNode type) : AstNode
    {
        public string Identifier { get; } = id;
        public ClassNameNode Type { get; } = type;

        public override void Print(string indent = "")
    {
        if (Type.GenericParameter != null)
            Console.WriteLine(indent + $"Parameter: {Identifier} : {Type.Name}[{Type.GenericParameter}]");
        else
            Console.WriteLine(indent + $"Parameter: {Identifier} : {Type.Name}");
    }
    }

    public class MethodHeaderNode(string name, List<ParameterDeclaration> parameters, string returnType) : AstNode
    {
        public string Name { get; } = name;
        public List<ParameterDeclaration> Parameters { get; } = parameters;
        public string ReturnType { get; } = returnType;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + $"Method: {Name}, Returns: {ReturnType ?? "void"}");
            foreach(var param in Parameters)
                param.Print(indent + "  ");
        }
    }
    public class ClassNameNode(string name, string genericParam) : AstNode
    {
        public string Name { get; } = name;
        public string GenericParameter { get; } = genericParam;

        public override void Print(string indent = "")
        {
            if (GenericParameter != null)
                Console.WriteLine(indent + $"ClassName: {Name}[{GenericParameter}]");
            else
                Console.WriteLine(indent + $"ClassName: {Name}");
        }
    }

    public class ExpressionDotSequence(List<ExpressionNode> expressions) : ExpressionNode
    {
        public List<ExpressionNode> Expressions { get; } = expressions;

        public override void Print(string indent = "")
        {
            Console.WriteLine(indent + "ExpressionDotSequence:");
            foreach(var expr in Expressions)
                expr.Print(indent + "  ");
        }
    }

    public class ConstructorInvocation(string className, string genericParam, List<ExpressionNode> args) : ExpressionNode
    {
        public string ClassName { get; } = className;
        public string GenericParameter { get; } = genericParam;
        public List<ExpressionNode> Arguments { get; } = args;

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

    public class FunctionalCall(ExpressionNode function, List<ExpressionNode> args) : ExpressionNode
    {
        public ExpressionNode Function { get; set; } = function;
        public List<ExpressionNode> Arguments { get; set; } = args;

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