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

            classes = RemoveUnusedMethods(classes, program);
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
            
            // НОВОЕ: Добавляем точку входа (main/Main) как используемый метод
            // Он не должен быть удалён оптимизатором
            foreach (var classDecl in classes)
            {
                foreach (var method in classDecl.Members.OfType<MethodDeclaration>())
                {
                    if (method.Header.Name == "main" || method.Header.Name == "Main")
                    {
                        used.Add($"{classDecl.Name}.{method.Header.Name}");
                    }
                }
            }
            
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
                // Проверяем, это ли вызов конструктора (создание объекта вида: Dog() или Animal())
                // Если это прямой вызов IdentifierExpression и это имя класса, то это конструктор
                bool isConstructorCall = false;
                if (funcCall.Function is IdentifierExpression funcIdent)
                {
                    // Это прямой вызов вроде Dog() или Animal()
                    var classDecl = program.Classes.FirstOrDefault(c => c.Name == funcIdent.Name);
                    if (classDecl != null)
                    {
                        // Это вызов конструктора
                        isConstructorCall = true;
                        string fullMethodName = $"{funcIdent.Name}.this";
                        used.Add(fullMethodName);
                    }
                }
                else if (!isConstructorCall)
                {
                    // Это обычный вызов метода. Нужно найти, в каком классе метод реально определён
                    var actualMethodClass = FindMethodClassInHierarchy(methodName, targetClass, program);
                    if (actualMethodClass != null)
                    {
                        string fullMethodName = $"{actualMethodClass}.{methodName}";
                        used.Add(fullMethodName);
                        
                        // Добавляем все переопределённые методы в иерархии наследования 
                        // (методы с тем же именем в производных классах)
                        AddOverridingMethods(targetClass, methodName, used, program);
                    }
                }
            }
            
            // Рекурсивно обрабатываем аргументы
            foreach (var arg in funcCall.Arguments)
            {
                CollectMethodCallsFromExpression(arg, used, currentClass, program);
            }
            
            // Обрабатываем целевое выражение
            CollectMethodCallsFromExpression(funcCall.Function, used, currentClass, program);
        }
        
        private string FindMethodClassInHierarchy(string methodName, string className, ProgramNode program)
        {
            var classDecl = program.Classes.FirstOrDefault(c => c.Name == className);
            while (classDecl != null)
            {
                // Проверяем, есть ли метод в этом классе
                if (classDecl.Members.OfType<MethodDeclaration>().Any(m => m.Header.Name == methodName))
                {
                    return classDecl.Name;
                }
                
                // Идём к базовому классу
                classDecl = string.IsNullOrEmpty(classDecl.Extension) ? 
                    null : program.Classes.FirstOrDefault(c => c.Name == classDecl.Extension);
            }
            return null;
        }
        
        private void AddOverridingMethods(string className, string methodName, HashSet<string> used, ProgramNode program)
        {
            // Ищем все производные классы, которые переопределяют этот метод
            var startClass = program.Classes.FirstOrDefault(c => c.Name == className);
            if (startClass == null)
                return;
            
            // Добавляем все методы с тем же именем в производных классах (те, которые переопределяют базовый)
            foreach (var classDecl in program.Classes)
            {
                // Проверяем, является ли этот класс производным от startClass
                if (IsSubclassOf(classDecl, startClass, program))
                {
                    // Проверяем, переопределяет ли этот класс метод
                    if (classDecl.Members.OfType<MethodDeclaration>().Any(m => m.Header.Name == methodName))
                    {
                        string methodFullName = $"{classDecl.Name}.{methodName}";
                        used.Add(methodFullName);
                    }
                }
            }
        }
        
        private bool IsSubclassOf(ClassDeclaration derived, ClassDeclaration baseClass, ProgramNode program)
        {
            var current = derived;
            while (current != null)
            {
                if (current.Name == baseClass.Name)
                    return true;
                
                if (string.IsNullOrEmpty(current.Extension))
                    break;
                
                current = program.Classes.FirstOrDefault(c => c.Name == current.Extension);
            }
            return false;
        }
        
        private void AddOverriddenMethods(string className, string methodName, HashSet<string> used, ProgramNode program)
        {
            // Ищем класс в иерархии и добавляем метод из всех базовых классов
            var classDecl = program.Classes.FirstOrDefault(c => c.Name == className);
            if (classDecl == null)
                return;
            
            // Идём вверх по иерархии наследования
            string currentBaseClass = classDecl.Extension;
            while (!string.IsNullOrEmpty(currentBaseClass))
            {
                var baseClassDecl = program.Classes.FirstOrDefault(c => c.Name == currentBaseClass);
                if (baseClassDecl == null)
                    break;
                
                // Проверяем, есть ли метод в этом базовом классе
                var methodExists = baseClassDecl.Members.OfType<MethodDeclaration>()
                    .Any(m => m.Header.Name == methodName);
                
                if (methodExists)
                {
                    string methodFullName = $"{currentBaseClass}.{methodName}";
                    used.Add(methodFullName);
                }
                
                // Ищем следующий базовый класс
                currentBaseClass = baseClassDecl.Extension;
            }
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
                        if (varDecl != null)
                        {
                            string varType = ExtractTypeFromExpression(varDecl.Expression);
                            if (varType != null)
                            {
                                return varType;
                            }
                        }
                    }
                    else if (member is MethodDeclaration method && method.Body != null)
                    {
                        var varDecl = FindVariableInBody(method.Body, variableName);
                        if (varDecl != null)
                        {
                            string varType = ExtractTypeFromExpression(varDecl.Expression);
                            if (varType != null)
                            {
                                return varType;
                            }
                        }
                    }
                    else if (member is VariableDeclaration classVar && classVar.Identifier == variableName)
                    {
                        string varType = ExtractTypeFromExpression(classVar.Expression);
                        if (varType != null)
                        {
                            return varType;
                        }
                    }
                }
            }
            
            return currentClass; // fallback
        }

        private string ExtractTypeFromExpression(ExpressionNode expr)
        {
            if (expr is IdentifierExpression typeIdent)
            {
                return typeIdent.Name;
            }
            else if (expr is FunctionalCall funcCall && funcCall.Function is IdentifierExpression funcIdent)
            {
                // Например: Dog() или Animal()
                return funcIdent.Name;
            }
            else if (expr is ConstructorInvocation constrInv)
            {
                return constrInv.ClassName;
            }
            return null;
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
                var folded = TryEvalMethod(func, args) ?? new FunctionalCall(func, args);
                
                // После сворачивания попробуем свернуть нормализованный результат
                if (folded is not FunctionalCall)
                    return folded;
                
                return folded;
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
                if (ci.ClassName == "Integer" && ci.Arguments.Count == 1)
                {
                    if (ci.Arguments[0] is IntegerLiteral lit1)
                        return new IntegerLiteral(lit1.Value);
                    // Integer(5 + 3) where 5 + 3 может быть свёрнуто в другом месте
                }

                // Boolean(true) → BooleanLiteral("true")
                if (ci.ClassName == "Boolean" && ci.Arguments.Count == 1 && ci.Arguments[0] is BooleanLiteral lit2)
                    return new BooleanLiteral(lit2.Value);
            }
            return expr;
        }

        private ExpressionNode NormalizeConstructors(ExpressionNode expr)
        {
            if (expr is FunctionalCall funcCall && funcCall.Function is IdentifierExpression ident)
            {
                // Проверяем, это вызов встроенного типа как конструктора
                var typeName = ident.Name;
                if (typeName is "Integer" or "Real" or "Boolean")
                {
                    // Преобразуем в ConstructorInvocation
                    return new ConstructorInvocation(typeName, null, funcCall.Arguments);
                }
            }
            
            return expr;
        }


        private ExpressionNode TryEvalMethod(ExpressionNode func, List<ExpressionNode> args)
        {
            // Сначала нормализуем левую часть - функция может быть MemberAccessExpression с FunctionalCall или ConstructorInvocation
            if (func is MemberAccessExpression ma)
            {
                // Нормализуем цель
                var normalizedTarget = ma.Target;
                
                // Если это FunctionalCall с IdentifierExpression (вроде Integer(...)), нормализуем его
                if (ma.Target is FunctionalCall fc && fc.Function is IdentifierExpression fcIdent)
                {
                    normalizedTarget = NormalizeFunctionalCall(fc);
                }
                else if (ma.Target is ConstructorInvocation ci)
                {
                    normalizedTarget = NormalizeLiteral(ci);
                }
                
                var left = normalizedTarget;
                var method = ma.Member as IdentifierExpression;
                if (method == null) return null;

                // Теперь пытаемся свернуть методы литеральных значений
                if (args.Count == 1)
                {
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
                            case "LessEqual": return new BooleanLiteral((a <= b));
                            case "GreaterEqual": return new BooleanLiteral((a >= b));
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
            }
            return null;
        }

        private ExpressionNode NormalizeFunctionalCall(FunctionalCall fc)
        {
            if (fc.Function is IdentifierExpression ident && fc.Arguments.Count == 1)
            {
                if (ident.Name == "Integer" && fc.Arguments[0] is IntegerLiteral lit)
                    return new IntegerLiteral(lit.Value);
                if (ident.Name == "Boolean" && fc.Arguments[0] is BooleanLiteral blit)
                    return new BooleanLiteral(blit.Value);
            }
            return fc;
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
            return classes.Select(c => new ClassDeclaration(
                c.Name, c.GenericParameter, c.Extension,
                c.Members.Select(m => RemoveVarsInMember(m)).ToList()
            )).ToList();
        }

        private MemberDeclaration RemoveVarsInMember(MemberDeclaration m)
        {
            if (m is MethodDeclaration md && md.Body != null)
            {
                var newElems = RemoveVarsInBody(md.Body.Elements);
                return new MethodDeclaration(md.Header, new MethodBodyNode(newElems));
            }
            
            if (m is ConstructorDeclaration cd)
            {
                var newElems = RemoveVarsInBody(cd.Body.Elements);
                return new ConstructorDeclaration(cd.Parameters, new MethodBodyNode(newElems));
            }
            
            return m;
        }

        private List<BodyElement> RemoveVarsInBody(IEnumerable<BodyElement> elems)
        {
            var used = new HashSet<string>();
            var elemsList = elems.ToList();
            
            // Первый проход: собираем все переменные, которые используются
            foreach (var e in elemsList)
            {
                CollectUsedVars(e, used);
            }
            
            // Второй проход: удаляем объявления неиспользуемых переменных
            var result = new List<BodyElement>();
            foreach (var e in elemsList)
            {
                if (e is VariableDeclaration v)
                {
                    // Пропускаем объявление, если переменная не используется
                    if (!used.Contains(v.Identifier))
                        continue;
                }
                result.Add(e);
            }
            
            return result;
        }

        private void CollectUsedVars(BodyElement elem, HashSet<string> used)
        {
            switch (elem)
            {
                case Assignment a:
                    // Левая часть присваивания - переменная используется
                    used.Add(a.Identifier);
                    CollectExprVars(a.Expression, used);
                    break;
                    
                case VariableDeclaration v:
                    // Правая часть - переменные в выражении используются
                    CollectExprVars(v.Expression, used);
                    break;
                    
                case ReturnStatement r:
                    if (r.Expression != null)
                        CollectExprVars(r.Expression, used);
                    break;
                    
                case ExpressionStatement s:
                    CollectExprVars(s.Expression, used);
                    break;
                    
                case IfStatement i:
                    CollectExprVars(i.Condition, used);
                    foreach (var elem2 in i.ThenBody.Elements)
                        CollectUsedVars(elem2, used);
                    if (i.ElseBody != null)
                        foreach (var elem2 in i.ElseBody.Body.Elements)
                            CollectUsedVars(elem2, used);
                    break;
                    
                case WhileLoop w:
                    CollectExprVars(w.Condition, used);
                    foreach (var elem2 in w.Body.Elements)
                        CollectUsedVars(elem2, used);
                    break;
            }
        }

        private void CollectExprVars(ExpressionNode expr, HashSet<string> used)
        {
            if (expr is IdentifierExpression id)
            {
                used.Add(id.Name);
            }
            else if (expr is FunctionalCall fc)
            {
                CollectExprVars(fc.Function, used);
                foreach (var arg in fc.Arguments)
                    CollectExprVars(arg, used);
            }
            else if (expr is MemberAccessExpression ma)
            {
                CollectExprVars(ma.Target, used);
                CollectExprVars(ma.Member, used);
            }
            else if (expr is ConstructorInvocation ci)
            {
                foreach (var arg in ci.Arguments)
                    CollectExprVars(arg, used);
            }
        }

    }
}
