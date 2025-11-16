using System;
using System.Collections.Generic;
using System.Linq;
using OCompiler.Parser;

namespace OCompiler.Semantic
{
    public class Optimizer
    {
        public ProgramNode Optimize(ProgramNode program)
        {
            var classes = program.Classes.ToList();

            // classes = RemoveUnusedMethods(classes, program);
            classes = ConstantFold(classes);
            classes = SimplifyConditionals(classes);
            classes = RemoveUnreachableCode(classes);
            classes = RemoveUnusedVariables(classes);

            classes = ConstantFold(classes);
            classes = SimplifyConditionals(classes);
            classes = RemoveUnreachableCode(classes);

            return new ProgramNode(classes);
        }


        // 1) 
                private ClassDeclaration OptimizeClass(ClassDeclaration classDecl)
        {
            var optimizedMembers = classDecl.Members.Select(member =>
            {
                return member switch
                {
                    MethodDeclaration method => OptimizeMethod(method),
                    _ => member
                };
            }).ToList();
            
            return new ClassDeclaration(
                classDecl.Name, 
                classDecl.GenericParameter, 
                classDecl.Extension, 
                optimizedMembers
            );
        }

        private MethodDeclaration OptimizeMethod(MethodDeclaration method)
        {
            if (method.Body == null) return method;
            
            var optimizedElements = new List<BodyElement>();
            bool foundReturn = false;
            
            foreach (var element in method.Body.Elements)
            {

                if (foundReturn)
                {
                    continue;
                }
                
                if (element is ReturnStatement)
                {
                    foundReturn = true;
                }
                
                optimizedElements.Add(element);
            }
            
            var optimizedBody = new MethodBodyNode(optimizedElements);
            return new MethodDeclaration(method.Header, optimizedBody);
        }


        private List<ClassDeclaration> RemoveUnusedMethods(List<ClassDeclaration> classes, ProgramNode program)
        {
            var usedMethods = CollectUsedMethods(classes, program);
            var result = new List<ClassDeclaration>();

            
            foreach (var classDecl in classes)
            {
                var usedMembers = classDecl.Members.Where(member =>
                {
                    if (member is MethodDeclaration method)
                    {
                        var fullName = $"{classDecl.Name}.{method.Header.Name}";
                        
                        // Конструкторы всегда используются
                        if (IsConstructor(method))
                            return true;
                            
                        // Проверяем, используется ли метод
                        bool isUsed = usedMethods.Contains(fullName);
                        
                        if (!isUsed)
                        {
                            Console.WriteLine($"[OPTIMIZE] Removing unused method: {fullName}");
                        }
                        
                        return isUsed;
                    }
                    
                    // Все остальные члены (переменные) оставляем
                    return true;
                }).ToList();
                    
                result.Add(new ClassDeclaration(
                    classDecl.Name, 
                    classDecl.GenericParameter,
                    classDecl.Extension, 
                    usedMembers
                ));
            }
            
            return result;
        }

        private bool IsConstructor(MethodDeclaration method)
        {
            return method.Header.Name == "this";
        }

        private HashSet<string> CollectUsedMethods(List<ClassDeclaration> classes, ProgramNode program)
        {
            var used = new HashSet<string>();
            
            // Сначала собираем все вызовы методов из тел методов и конструкторов
            foreach (var classDecl in classes)
            {
                foreach (var member in classDecl.Members)
                {
                    if (member is MethodDeclaration method && method.Body != null)
                    {
                        CollectMethodCallsFromBody(method.Body, used, classDecl.Name, program);
                    }
                    else if (member is ConstructorDeclaration constructor)
                    {
                        CollectMethodCallsFromBody(constructor.Body, used, classDecl.Name, program);
                    }
                }
            }
            
            return used;
        }

        private void CollectMethodCallsFromBody(MethodBodyNode body, HashSet<string> used, string currentClass, ProgramNode program)
        {
            foreach (var element in body.Elements)
            {
                CollectMethodCallsFromElement(element, used, currentClass, program);
            }
        }

        private void CollectMethodCallsFromElement(BodyElement element, HashSet<string> used, string currentClass, ProgramNode program)
        {
            switch (element)
            {
                case ExpressionStatement exprStmt:
                    CollectMethodCallsFromExpression(exprStmt.Expression, used, currentClass, program);
                    break;
                    
                case Assignment assignment:
                    CollectMethodCallsFromExpression(assignment.Expression, used, currentClass, program);
                    break;
                    
                case VariableDeclaration varDecl:
                    CollectMethodCallsFromExpression(varDecl.Expression, used, currentClass, program);
                    break;
                    
                case WhileLoop whileLoop:
                    CollectMethodCallsFromExpression(whileLoop.Condition, used, currentClass, program);
                    CollectMethodCallsFromBody(whileLoop.Body, used, currentClass, program);
                    break;
                    
                case IfStatement ifStmt:
                    CollectMethodCallsFromExpression(ifStmt.Condition, used, currentClass, program);
                    CollectMethodCallsFromBody(ifStmt.ThenBody, used, currentClass, program);
                    if (ifStmt.ElseBody != null)
                    {
                        CollectMethodCallsFromBody(ifStmt.ElseBody.Body, used, currentClass, program);
                    }
                    break;
                    
                case ReturnStatement returnStmt:
                    if (returnStmt.Expression != null)
                    {
                        CollectMethodCallsFromExpression(returnStmt.Expression, used, currentClass, program);
                    }
                    break;
            }
        }

        private void CollectMethodCallsFromExpression(ExpressionNode expr, HashSet<string> used, string currentClass, ProgramNode program)
        {
            switch (expr)
            {
                case FunctionalCall funcCall:
                    // Обрабатываем вызов функции
                    ProcessFunctionCall(funcCall, used, currentClass, program);
                    break;
                    
                case MemberAccessExpression memberAccess:
                    // Обрабатываем доступ к члену (может быть вызовом метода)
                    CollectMethodCallsFromExpression(memberAccess.Target, used, currentClass, program);
                    CollectMethodCallsFromExpression(memberAccess.Member, used, currentClass, program);
                    break;
                    
                case ConstructorInvocation constr:
                    // Конструкторы считаются используемыми
                    used.Add($"{constr.ClassName}.this");
                    foreach (var arg in constr.Arguments)
                    {
                        CollectMethodCallsFromExpression(arg, used, currentClass, program);
                    }
                    break;
                    
                case IdentifierExpression ident:
                    // Идентификаторы сами по себе не являются вызовами методов
                    break;
                    
                default:
                    // Для других типов выражений рекурсивно обрабатываем дочерние выражения
                    ProcessChildExpressions(expr, used, currentClass, program);
                    break;
            }
        }

        private void ProcessChildExpressions(ExpressionNode expr, HashSet<string> used, string currentClass, ProgramNode program)
        {
            var properties = expr.GetType().GetProperties();
            foreach (var prop in properties)
            {
                if (typeof(ExpressionNode).IsAssignableFrom(prop.PropertyType))
                {
                    var childExpr = prop.GetValue(expr) as ExpressionNode;
                    if (childExpr != null)
                    {
                        CollectMethodCallsFromExpression(childExpr, used, currentClass, program);
                    }
                }
                else if (typeof(IEnumerable<ExpressionNode>).IsAssignableFrom(prop.PropertyType))
                {
                    var childExprs = prop.GetValue(expr) as IEnumerable<ExpressionNode>;
                    if (childExprs != null)
                    {
                        foreach (var childExpr in childExprs)
                        {
                            CollectMethodCallsFromExpression(childExpr, used, currentClass, program);
                        }
                    }
                }
            }
        }

        private void ProcessFunctionCall(FunctionalCall funcCall, HashSet<string> used, string currentClass, ProgramNode program)
        {
            // Определяем класс, к которому принадлежит метод
            string targetClass = DetermineTargetClass(funcCall.Function, currentClass, program);
            string methodName = DetermineMethodName(funcCall.Function);
            
            if (targetClass != null && methodName != null)
            {
                string fullMethodName = $"{targetClass}.{methodName}";
                used.Add(fullMethodName);

            }
            
            // Рекурсивно обрабатываем аргументы
            foreach (var arg in funcCall.Arguments)
            {
                CollectMethodCallsFromExpression(arg, used, currentClass, program);
            }
            
            // Обрабатываем целевое выражение
            CollectMethodCallsFromExpression(funcCall.Function, used, currentClass, program);
        }

        private string DetermineTargetClass(ExpressionNode function, string currentClass, ProgramNode program)
        {
            switch (function)
            {
                case IdentifierExpression ident:
                    // Прямой вызов метода - используем текущий класс
                    return currentClass;
                    
                case MemberAccessExpression memberAccess:
                    // Вызов метода через объект: obj.Method()
                    if (memberAccess.Target is IdentifierExpression targetIdent)
                    {
                        // Определяем тип переменной
                        return DetermineVariableType(targetIdent.Name, currentClass, program);
                    }
                    else if (memberAccess.Target is MemberAccessExpression nestedMemberAccess)
                    {
                        // Рекурсивно определяем тип для вложенных выражений
                        return DetermineTargetClass(nestedMemberAccess, currentClass, program);
                    }
                    break;
            }
            
            return currentClass; // fallback
        }

        private string DetermineVariableType(string variableName, string currentClass, ProgramNode program)
        {
            // Ищем объявление переменной в текущем классе
            var classDecl = program.Classes.FirstOrDefault(c => c.Name == currentClass);
            if (classDecl != null)
            {
                // Ищем объявление переменной в конструкторе или методах
                foreach (var member in classDecl.Members)
                {
                    if (member is ConstructorDeclaration constructor)
                    {
                        var varDecl = FindVariableInBody(constructor.Body, variableName);
                        if (varDecl != null && varDecl.Expression is IdentifierExpression typeIdent)
                        {
                            return typeIdent.Name;
                        }
                    }
                    else if (member is MethodDeclaration method && method.Body != null)
                    {
                        var varDecl = FindVariableInBody(method.Body, variableName);
                        if (varDecl != null && varDecl.Expression is IdentifierExpression typeIdent)
                        {
                            return typeIdent.Name;
                        }
                    }
                    else if (member is VariableDeclaration classVar && classVar.Identifier == variableName)
                    {
                        if (classVar.Expression is IdentifierExpression typeIdent)
                        {
                            return typeIdent.Name;
                        }
                    }
                }
            }
            
            return currentClass; // fallback
        }

        private VariableDeclaration? FindVariableInBody(MethodBodyNode body, string variableName)
        {
            foreach (var element in body.Elements)
            {
                if (element is VariableDeclaration varDecl && varDecl.Identifier == variableName)
                {
                    return varDecl;
                }
            }
            return null;
        }

        private string DetermineMethodName(ExpressionNode function)
        {
            switch (function)
            {
                case IdentifierExpression ident:
                    return ident.Name;
                    
                case MemberAccessExpression memberAccess:
                    if (memberAccess.Member is IdentifierExpression memberIdent)
                    {
                        return memberIdent.Name;
                    }
                    break;
            }
            
            return null;
        }
        private List<ClassDeclaration> ConstantFold(List<ClassDeclaration> classes)
        {
            return classes.Select(c => new ClassDeclaration(
                c.Name, c.GenericParameter, c.Extension,
                c.Members.Select(OptimizeMemberExpressions).ToList()
            )).ToList();
        }

        private MemberDeclaration OptimizeMemberExpressions(MemberDeclaration m)
        {
            switch (m)
            {
                case MethodDeclaration md when md.Body != null:
                    return new MethodDeclaration(md.Header,
                        new MethodBodyNode(md.Body.Elements.Select(ConstantFoldElement).ToList())
                    );

                case ConstructorDeclaration cd:
                    return new ConstructorDeclaration(cd.Parameters,
                        new MethodBodyNode(cd.Body.Elements.Select(ConstantFoldElement).ToList())
                    );
            }
            return m;
        }

        private BodyElement ConstantFoldElement(BodyElement e)
        {
            switch (e)
            {
                case Assignment a:
                    return new Assignment(a.Identifier, Fold(a.Expression));

                case VariableDeclaration v:
                    return new VariableDeclaration(v.Identifier, Fold(v.Expression));

                case ExpressionStatement s:
                    return new ExpressionStatement(Fold(s.Expression));

                case ReturnStatement r when r.Expression != null:
                    return new ReturnStatement(Fold(r.Expression));

                case IfStatement i:
                    return new IfStatement(
                        Fold(i.Condition),
                        new MethodBodyNode(i.ThenBody.Elements.Select(ConstantFoldElement).ToList()),
                        i.ElseBody == null ? null :
                            new ElsePart(new MethodBodyNode(i.ElseBody.Body.Elements.Select(ConstantFoldElement).ToList()))
                    );

                case WhileLoop w:
                    return new WhileLoop(
                        Fold(w.Condition),
                        new MethodBodyNode(w.Body.Elements.Select(ConstantFoldElement).ToList())
                    );
            }
            return e;
        }

        private ExpressionNode Fold(ExpressionNode expr)
        {

            if (expr is FunctionalCall call)
            {
                var func = Fold(call.Function);
                var args = call.Arguments.Select(Fold).ToList();
                return TryEvalMethod(func, args) ?? new FunctionalCall(func, args);
            }
            if (expr is MemberAccessExpression ma)
                return new MemberAccessExpression(Fold(ma.Target), Fold(ma.Member));
            if (expr is ConstructorInvocation ci)
                return new ConstructorInvocation(ci.ClassName, ci.GenericParameter, ci.Arguments.Select(Fold).ToList());

            return NormalizeLiteral(expr);
        }
        private ExpressionNode NormalizeLiteral(ExpressionNode expr)
        {
            if (expr is ConstructorInvocation ci)
            {
                // Integer(5) → IntegerLiteral("5")
                if (ci.ClassName == "Integer" && ci.Arguments.Count == 1 && ci.Arguments[0] is IntegerLiteral lit1)
                    return new IntegerLiteral(lit1.Value);

                // Boolean(true) → BooleanLiteral("true")
                if (ci.ClassName == "Boolean" && ci.Arguments.Count == 1 && ci.Arguments[0] is BooleanLiteral lit2)
                    return new BooleanLiteral(lit2.Value);
            }
            return expr;
        }

        private ExpressionNode TryEvalMethod(ExpressionNode func, List<ExpressionNode> args)
        {
            // <literal>.Method(<literal>)
            if (func is MemberAccessExpression ma && args.Count == 1)
            {
                var left = ma.Target;
                var method = ma.Member as IdentifierExpression;
                if (method == null) return null;

                if (IsIntLit(left) && IsIntLit(args[0]))
                {
                    int a = ((IntegerLiteral)left).Value;
                    int b = ((IntegerLiteral)args[0]).Value;

                    switch (method.Name)
                    {
                        case "Plus": return new IntegerLiteral((a + b));
                        case "Minus": return new IntegerLiteral((a - b));
                        case "Mult": return new IntegerLiteral((a * b));
                        case "Div": return b != 0 ? new IntegerLiteral((a / b)) : null;
                        case "Less": return new BooleanLiteral((a < b));
                        case "Greater": return new BooleanLiteral((a > b));
                        case "Equal": return new BooleanLiteral((a == b));
                    }
                }

                if (IsBoolLit(left) && IsBoolLit(args[0]))
                {
                    bool a = ((BooleanLiteral)left).Value;
                    bool b = ((BooleanLiteral)args[0]).Value;

                    switch (method.Name)
                    {
                        case "And": return new BooleanLiteral((a && b));
                        case "Or": return new BooleanLiteral((a || b));
                        case "Xor": return new BooleanLiteral((a ^ b));
                        case "Equal": return new BooleanLiteral((a == b));
                    }
                }
            }
            return null;
        }

        private bool IsIntLit(ExpressionNode e) => e is IntegerLiteral;
        private bool IsBoolLit(ExpressionNode e) => e is BooleanLiteral;

        // ============================================
        // 2) Simplify IF / WHILE when condition is literal
        // ============================================

        private List<ClassDeclaration> SimplifyConditionals(List<ClassDeclaration> classes)
        {
            return classes.Select(c => new ClassDeclaration(
                c.Name, c.GenericParameter, c.Extension,
                c.Members.Select(SimplifyInMember).ToList()
            )).ToList();
        }

        private MemberDeclaration SimplifyInMember(MemberDeclaration m)
        {
            if (m is MethodDeclaration md && md.Body != null)
                return new MethodDeclaration(md.Header, new MethodBodyNode(SimplifyList(md.Body.Elements)));

            if (m is ConstructorDeclaration cd)
                return new ConstructorDeclaration(cd.Parameters, new MethodBodyNode(SimplifyList(cd.Body.Elements)));

            return m;
        }

        private List<BodyElement> SimplifyList(IEnumerable<BodyElement> elems)
        {
            var list = new List<BodyElement>();
            foreach (var e in elems)
            {
                if (e is IfStatement i && i.Condition is BooleanLiteral b)
                {
                    if (b.Value)
                        list.AddRange(i.ThenBody.Elements);
                    else if (i.ElseBody != null)
                        list.AddRange(i.ElseBody.Body.Elements);
                    continue;
                }
                if (e is WhileLoop w && w.Condition is BooleanLiteral bl && !bl.Value)
                    continue;

                list.Add(e);
            }
            return list;
        }

        // ============================================
        // 3) Remove unreachable code after return
        // ============================================

        private List<ClassDeclaration> RemoveUnreachableCode(List<ClassDeclaration> classes)
        {
            return classes.Select(c => new ClassDeclaration(
                c.Name, c.GenericParameter, c.Extension,
                c.Members.Select(RemoveInMember).ToList()
            )).ToList();
        }

        private MemberDeclaration RemoveInMember(MemberDeclaration m)
        {
            if (m is MethodDeclaration md && md.Body != null)
                return new MethodDeclaration(md.Header, new MethodBodyNode(RemoveReturnTail(md.Body.Elements)));

            if (m is ConstructorDeclaration cd)
                return new ConstructorDeclaration(cd.Parameters, new MethodBodyNode(RemoveReturnTail(cd.Body.Elements)));

            return m;
        }

        private List<BodyElement> RemoveReturnTail(IEnumerable<BodyElement> elems)
        {
            var res = new List<BodyElement>();
            foreach (var e in elems)
            {
                res.Add(e);
                if (e is ReturnStatement)
                    break;
            }
            return res;
        }

        // ============================================
        // 4) Remove unused variables
        // ============================================

        private List<ClassDeclaration> RemoveUnusedVariables(List<ClassDeclaration> classes)
        {
            var used = new HashSet<string>();
            foreach (var c in classes)
                foreach (var m in c.Members.OfType<MethodDeclaration>())
                    if (m.Body != null)
                        CollectUsed(m.Body.Elements, used);

            return classes.Select(c => new ClassDeclaration(
                c.Name, c.GenericParameter, c.Extension,
                c.Members.Select(m => RemoveVars(m, used)).ToList()
            )).ToList();
        }

        private void CollectUsed(IEnumerable<BodyElement> elems, HashSet<string> used)
        {
            foreach (var e in elems)
            {
                switch (e)
                {
                    case Assignment a:
                        used.Add(a.Identifier);
                        CollectExpr(a.Expression); break;
                    case VariableDeclaration v:
                        CollectExpr(v.Expression); break;
                    case ReturnStatement r:
                        if (r.Expression != null) CollectExpr(r.Expression); break;
                    case ExpressionStatement s:
                        CollectExpr(s.Expression); break;
                }
            }

            void CollectExpr(ExpressionNode e)
            {
                if (e is IdentifierExpression id) used.Add(id.Name);
                if (e is FunctionalCall fc)
                {
                    CollectExpr(fc.Function);
                    foreach (var a in fc.Arguments) CollectExpr(a);
                }
                if (e is MemberAccessExpression ma)
                {
                    CollectExpr(ma.Target);
                    CollectExpr(ma.Member);
                }
            }
        }

        private MemberDeclaration RemoveVars(MemberDeclaration m, HashSet<string> used)
        {
            if (m is MethodDeclaration md && md.Body != null)
            {
                var newElems = md.Body.Elements
                    .Where(e => !(e is VariableDeclaration v && !used.Contains(v.Identifier)))
                    .ToList();
                return new MethodDeclaration(md.Header, new MethodBodyNode(newElems));
            }
            return m;
        }

    }
}
