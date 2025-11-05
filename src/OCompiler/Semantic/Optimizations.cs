// using OCompiler.Parser;
// namespace OCompiler.Semantic
// {
//     public class Optimizer
//     {
//         private readonly ClassHierarchy _hierarchy;

//         public Optimizer(ClassHierarchy hierarchy)
//         {
//             _hierarchy = hierarchy;
//         }

//         public ProgramNode Optimize(ProgramNode program)
//         {
//             var optimizedClasses = program.Classes.Select(OptimizeClass).ToList();
            
//             // 1. Удаление неиспользуемых методов
//             optimizedClasses = RemoveUnusedMethods(optimizedClasses);
            
//             // 2. Inline простых методов
//             optimizedClasses = InlineSimpleMethods(optimizedClasses);
            
//             // 3. Упрощение цепочек вызовов
//             optimizedClasses = SimplifyCallChains(optimizedClasses);
            
//             // 4. Специализация для стандартных типов
//             optimizedClasses = SpecializeForStandardTypes(optimizedClasses);

//             return new ProgramNode(optimizedClasses);
//         }

//         // 1. Удаление неиспользуемых методов
//         private List<ClassDeclaration> RemoveUnusedMethods(List<ClassDeclaration> classes)
//         {
//             var usedMethods = CollectUsedMethods(classes);
//             var result = new List<ClassDeclaration>();
            
//             foreach (var classDecl in classes)
//             {
//                 var usedMembers = classDecl.Members.Where(member =>
//                     member is not MethodDeclaration method ||
//                     usedMethods.Contains($"{classDecl.Name}.{method.Header.Name}") ||
//                     IsConstructor(method) ||
//                     IsPublicInterface(method)).ToList();
                    
//                 result.Add(new ClassDeclaration(classDecl.Name, classDecl.GenericParameter,
//                     classDecl.BaseClass, usedMembers));
//             }
            
//             return result;
//         }

//         // 2. Inline простых методов
//         private List<ClassDeclaration> InlineSimpleMethods(List<ClassDeclaration> classes)
//         {
//             return classes.Select(classDecl =>
//             {
//                 var inlineCandidates = classDecl.Members.OfType<MethodDeclaration>()
//                     .Where(m => IsInlineCandidate(m))
//                     .ToDictionary(m => m.Header.Name);
                
//                 var optimizedMembers = classDecl.Members.Select(member =>
//                 {
//                     if (member is MethodDeclaration method)
//                     {
//                         return InlineMethodCalls(method, inlineCandidates);
//                     }
//                     return member;
//                 }).ToList();
                
//                 return new ClassDeclaration(classDecl.Name, classDecl.GenericParameter,
//                     classDecl.BaseClass, optimizedMembers);
//             }).ToList();
//         }

//         private MethodDeclaration InlineMethodCalls(MethodDeclaration method, 
//             Dictionary<string, MethodDeclaration> inlineCandidates)
//         {
//             if (method.Body == null) return method;
            
//             var optimizedBody = InlineCallsInBody(method.Body, inlineCandidates);
//             return new MethodDeclaration(method.Header, optimizedBody);
//         }

//         // 3. Упрощение цепочек вызовов
//         private List<ClassDeclaration> SimplifyCallChains(List<ClassDeclaration> classes)
//         {
//             return classes.Select(classDecl =>
//             {
//                 var optimizedMembers = classDecl.Members.Select(member =>
//                 {
//                     return member switch
//                     {
//                         MethodDeclaration method => SimplifyMethodCalls(method),
//                         ConstructorDeclaration constr => SimplifyConstructorCalls(constr),
//                         _ => member
//                     };
//                 }).ToList();
                
//                 return new ClassDeclaration(classDecl.Name, classDecl.GenericParameter,
//                     classDecl.BaseClass, optimizedMembers);
//             }).ToList();
//         }

//         private MethodDeclaration SimplifyMethodCalls(MethodDeclaration method)
//         {
//             if (method.Body == null) return method;
            
//             var optimizedElements = method.Body.Elements.Select(element =>
//             {
//                 if (element is ExpressionStatement exprStmt)
//                 {
//                     var simplifiedExpr = SimplifyExpression(exprStmt.Expression);
//                     return new ExpressionStatement(simplifiedExpr);
//                 }
//                 return element;
//             }).ToList();
            
//             return new MethodDeclaration(method.Header, new MethodBodyNode(optimizedElements));
//         }

//         private ExpressionNode SimplifyExpression(ExpressionNode expr)
//         {
//             return expr switch
//             {
//                 MemberAccessExpression memberAccess => SimplifyMemberAccess(memberAccess),
//                 FunctionalCall funcCall => SimplifyFunctionalCall(funcCall),
//                 _ => expr
//             };
//         }

//         private ExpressionNode SimplifyMemberAccess(MemberAccessExpression expr)
//         {
//             // Упрощение цепочек вида obj.getX().getY() -> obj.getY() если возможно
//             if (expr.Expression is FunctionalCall innerCall && 
//                 innerCall.Target is MemberAccessExpression innerMember)
//             {
//                 // Проверяем можно ли объединить вызовы
//                 if (CanCombineCalls(innerCall, expr.Member))
//                 {
//                     return new FunctionalCall(
//                         innerMember.Expression,
//                         new List<ExpressionNode> { expr.Member }
//                     );
//                 }
//             }
            
//             return expr;
//         }

//         // 4. Специализация для стандартных типов
//         private List<ClassDeclaration> SpecializeForStandardTypes(List<ClassDeclaration> classes)
//         {
//             return classes.Select(classDecl =>
//             {
//                 var optimizedMembers = classDecl.Members.Select(member =>
//                 {
//                     if (member is MethodDeclaration method)
//                     {
//                         return SpecializeMethodForTypes(method);
//                     }
//                     return member;
//                 }).ToList();
                
//                 return new ClassDeclaration(classDecl.Name, classDecl.GenericParameter,
//                     classDecl.BaseClass, optimizedMembers);
//             }).ToList();
//         }

//         private MethodDeclaration SpecializeMethodForTypes(MethodDeclaration method)
//         {
//             // Специализация методов работающих со стандартными типами
//             if (method.Body == null) return method;
            
//             var optimizedElements = method.Body.Elements.Select(element =>
//             {
//                 if (element is ExpressionStatement exprStmt)
//                 {
//                     var specializedExpr = SpecializeExpression(exprStmt.Expression);
//                     return new ExpressionStatement(specializedExpr);
//                 }
//                 return element;
//             }).ToList();
            
//             return new MethodDeclaration(method.Header, new MethodBodyNode(optimizedElements));
//         }

//         private ExpressionNode SpecializeExpression(ExpressionNode expr)
//         {
//             // Замена вызовов методов стандартных классов на более эффективные эквиваленты
//             if (expr is FunctionalCall funcCall && funcCall.Target is IdentifierExpression ident)
//             {
//                 var type = _symbolTable.Lookup(ident.Name)?.Type;
                
//                 // Специализация для Integer
//                 if (type == "Integer" && funcCall.Arguments.Count == 1)
//                 {
//                     return SpecializeIntegerCall(ident.Name, funcCall);
//                 }
                
//                 // Специализация для Boolean
//                 if (type == "Boolean" && funcCall.Arguments.Count == 1)
//                 {
//                     return SpecializeBooleanCall(ident.Name, funcCall);
//                 }
//             }
            
//             return expr;
//         }

//         private ExpressionNode SpecializeIntegerCall(string methodName, FunctionalCall call)
//         {
//             // Замена x.Plus(1) на более эффективную операцию если возможно
//             if (methodName == "Plus" && call.Arguments[0] is IntegerLiteral literal && literal.Value == 1)
//             {
//                 // В реальной реализации здесь была бы замена на инкремент
//                 return call; // Заглушка
//             }
            
//             return call;
//         }

//         // Вспомогательные методы
//         private HashSet<string> CollectUsedMethods(List<ClassDeclaration> classes)
//         {
//             var used = new HashSet<string>();
            
//             foreach (var classDecl in classes)
//             {
//                 // Конструкторы всегда используются
//                 used.Add($"{classDecl.Name}.this");
                
//                 foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
//                 {
//                     if (member.Body != null)
//                     {
//                         CollectMethodCalls(member.Body, used);
//                     }
//                 }
//             }
            
//             return used;
//         }

//         private bool IsInlineCandidate(MethodDeclaration method)
//         {
//             return method.Body != null && 
//                    method.Header.Parameters.Count <= 3 &&
//                    EstimateMethodSize(method.Body) < 10; // Маленькие методы
//         }

//         private int EstimateMethodSize(MethodBodyNode body)
//         {
//             return body.Elements.Count;
//         }

//         private bool CanCombineCalls(FunctionalCall innerCall, ExpressionNode outerMember)
//         {
//             // Проверяем можно ли объединить два последовательных вызова
//             // Например: list.get(0).toString() -> list.toStringAt(0)
//             return innerCall.Target is MemberAccessExpression;
//         }
//     }
// }


using System;
using System.Collections.Generic;
using System.Linq;
using OCompiler.Parser;

namespace OCompiler.Semantic
{
    public class Optimizer
    {
        private readonly ClassHierarchy _hierarchy;

        public Optimizer(ClassHierarchy hierarchy)
        {
            _hierarchy = hierarchy;
        }

        public ProgramNode Optimize(ProgramNode program)
        {
            var optimizedClasses = program.Classes.Select(OptimizeClass).ToList();
            
            // 1. Удаление неиспользуемых методов
            optimizedClasses = RemoveUnusedMethods(optimizedClasses, program);
            
            return new ProgramNode(optimizedClasses);
        }

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
                // Удаление кода после return
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

        // 1. Удаление неиспользуемых методов
        private List<ClassDeclaration> RemoveUnusedMethods(List<ClassDeclaration> classes, ProgramNode program)
        {
            var usedMethods = CollectUsedMethods(classes, program);
            var result = new List<ClassDeclaration>();
            
            // Для отладки - выводим используемые методы
            Console.WriteLine("**[ DEBUG ] Used methods:");
            foreach (var method in usedMethods.OrderBy(m => m))
            {
                Console.WriteLine($"  - {method}");
            }
            
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
                Console.WriteLine($"[DEBUG] Found method call: {fullMethodName}");
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
    }
}