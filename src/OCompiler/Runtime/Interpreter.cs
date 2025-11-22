using System;
using System.Collections.Generic;
using System.Linq;
using OCompiler.Parser;
using OCompiler.Semantic;

namespace OCompiler.Runtime
{
    // Very small interpreter used as a fallback to execute constructor bodies (this())
    // Supports: variable declarations with constructor invocations, expression statements
    // with method calls on objects, simple method returns of literals.
    public class Interpreter
    {
        private readonly ProgramNode _ast;
        private readonly ClassHierarchy _hierarchy;

        public Interpreter(ProgramNode ast, ClassHierarchy hierarchy)
        {
            _ast = ast;
            _hierarchy = hierarchy;
        }

        public void ExecuteConstructorByName(string className)
        {
            var classDecl = _ast.Classes.FirstOrDefault(c => c.Name == className);
            if (classDecl == null)
            {
                Console.WriteLine($"**[ WARN ] Interpreter: class '{className}' not found in AST");
                return;
            }

            var ctor = classDecl.Members.OfType<ConstructorDeclaration>().FirstOrDefault();
            if (ctor == null)
            {
                // nothing to execute
                Console.WriteLine($"**[ INFO ] Interpreter: no explicit constructor for '{className}'");
                return;
            }

            var instance = new ObjectInstance(className);
            var env = new ExecutionEnvironment(instance);

            try
            {
                ExecuteMethodBody(ctor.Body, env);
                Console.WriteLine($"**[ OK ] Interpreter: executed constructor for '{className}'");
            }
            catch (ReturnSignal)
            {
                // ignore return inside constructor
                Console.WriteLine($"**[ OK ] Interpreter: constructor returned for '{className}'");
            }
        }

        private object? ExecuteMethodBody(MethodBodyNode body, ExecutionEnvironment env)
        {
            foreach (var elem in body.Elements)
            {
                switch (elem)
                {
                    case VariableDeclaration vd:
                        var val = EvaluateExpression(vd.Expression, env);
                        env.Locals[vd.Identifier] = val;
                        break;
                    case ExpressionStatement es:
                        EvaluateExpression(es.Expression, env);
                        break;
                    case Assignment asg:
                        var r = EvaluateExpression(asg.Expression, env);
                        if (env.Locals.ContainsKey(asg.Identifier))
                            env.Locals[asg.Identifier] = r;
                        else
                            env.Instance.Fields[asg.Identifier] = r;
                        break;
                    case IfStatement ifs:
                        var cond = EvaluateExpression(ifs.Condition, env);
                        if (cond is bool b && b)
                            ExecuteMethodBody(ifs.ThenBody, env);
                        else if (ifs.ElseBody != null)
                            ExecuteMethodBody(ifs.ElseBody.Body, env);
                        break;
                    case WhileLoop wl:
                        while (true)
                        {
                            var c = EvaluateExpression(wl.Condition, env);
                            if (c is bool bb && bb)
                                ExecuteMethodBody(wl.Body, env);
                            else
                                break;
                        }
                        break;
                    case ReturnStatement rs:
                        var ret = EvaluateExpression(rs.Expression, env);
                        throw new ReturnSignal(ret);
                    default:
                        // unsupported element - ignore
                        break;
                }
            }

            return null;
        }

        private object? EvaluateExpression(ExpressionNode expr, ExecutionEnvironment env)
        {
            switch (expr)
            {
                case IntegerLiteral il:
                    return il.Value;
                case RealLiteral rl:
                    return rl.Value;
                case BooleanLiteral bl:
                    return bl.Value;
                case ThisExpression _:
                    return env.Instance;
                case IdentifierExpression id:
                    if (env.Locals.TryGetValue(id.Name, out var v)) return v;
                    if (env.Instance.Fields.TryGetValue(id.Name, out var f)) return f;
                    return null;
                case ConstructorInvocation ci:
                    var className = ci.ClassName;
                    var inst = new ObjectInstance(className);
                    var newEnv = new ExecutionEnvironment(inst);
                    var classDecl = _ast.Classes.FirstOrDefault(c => c.Name == className);
                    if (classDecl != null)
                    {
                        var ctor = classDecl.Members.OfType<ConstructorDeclaration>().FirstOrDefault();
                        if (ctor != null)
                        {
                            ExecuteMethodBody(ctor.Body, newEnv);
                        }
                    }
                    return inst;
                case FunctionalCall fc:
                    // Expect function to be either IdentifierExpression (global function) or MemberAccessExpression
                    if (fc.Function is MemberAccessExpression mae)
                    {
                        var targetObj = EvaluateExpression(mae.Target, env);
                        var memberExpr = mae.Member as IdentifierExpression;
                        if (memberExpr == null) return null;
                        var methodName = memberExpr.Name;
                        if (targetObj is ObjectInstance oi)
                        {
                            var methodDecl = _hierarchy.FindMethodInHierarchy(methodName, oi.ClassName);
                            if (methodDecl != null)
                            {
                                var methodEnv = new ExecutionEnvironment(oi);
                                // evaluate args and bind as locals with parameter names if available
                                for (int i = 0; i < fc.Arguments.Count; i++)
                                {
                                    var argVal = EvaluateExpression(fc.Arguments[i], env);
                                    if (i < methodDecl.Header.Parameters.Count)
                                    {
                                        var paramName = methodDecl.Header.Parameters[i].Identifier;
                                        methodEnv.Locals[paramName] = argVal;
                                    }
                                }

                                try
                                {
                                    var rv = ExecuteMethodBody(methodDecl.Body, methodEnv);
                                    return rv;
                                }
                                catch (ReturnSignal rs)
                                {
                                    return rs.Value;
                                }
                            }
                        }
                    }
                    else if (fc.Function is IdentifierExpression fid)
                    {
                        // Not supporting free functions - ignore
                        return null;
                    }

                    return null;
                case MemberAccessExpression ma:
                    var targ = EvaluateExpression(ma.Target, env);
                    if (targ is ObjectInstance oi2 && ma.Member is IdentifierExpression mem)
                    {
                        if (oi2.Fields.TryGetValue(mem.Name, out var val2)) return val2;
                    }
                    return null;
                default:
                    return null;
            }
        }

        private class ReturnSignal : Exception
        {
            public object? Value { get; }
            public ReturnSignal(object? value = null) { Value = value; }
        }

        private class ExecutionEnvironment
        {
            public ObjectInstance Instance { get; }
            public Dictionary<string, object?> Locals { get; } = new();
            public ExecutionEnvironment(ObjectInstance instance) { Instance = instance; }
        }

        private class ObjectInstance
        {
            public string ClassName { get; }
            public Dictionary<string, object?> Fields { get; } = new();
            public ObjectInstance(string className) { ClassName = className; }
            public override string ToString() => $"<inst {ClassName}>";
        }
    }
}
