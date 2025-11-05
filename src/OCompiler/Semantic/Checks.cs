using OCompiler.Parser;
using System;
using System.Collections.Generic;
using System.Linq;
using OCompiler.Semantic.Types;

namespace OCompiler.Semantic
{
    public class SemanticChecker
    {
        private readonly SymbolTable _symbolTable = new();
        private readonly List<string> _errors = new();
        private readonly List<string> _warnings = new();
        public IReadOnlyList<string> Warnings => _warnings;
        private string? _currentClass;
        private string? _currentMethod;
        private readonly ClassHierarchy _hierarchy;
        private bool _inLoop = false;

        public IReadOnlyList<string> Errors => _errors;

        public SemanticChecker(ClassHierarchy hierarchy)
        {
            _hierarchy = hierarchy;
        }

        public void Check(ProgramNode program)
        {
            // 1. Проверка корректности использования ключевых слов
            CheckKeywordUsage(program);
            
            // 2. Проверка объявлений перед использованием
            CheckDeclarationsBeforeUsage(program);
            
            // 3. Проверка иерархии классов
            CheckClassHierarchy(program);
            
            // 4. Проверка переопределения методов
            CheckMethodOverriding(program);
            
            // 5. Проверка типов
            CheckTypeCompatibility(program);
            
            // 6. Проверка вызовов конструкторов
            CheckConstructorCalls(program);
            
            // 7. Проверка forward declarations
            CheckForwardDeclarations(program);
            
            // 8. Проверка использования this
            CheckThisUsage(program);
            
            // 9. Проверка возвращаемых значений
            CheckReturnStatements(program);
            
            // 10. Проверка границ массивов
            CheckArrayBounds(program);
            
            // 11. Дополнительные проверки
            CheckAdditionalSemantics(program);
        }

        // 1. Correct Keyword Usage
        private void CheckKeywordUsage(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                _currentClass = classDecl.Name;
                
                // Проверка что 'extends' используется только если есть базовый класс
                if (!string.IsNullOrEmpty(classDecl.Extension) && 
                    !_hierarchy.ClassExists(classDecl.Extension))
                {
                    _errors.Add($"Invalid 'extends' usage: base class '{classDecl.Extension}' not found");
                }
                
                foreach (var member in classDecl.Members)
                {
                    CheckMemberKeywordUsage(member, classDecl);
                }
            }
        }

        private void CheckMemberKeywordUsage(MemberDeclaration member, ClassDeclaration classDecl = null)
        {
            switch (member)
            {
                case MethodDeclaration method:
                    CheckMethodKeywordUsage(method);
                    break;
                    
                case ConstructorDeclaration constructor:
                    CheckConstructorKeywordUsage(constructor);
                    break;
                    
                case VariableDeclaration varDecl:
                    CheckVariableKeywordUsage(varDecl, classDecl);
                    break;
            }
        }

        private void CheckMethodKeywordUsage(MethodDeclaration method)
        {
            _currentMethod = method.Header.Name;
            _inLoop = false;
            
            if (method.Body != null)
            {
                CheckBodyKeywordUsage(method.Body);
            }
        }

        private void CheckConstructorKeywordUsage(ConstructorDeclaration constructor)
        {
            _currentMethod = "this";
            _inLoop = false;
            
            CheckBodyKeywordUsage(constructor.Body);
        }

        private void CheckBodyKeywordUsage(MethodBodyNode body)
        {
            foreach (var element in body.Elements)
            {
                switch (element)
                {
                    case WhileLoop whileLoop:
                        var previousLoopState = _inLoop;
                        _inLoop = true;
                        CheckBodyKeywordUsage(whileLoop.Body);
                        _inLoop = previousLoopState;
                        break;
                        
                    case ReturnStatement returnStmt:
                        // 'return' должен быть только внутри методов
                        if (string.IsNullOrEmpty(_currentMethod) || _currentMethod == "this")
                        {
                            _errors.Add("'return' statement outside of method");
                        }
                        break;
                        
                    case ExpressionStatement exprStmt:
                        CheckExpressionKeywordUsage(exprStmt.Expression);
                        break;
                }
            }
        }

        private void CheckExpressionKeywordUsage(ExpressionNode expr)
        {
            if (expr is ThisExpression)
            {
                // 'this' должен быть только внутри классов
                if (string.IsNullOrEmpty(_currentClass))
                {
                    _errors.Add("'this' keyword used outside of class context");
                }
            }
            
            // Рекурсивная проверка для составных выражений
            if (expr is MemberAccessExpression memberAccess)
            {
                CheckExpressionKeywordUsage(memberAccess.Target);
            }
            else if (expr is FunctionalCall funcCall)
            {
                CheckExpressionKeywordUsage(funcCall.Function);
                foreach (var arg in funcCall.Arguments)
                {
                    CheckExpressionKeywordUsage(arg);
                }
            }
        }

        private void CheckVariableKeywordUsage(VariableDeclaration varDecl, ClassDeclaration classDecl = null)
        {
            // 1. Проверка что 'var' имеет инициализатор
            if (varDecl.Expression == null)
            {
                _errors.Add($"Variable '{varDecl.Identifier}' must have initializer");
                return;
            }

            // 2. Проверка что тип инициализатора может быть выведен
            // Исключение: если это generic-параметр класса, это нормально
            if (varDecl.Expression is IdentifierExpression ident)
            {
                if (classDecl != null && !string.IsNullOrEmpty(classDecl.GenericParameter) && 
                    ident.Name == classDecl.GenericParameter)
                {
                    // Это generic-параметр класса - это валидно
                    return;
                }
            }
            
            var initializerType = InferExpressionType(varDecl.Expression);
            if (initializerType == "Unknown")
            {
                _errors.Add($"Cannot infer type for variable '{varDecl.Identifier}' from expression");
            }

            // 3. Проверка что инициализатор не является 'var' выражением (если такое возможно)
            if (varDecl.Expression is VariableDeclaration)
            {
                _errors.Add($"Cannot use 'var' in variable initializer for '{varDecl.Identifier}'");
            }
        }

        // 2. Declarations Before Usage
        private void CheckDeclarationsBeforeUsage(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                _currentClass = classDecl.Name;
                _symbolTable.EnterScope();

                // Обрабатываем переменные уровня класса
                var classVariables = classDecl.Members.OfType<VariableDeclaration>().ToList();
                
                foreach (var varDecl in classVariables)
                {
                    AddClassVariable(varDecl, classDecl);
                }

                // Обрабатываем конструкторы (где объявлены локальные переменные)
                var constructors = classDecl.Members.OfType<ConstructorDeclaration>().ToList();
                
                foreach (var constructor in constructors)
                {
                    CheckConstructorDeclarations(constructor);
                }

                // Обрабатываем методы
                foreach (var method in classDecl.Members.OfType<MethodDeclaration>())
                {
                    _symbolTable.AddMethod($"{_currentClass}.{method.Header.Name}", method);
                    CheckMethodDeclarations(method);
                }
                
                _symbolTable.ExitScope();
            }
        }
        private void CheckMethodDeclarations(MethodDeclaration method)
        {
            
            _currentMethod = method.Header.Name;
            
            // Только для методов с телом создаем новую область видимости
            if (method.Body != null)
            {
                _symbolTable.EnterScope();
                
                // Добавляем параметры
                foreach (var param in method.Header.Parameters)
                {
                    _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
                }
                
                // Обрабатываем переменные в теле метода
                var methodVariables = method.Body.Elements.OfType<VariableDeclaration>().ToList();
                
                foreach (var varDecl in methodVariables)
                {
                    if (varDecl.Expression is ConstructorInvocation constr)
                    {
                        _symbolTable.AddSymbol(varDecl.Identifier, 
                            new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                    }
                    else
                    {
                        var exprType = InferExpressionType(varDecl.Expression);
                        _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                    }
                }
                
                // Проверяем остальные элементы
                foreach (var element in method.Body.Elements)
                {
                    if (element is VariableDeclaration varDecl)
                    {
                        // Для переменных проверяем их инициализаторы
                        CheckExpressionDeclarations(varDecl.Expression);
                    }
                    else
                    {
                        CheckElementDeclarations(element);
                    }
                }
                
                _symbolTable.ExitScope();
            }
        }

        // НОВЫЙ МЕТОД: Анализирует выражение и возвращает его тип
        private string AnalyzeExpressionAndGetType(ExpressionNode expr)
        {
            if (expr is ConstructorInvocation constr)
            {
                return BuildFullTypeName(constr.ClassName, constr.GenericParameter);
            }
            // Добавьте другие случаи по необходимости
            return InferExpressionType(expr);
        }
        private string GetConstructorType(ConstructorInvocation constr)
        {
            if (constr.ClassName == "Array")
            {
                return $"Array[{constr.GenericParameter}]";
            }
            return constr.ClassName;
        }

        private void CheckElementDeclarations(BodyElement element)
        {
            switch (element)
            {
                case Assignment assignment:
                    if (_symbolTable.Lookup(assignment.Identifier) == null)
                    {
                        _errors.Add($"Variable '{assignment.Identifier}' used before declaration");
                    }
                    CheckExpressionDeclarations(assignment.Expression);
                    break;
                    
                case ExpressionStatement exprStmt:
                    CheckExpressionDeclarations(exprStmt.Expression);
                    break;
                    
                case VariableDeclaration varDecl:
                    CheckExpressionDeclarations(varDecl.Expression);
                    break;
                    
                case WhileLoop whileLoop:
                    CheckExpressionDeclarations(whileLoop.Condition);
                    CheckBodyDeclarations(whileLoop.Body);
                    break;
                    
                case IfStatement ifStmt:
                    CheckExpressionDeclarations(ifStmt.Condition);
                    CheckBodyDeclarations(ifStmt.ThenBody);
                    if (ifStmt.ElseBody != null)
                    {
                        CheckBodyDeclarations(ifStmt.ElseBody.Body);
                    }
                    break;
                    
                case ReturnStatement returnStmt:
                    if (returnStmt.Expression != null)
                    {
                        CheckExpressionDeclarations(returnStmt.Expression);
                    }
                    break;
            }
        }

        private void CheckBodyDeclarations(MethodBodyNode body)
        {
            _symbolTable.EnterScope();
            
            // Сначала объявляем переменные
            foreach (var element in body.Elements.OfType<VariableDeclaration>())
            {
                // _symbolTable.AddSymbol(element.Identifier, new Symbol(element.Identifier, "Unknown"));
            }
            
            // Затем проверяем использование
            foreach (var element in body.Elements)
            {
                if (element is VariableDeclaration varDecl)
                {
                    // Теперь переменная уже объявлена, можно проверять ее инициализатор
                    CheckExpressionDeclarations(varDecl.Expression);
                }
                else
                {
                    CheckElementDeclarations(element);
                }
            }
            
            _symbolTable.ExitScope();
        }

        private void CheckExpressionDeclarations(ExpressionNode expr)
        {
            if (expr is IdentifierExpression ident)
            {
                if (_hierarchy.IsBuiltInClass(ident.Name))
                {
                    return; // Пропускаем проверку для встроенных типов
                }
                var symbol = _symbolTable.Lookup(ident.Name);
        
                // Если символ не найден в таблице, проверяем не является ли это классом или встроенным типом
                if (symbol == null)
                {
                    // Проверяем не является ли идентификатор именем класса
                    if (!_hierarchy.ClassExists(ident.Name) && 
                        !IsBuiltInType(ident.Name) && 
                        !IsMethodExists(ident.Name))
                    {
                        _errors.Add($"Identifier '{ident.Name}' used before declaration");
                    }
                }
            }
            else if (expr is FunctionalCall funcCall)
            {
                // Сначала проверяем саму функцию (может быть MemberAccessExpression)
                CheckExpressionDeclarations(funcCall.Function);
                
                // Затем проверяем аргументы
                foreach (var arg in funcCall.Arguments)
                {
                    CheckExpressionDeclarations(arg);
                }
                
                // Затем проверяем, что вызов метода/конструктора валиден
                var methodName = ExtractMethodName(funcCall.Function);
                if (methodName != null)
                {
                    bool isConstructor = _hierarchy.ClassExists(methodName);
                    bool isMethod = IsMethodExists(methodName);
                    bool isBuiltInType = _hierarchy.IsBuiltInClass(methodName);
                    bool isBuiltInMethodCall = IsBuiltInMethodCall(funcCall);
                    bool isArrayMethod = IsArrayMethodCall(funcCall);
                    
                    // ДОБАВЛЯЕМ: проверка конструкторов встроенных типов
                    bool isBuiltInConstructor = _hierarchy.IsBuiltInClass(methodName) && 
                                            _hierarchy.IsValidBuiltInConstructor(methodName, funcCall.Arguments.Count);
                    
                    if (!isConstructor && !isMethod && !isBuiltInType && !isBuiltInMethodCall && !isArrayMethod && !isBuiltInConstructor)
                    {
                        _errors.Add($"Method or constructor '{methodName}' not found");
                    }
                    
                    // ДОБАВЛЯЕМ: проверка конструкторов встроенных типов
                    if (isBuiltInConstructor)
                    {
                        CheckBuiltInConstructorCall(new ConstructorInvocation(methodName, null, funcCall.Arguments));
                    }
                }
            }
            else if (expr is MemberAccessExpression memberAccess)
            {
                CheckExpressionDeclarations(memberAccess.Target);
                
                // Проверяем что Member тоже существует
                if (memberAccess.Member is IdentifierExpression memberIdent)
                {
                    var targetType = InferExpressionType(memberAccess.Target);
                    string actualTargetType = targetType;
                    
                    // Если targetType неизвестен, но target - это IdentifierExpression (переменная), 
                    // пытаемся найти тип переменной в таблице символов
                    if (targetType == "Unknown" && memberAccess.Target is IdentifierExpression targetIdent)
                    {
                        var symbol = _symbolTable.Lookup(targetIdent.Name);
                        if (symbol != null)
                        {
                            actualTargetType = symbol.GetFullTypeName();
                        }
                    }
                    
                    // Если это метод встроенного типа, проверяем его существование
                    if (_hierarchy.IsBuiltInClass(actualTargetType))
                    {
                        var methods = _hierarchy.GetBuiltInMethods(actualTargetType, memberIdent.Name);
                        if (!methods.Any())
                        {
                            _errors.Add($"Method '{memberIdent.Name}' not found in built-in class '{actualTargetType}'");
                        }
                    }
                    // Если это метод класса (например, this.fact)
                    else if (_hierarchy.ClassExists(actualTargetType))
                    {
                        var method = FindMethod(memberIdent.Name, actualTargetType);
                        if (method == null)
                        {
                            _errors.Add($"Method '{memberIdent.Name}' not found in class '{actualTargetType}'");
                        }
                    }
                }
            }
            else if (expr is ConstructorInvocation constr)
            {
                if (!_hierarchy.ClassExists(constr.ClassName))
                {
                    _errors.Add($"Constructor call for unknown class '{constr.ClassName}'");
                }
                foreach (var arg in constr.Arguments)
                {
                    CheckExpressionDeclarations(arg);
                }
            }
        }
        private bool IsBuiltInMethodCall(FunctionalCall funcCall)
        {
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                var targetType = InferExpressionType(memberAccess.Target);
                var methodName = ExtractMethodName(funcCall.Function);
                
                return _hierarchy.IsBuiltInClass(targetType) && 
                    _hierarchy.HasBuiltInMethod(targetType, methodName);
            }
            return false;
        }

        // 3. Проверка иерархии классов
        private void CheckClassHierarchy(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                // Проверка циклического наследования
                if (HasInheritanceCycle(classDecl, program))
                {
                    _errors.Add($"Circular inheritance detected for class '{classDecl.Name}'");
                }
                
                // Проверка что базовый класс существует
                if (!string.IsNullOrEmpty(classDecl.Extension) && 
                    !_hierarchy.ClassExists(classDecl.Extension))
                {
                    _errors.Add($"Base class '{classDecl.Extension}' not found for class '{classDecl.Name}'");
                }
                
                // Проверка что класс не наследуется от final классов (Integer, Real, Boolean)
                if (_hierarchy.IsFinalClass(classDecl.Extension))
                {
                    _errors.Add($"Class '{classDecl.Name}' cannot inherit from final class '{classDecl.Extension}'");
                }
            }
        }

        // 4. Проверка переопределения методов
        private void CheckMethodOverriding(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                _currentClass = classDecl.Name;
                var baseClass = _hierarchy.GetBaseClass(classDecl);
                
                if (baseClass != null)
                {
                    CheckMethodOverriding(classDecl, baseClass);
                }
            }
        }

        private void CheckMethodOverriding(ClassDeclaration derived, ClassDeclaration baseClass)
        {
            var derivedMethods = derived.Members.OfType<MethodDeclaration>();
            var baseMethods = baseClass.Members.OfType<MethodDeclaration>();
            
            foreach (var derivedMethod in derivedMethods)
            {
                var baseMethod = baseMethods.FirstOrDefault(m => 
                    m.Header.Name == derivedMethod.Header.Name && 
                    AreParametersCompatible(m.Header.Parameters, derivedMethod.Header.Parameters));
                
                if (baseMethod != null)
                {
                    // Проверка совместимости возвращаемых типов
                    if (derivedMethod.Header.ReturnType != baseMethod.Header.ReturnType)
                    {
                        _errors.Add($"Method '{derivedMethod.Header.Name}' in class '{derived.Name}' " +
                                  $"has incompatible return type with base method");
                    }
                }
            }
        }

        // 5. Type Checking
        private void CheckTypeCompatibility(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                _currentClass = classDecl.Name;
                _symbolTable.EnterScope();
                
                // Добавляем переменные уровня класса
                foreach (var varDecl in classDecl.Members.OfType<VariableDeclaration>())
                {
                    AddClassVariable(varDecl, classDecl);
                }
                
                foreach (var member in classDecl.Members)
                {
                    if (member is MethodDeclaration method)
                    {
                        CheckMethodTypes(method);
                    }
                    else if (member is VariableDeclaration varDecl)
                    {
                        CheckVariableType(varDecl, classDecl);
                    }
                    else if (member is ConstructorDeclaration constructor)
                    {
                        CheckConstructorTypes(constructor);
                    }
                }
                
                _symbolTable.ExitScope();
            }
        }

        private void CheckMethodTypes(MethodDeclaration method)
        {
            _currentMethod = method.Header.Name;
            
            if (method.Body != null)
            {
                _symbolTable.EnterScope();
                
                // Добавляем параметры метода
                foreach (var param in method.Header.Parameters)
                {
                    _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
                }
                
                // Добавляем переменные метода
                foreach (var varDecl in method.Body.Elements.OfType<VariableDeclaration>())
                {
                    if (varDecl.Expression is ConstructorInvocation constr)
                    {
                        _symbolTable.AddSymbol(varDecl.Identifier, 
                            new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                    }
                    else
                    {
                        var exprType = InferExpressionType(varDecl.Expression);
                        _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                    }
                }
                
                // Проверяем типы в теле метода
                foreach (var element in method.Body.Elements)
                {
                    CheckElementTypes(element);
                }
                
                _symbolTable.ExitScope();
            }
        }

        private void CheckConstructorTypes(ConstructorDeclaration constructor)
        {
            _currentMethod = "this";
            
            _symbolTable.EnterScope();
            
            // Добавляем параметры конструктора
            foreach (var param in constructor.Parameters)
            {
                _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
            }
            
            // Добавляем переменные конструктора
            if (constructor.Body != null)
            {
                foreach (var varDecl in constructor.Body.Elements.OfType<VariableDeclaration>())
                {
                    if (varDecl.Expression is ConstructorInvocation constr)
                    {
                        _symbolTable.AddSymbol(varDecl.Identifier, 
                            new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                    }
                    else
                    {
                        var exprType = InferExpressionType(varDecl.Expression);
                        _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                    }
                }
                
                foreach (var element in constructor.Body.Elements)
                {
                    CheckElementTypes(element);
                }
            }
            
            _symbolTable.ExitScope();
        }

        private void CheckElementTypes(BodyElement element)
        {
            switch (element)
            {
                case Assignment assignment:
                    var varType = GetVariableType(assignment.Identifier);
                    var exprType = InferExpressionType(assignment.Expression);
                    if (!AreTypesCompatible(exprType, varType))
                    {
                        _errors.Add($"Type mismatch in assignment to '{assignment.Identifier}'. Expected: {varType}, Got: {exprType}");
                    }
                    break;
                    
                case VariableDeclaration varDecl:
                    // В вашем языке тип переменной выводится из выражения
                    var initializerType = InferExpressionType(varDecl.Expression);
                    // Можно добавить проверку что тип инициализатора допустим
                    break;
                    
                case ExpressionStatement exprStmt:
                    CheckExpressionTypes(exprStmt.Expression);
                    break;
                    
                case WhileLoop whileLoop:
                    var conditionType = InferExpressionType(whileLoop.Condition);
                    if (conditionType != "Boolean" && conditionType != "Unknown")
                    {
                        _errors.Add($"While loop condition must be boolean, got: {conditionType}");
                    }
                    break;
                    
                case IfStatement ifStmt:
                    var ifConditionType = InferExpressionType(ifStmt.Condition);
                    if (ifConditionType != "Boolean" && ifConditionType != "Unknown")
                    {
                        _errors.Add($"If statement condition must be boolean, got: {ifConditionType}");
                    }
                    break;
            }
        }

        private void CheckExpressionTypes(ExpressionNode expr)
        {
            if (expr is FunctionalCall funcCall)
            {
                CheckFunctionCallTypes(funcCall);
            }
            
            // Рекурсивная проверка для составных выражений
            if (expr is MemberAccessExpression memberAccess)
            {
                CheckExpressionTypes(memberAccess.Target);
            }
            else if (expr is FunctionalCall nestedFuncCall)
            {
                CheckExpressionTypes(nestedFuncCall.Function);
                foreach (var arg in nestedFuncCall.Arguments)
                {
                    CheckExpressionTypes(arg);
                }
            }
        }

        private void CheckFunctionCallTypes(FunctionalCall funcCall)
        {
            var methodName = ExtractMethodName(funcCall.Function);
            if (methodName != null)
            {
                // ПРОВЕРКА ВСТРОЕННЫХ МЕТОДОВ
                if (funcCall.Function is MemberAccessExpression memberAccess)
                {
                    var targetType = InferExpressionType(memberAccess.Target);
                    
                    // Если вызываем метод встроенного класса
                    if (_hierarchy.IsBuiltInClass(targetType))
                    {
                        CheckBuiltInMethodCall(funcCall, memberAccess.Target, methodName);
                        return;
                    }
                }

                // Существующая проверка для пользовательских методов
                var method = FindMethod(methodName, _currentClass);
                if (method != null && method.Header.Parameters.Count != funcCall.Arguments.Count)
                {
                    _errors.Add($"Argument count mismatch for method '{methodName}'. Expected: {method.Header.Parameters.Count}, Got: {funcCall.Arguments.Count}");
                }
            }
        }
        private void CheckBuiltInMethodCall(FunctionalCall funcCall, ExpressionNode target, string methodName)
        {
            var targetType = InferExpressionType(target);
            
            if (_hierarchy.IsBuiltInClass(targetType))
            {
                var methods = _hierarchy.GetBuiltInMethods(targetType, methodName);
                
                if (!methods.Any())
                {
                    _errors.Add($"Built-in class '{targetType}' has no method '{methodName}'");
                    return;
                }

                // Проверяем аргументы для каждой перегрузки метода
                bool foundMatch = false;
                foreach (var method in methods)
                {
                    if (method.Parameters.Count == funcCall.Arguments.Count)
                    {
                        foundMatch = true;
                        
                        // Проверяем типы аргументов
                        for (int i = 0; i < method.Parameters.Count; i++)
                        {
                            var argType = InferExpressionType(funcCall.Arguments[i]);
                            var paramType = method.Parameters[i].Type;
                            // Специализация T для обобщённых контейнеров
                            if (paramType == "T")
                            {
                                if (targetType.StartsWith("Array["))
                                {
                                    paramType = ExtractArrayElementType(targetType);
                                }
                                else if (targetType.StartsWith("List["))
                                {
                                    // List[Integer] -> Integer
                                    paramType = targetType.Substring(5, targetType.Length - 6);
                                }
                                else
                                {
                                    paramType = "Unknown";
                                }
                            }
                            
                            if (!AreTypesCompatible(argType, paramType) && argType != "Unknown")
                            {
                                _errors.Add($"Argument {i+1} type mismatch in '{targetType}.{methodName}'. Expected: {paramType}, Got: {argType}");
                            }
                        }
                        break;
                    }
                }
                
                if (!foundMatch)
                {
                    _errors.Add($"No matching overload found for '{targetType}.{methodName}' with {funcCall.Arguments.Count} arguments");
                }
            }
        }
        private void CheckVariableType(VariableDeclaration varDecl, ClassDeclaration classDecl = null)
        {
            if (varDecl.Expression == null) return;

            // Проверяем, является ли это generic-параметром класса
            if (varDecl.Expression is IdentifierExpression ident)
            {
                if (classDecl != null && !string.IsNullOrEmpty(classDecl.GenericParameter) && 
                    ident.Name == classDecl.GenericParameter)
                {
                    // Это generic-параметр класса - это валидно, пропускаем проверку типа
                    return;
                }
            }

            var exprType = InferExpressionType(varDecl.Expression);
            
            // Если создаем массив, сохраняем generic параметр
            if (varDecl.Expression is ConstructorInvocation constr && constr.ClassName == "Array")
            {
                var symbol = _symbolTable.Lookup(varDecl.Identifier);
                if (symbol != null)
                {
                    // Обновляем символ с generic параметром
                    _symbolTable.UpdateSymbol(varDecl.Identifier, 
                        new Symbol(varDecl.Identifier, "Array", constr.GenericParameter));
                }
            }     
            // Проверка что тип может быть выведен
            if (exprType == "Unknown")
            {
                _errors.Add($"Cannot infer type for 'var {varDecl.Identifier}'");
            }
            CheckExpressionTypes(varDecl.Expression);
        }

        // 6. Проверка вызовов конструкторов
        private void CheckConstructorCalls(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                _currentClass = classDecl.Name;
                _symbolTable.EnterScope();
                
                // Добавляем переменные уровня класса
                foreach (var varDecl in classDecl.Members.OfType<VariableDeclaration>())
                {
                    AddClassVariable(varDecl, classDecl);
                }
                
                foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
                {
                    CheckConstructorCallsInMethod(member);
                }
                
                foreach (var constructor in classDecl.Members.OfType<ConstructorDeclaration>())
                {
                    CheckConstructorCallsInConstructor(constructor);
                }
                
                _symbolTable.ExitScope();
            }
        }

        private void CheckConstructorCallsInMethod(MethodDeclaration method)
        {
            if (method.Body == null) return;
            
            _symbolTable.EnterScope();
            
            // Добавляем параметры метода
            foreach (var param in method.Header.Parameters)
            {
                _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
            }
            
            // Добавляем переменные метода
            foreach (var varDecl in method.Body.Elements.OfType<VariableDeclaration>())
            {
                if (varDecl.Expression is ConstructorInvocation constr)
                {
                    _symbolTable.AddSymbol(varDecl.Identifier, 
                        new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                }
                else
                {
                    var exprType = InferExpressionType(varDecl.Expression);
                    _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                }
            }
            
            var constructorCalls = CollectConstructorCalls(method.Body);
        
            foreach (var constructorCall in constructorCalls)
            {
                if (!_hierarchy.ClassExists(constructorCall.ClassName) && 
                    !_hierarchy.IsBuiltInClass(constructorCall.ClassName))
                {
                    _errors.Add($"Constructor call for unknown class '{constructorCall.ClassName}'");
                }
                else if (_hierarchy.IsBuiltInClass(constructorCall.ClassName))
                {
                    CheckBuiltInConstructorCall(constructorCall);
                }
            }
            
            _symbolTable.ExitScope();
        }
        private void CheckBuiltInConstructorCall(ConstructorInvocation constr)
        {
            var classInfo = _hierarchy.GetBuiltInClass(constr.ClassName);
            if (classInfo == null) return;
            
            bool foundMatch = false;
            foreach (var constructor in classInfo.Constructors)
            {
                if (constructor.Parameters.Count == constr.Arguments.Count)
                {
                    foundMatch = true;
                    
                    // Проверяем типы аргументов
                    for (int i = 0; i < constructor.Parameters.Count; i++)
                    {
                        var argType = InferExpressionType(constr.Arguments[i]);
                        var paramType = constructor.Parameters[i].Type;
                        
                        if (!AreTypesCompatible(argType, paramType) && argType != "Unknown")
                        {
                            _errors.Add($"Argument {i+1} type mismatch in '{constr.ClassName}' constructor. Expected: {paramType}, Got: {argType}");
                        }
                    }
                    break;
                }
            }
            
            if (!foundMatch)
            {
                _errors.Add($"No matching constructor found for '{constr.ClassName}' with {constr.Arguments.Count} arguments");
            }
        }

        private void CheckConstructorCallsInConstructor(ConstructorDeclaration constructor)
        {
            _symbolTable.EnterScope();
            
            // Добавляем параметры конструктора
            foreach (var param in constructor.Parameters)
            {
                _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
            }
            
            // Добавляем переменные конструктора
            if (constructor.Body != null)
            {
                foreach (var varDecl in constructor.Body.Elements.OfType<VariableDeclaration>())
                {
                    if (varDecl.Expression is ConstructorInvocation constr)
                    {
                        _symbolTable.AddSymbol(varDecl.Identifier, 
                            new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                    }
                    else
                    {
                        var exprType = InferExpressionType(varDecl.Expression);
                        _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                    }
                }
                
                var constructorCalls = CollectConstructorCalls(constructor.Body);
                
                foreach (var constructorCall in constructorCalls)
                {
                    if (!_hierarchy.ClassExists(constructorCall.ClassName))
                    {
                        _errors.Add($"Constructor call for unknown class '{constructorCall.ClassName}'");
                    }
                }
            }
            
            _symbolTable.ExitScope();
        }

        private List<ConstructorInvocation> CollectConstructorCalls(MethodBodyNode? body)
        {
            var result = new List<ConstructorInvocation>();
            
            if (body == null) return result;
            
            foreach (var element in body.Elements)
            {
                if (element is ExpressionStatement exprStmt)
                {
                    CollectConstructorCallsFromExpression(exprStmt.Expression, result);
                }
                else if (element is VariableDeclaration varDecl)
                {
                    CollectConstructorCallsFromExpression(varDecl.Expression, result);
                }
                else if (element is Assignment assignment)
                {
                    CollectConstructorCallsFromExpression(assignment.Expression, result);
                }
            }
            
            return result;
        }

        private void CollectConstructorCallsFromExpression(ExpressionNode expr, List<ConstructorInvocation> result)
        {
            if (expr is ConstructorInvocation constr)
            {
                result.Add(constr);
            }
            else if (expr is FunctionalCall funcCall)
            {
                CollectConstructorCallsFromExpression(funcCall.Function, result);
                foreach (var arg in funcCall.Arguments)
                {
                    CollectConstructorCallsFromExpression(arg, result);
                }
            }
            else if (expr is MemberAccessExpression memberAccess)
            {
                CollectConstructorCallsFromExpression(memberAccess.Target, result);
            }
        }

        // 7. Проверка forward declarations
        private void CheckForwardDeclarations(ProgramNode program)
        {
            var forwardDeclarations = new Dictionary<string, MethodDeclaration>();
            
            foreach (var classDecl in program.Classes)
            {
                foreach (var method in classDecl.Members.OfType<MethodDeclaration>())
                {
                    if (method.Body == null) // Forward declaration
                    {
                        var key = $"{classDecl.Name}.{method.Header.Name}";
                        forwardDeclarations[key] = method;
                    }
                }
            }
            
            // Проверка что у всех forward declarations есть реализации
            foreach (var forwardDecl in forwardDeclarations)
            {
                var hasImplementation = program.Classes
                    .SelectMany(c => c.Members.OfType<MethodDeclaration>())
                    .Any(m => m.Body != null && 
                             m.Header.Name == forwardDecl.Value.Header.Name &&
                             AreMethodSignaturesEqual(m.Header, forwardDecl.Value.Header));
                
                if (!hasImplementation)
                {
                    _errors.Add($"Forward declaration of method '{forwardDecl.Key}' has no implementation");
                }
            }
        }

        // 8. Проверка использования this
        private void CheckThisUsage(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
                {
                    CheckThisUsageInMethod(member, classDecl.Name);
                }
                
                foreach (var constructor in classDecl.Members.OfType<ConstructorDeclaration>())
                {
                    CheckThisUsageInConstructor(constructor, classDecl.Name);
                }
            }
        }

        private void CheckThisUsageInMethod(MethodDeclaration method, string className)
        {
            if (method.Body != null)
            {
                CheckThisUsageInBody(method.Body, className);
            }
        }

        private void CheckThisUsageInConstructor(ConstructorDeclaration constructor, string className)
        {
            CheckThisUsageInBody(constructor.Body, className);
        }

        private void CheckThisUsageInBody(MethodBodyNode body, string className)
        {
            foreach (var element in body.Elements)
            {
                if (element is ExpressionStatement exprStmt)
                {
                    CheckThisUsageInExpression(exprStmt.Expression, className);
                }
                else if (element is Assignment assignment)
                {
                    CheckThisUsageInExpression(assignment.Expression, className);
                }
                else if (element is VariableDeclaration varDecl)
                {
                    CheckThisUsageInExpression(varDecl.Expression, className);
                }
                else if (element is ReturnStatement returnStmt && returnStmt.Expression != null)
                {
                    CheckThisUsageInExpression(returnStmt.Expression, className);
                }
            }
        }

        private void CheckThisUsageInExpression(ExpressionNode expr, string className)
        {
            if (expr is ThisExpression)
            {
                if (string.IsNullOrEmpty(className))
                {
                    _errors.Add("'this' used outside of class context");
                }
            }
            
            // Рекурсивная проверка
            if (expr is MemberAccessExpression memberAccess)
            {
                CheckThisUsageInExpression(memberAccess.Target, className);
            }
            else if (expr is FunctionalCall funcCall)
            {
                CheckThisUsageInExpression(funcCall.Function, className);
                foreach (var arg in funcCall.Arguments)
                {
                    CheckThisUsageInExpression(arg, className);
                }
            }
        }

        // 9. Проверка возвращаемых значений
        private void CheckReturnStatements(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                foreach (var method in classDecl.Members.OfType<MethodDeclaration>())
                {
                    CheckMethodReturnStatements(method);
                }
            }
        }

        private void CheckMethodReturnStatements(MethodDeclaration method)
        {
            if (!string.IsNullOrEmpty(method.Header.ReturnType))
            {
                var returnStatements = CollectReturnStatements(method.Body);
                
                if (!returnStatements.Any())
                {
                    _errors.Add($"Method '{method.Header.Name}' with return type '{method.Header.ReturnType}' " +
                              "has no return statement");
                }
                
                foreach (var returnStmt in returnStatements)
                {
                    if (returnStmt.Expression == null)
                    {
                        _errors.Add($"Method '{method.Header.Name}' must return a value");
                    }
                    else
                    {
                        var returnType = InferExpressionType(returnStmt.Expression);
                        if (!AreTypesCompatible(returnType, method.Header.ReturnType))
                        {
                            _errors.Add($"Return type mismatch in method '{method.Header.Name}'. Expected: {method.Header.ReturnType}, Got: {returnType}");
                        }
                    }
                }
            }
            else
            {
                // Метод без возвращаемого типа не должен возвращать значения
                var returnStatements = CollectReturnStatements(method.Body);
                foreach (var returnStmt in returnStatements)
                {
                    if (returnStmt.Expression != null)
                    {
                        _errors.Add($"Method '{method.Header.Name}' without return type cannot return a value");
                    }
                }
            }
        }

        private List<ReturnStatement> CollectReturnStatements(MethodBodyNode? body)
        {
            var result = new List<ReturnStatement>();
            
            if (body == null) return result;
            
            foreach (var element in body.Elements)
            {
                CollectReturnStatementsFromElement(element, result);
            }
            
            return result;
        }

        private void CollectReturnStatementsFromElement(BodyElement element, List<ReturnStatement> result)
        {
            switch (element)
            {
                case ReturnStatement returnStmt:
                    result.Add(returnStmt);
                    break;
                    
                case IfStatement ifStmt:
                    // Рекурсивно собираем return statements из then и else веток
                    CollectReturnStatementsFromBody(ifStmt.ThenBody, result);
                    if (ifStmt.ElseBody != null)
                    {
                        CollectReturnStatementsFromBody(ifStmt.ElseBody.Body, result);
                    }
                    break;
                    
                case WhileLoop whileLoop:
                    // Рекурсивно собираем return statements из тела цикла
                    CollectReturnStatementsFromBody(whileLoop.Body, result);
                    break;
            }
        }

        private void CollectReturnStatementsFromBody(MethodBodyNode body, List<ReturnStatement> result)
        {
            foreach (var element in body.Elements)
            {
                CollectReturnStatementsFromElement(element, result);
            }
        }

        // 10. Array Bound Checking
    

        // 11. Other Checks
        private void CheckAdditionalSemantics(ProgramNode program)
        {
            // Проверка уникальности имен в пределах scope
            CheckUniqueNames(program);
            
            // Проверка что конструкторы не имеют возвращаемого типа
            CheckConstructorReturnTypes(program);
        }

        private void CheckUniqueNames(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                var names = new HashSet<string>();
                var methodSignatures = new HashSet<string>();
                
                foreach (var member in classDecl.Members)
                {
                    string name = member switch
                    {
                        MethodDeclaration method => method.Header.Name,
                        VariableDeclaration varDecl => varDecl.Identifier,
                        ConstructorDeclaration => "this",
                        _ => null
                    };
                    
                    if (name != null)
                    {
                        // Для методов разрешаем перегрузку - проверяем по сигнатуре
                        if (member is MethodDeclaration method)
                        {
                            var signature = BuildMethodSignature(method);
                            if (methodSignatures.Contains(signature))
                            {
                                _errors.Add($"Duplicate method signature '{signature}' in class '{classDecl.Name}'");
                            }
                            methodSignatures.Add(signature);
                        }
                        else
                        {
                            // Для переменных и конструкторов - проверяем уникальность имени
                            if (names.Contains(name))
                            {
                                _errors.Add($"Duplicate name '{name}' in class '{classDecl.Name}'");
                            }
                            names.Add(name);
                        }
                    }
                }
            }
        }

        private string BuildMethodSignature(MethodDeclaration method)
        {
            var paramTypes = string.Join(",", method.Header.Parameters.Select(p => p.Type.Name));
            return $"{method.Header.Name}({paramTypes})";
        }

        private void CheckConstructorReturnTypes(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                foreach (var member in classDecl.Members.OfType<ConstructorDeclaration>())
                {
                    // Конструкторы не должны иметь возвращаемый тип
                    // (в вашем языке это уже обеспечено синтаксисом)
                }
            }
        }

        // Вспомогательные методы
        private string InferExpressionType(ExpressionNode expr)
        {
            switch (expr)
            {
                case IntegerLiteral:
                    return "Integer";
                case RealLiteral:
                    return "Real";
                case BooleanLiteral:
                    return "Boolean";
                case ThisExpression:
                    return _currentClass;
                case IdentifierExpression ident:
                    var symbol = _symbolTable.Lookup(ident.Name);
                    if (symbol != null)
                    {
                        // Если тип известен из SymbolTable, используем его
                        return symbol.GetFullTypeName();
                    }
                    // Если это имя класса, возвращаем его как тип
                    else if (_hierarchy.ClassExists(ident.Name))
                    {
                        return ident.Name;
                    }
                    return "Unknown";
                case FunctionalCall funcCall when funcCall.Function is IdentifierExpression ctorIdent:
                    // Вызов конструктора по имени класса: Integer(...), Real(...), Boolean(...), Array(...), List(...)
                    if (_hierarchy.IsBuiltInClass(ctorIdent.Name) || _hierarchy.ClassExists(ctorIdent.Name))
                    {
                        // Примитивы и пользовательские классы возвращают сам тип
                        if (ctorIdent.Name == "Integer" || ctorIdent.Name == "Real" || ctorIdent.Name == "Boolean")
                        {
                            return ctorIdent.Name;
                        }
                        // Для Array/List без указания generic-а через ConstructorInvocation
                        return ctorIdent.Name;
                    }
                    return "Unknown";
                case ConstructorInvocation constr:
                    var fullName = BuildFullTypeName(constr.ClassName, constr.GenericParameter);
                    return fullName;
                case MemberAccessExpression memberAccess:
                    return InferMemberAccessType(memberAccess);
                case FunctionalCall funcCall when funcCall.Function is MemberAccessExpression memberAccess:
                    return InferBuiltInMethodReturnType(funcCall, memberAccess);
                default:
                    return "Unknown";
            }
        }

        private string GetIdentifierType(IdentifierExpression ident)
        {
            var symbol = _symbolTable.Lookup(ident.Name);
            if (symbol != null)
            {
                // ИСПОЛЬЗУЕМ GetFullTypeName для получения полного типа
                var fullType = symbol.GetFullTypeName();
                return fullType;
            }
            
            return "Unknown";
        }
        private string InferMemberAccessType(MemberAccessExpression memberAccess)
        {
            var targetType = InferExpressionType(memberAccess.Target);
            
            // Если target - массив и обращаемся к его методам
            if (targetType == "Array" || targetType.StartsWith("Array["))
            {
                if (memberAccess.Member is IdentifierExpression memberIdent)
                {
                    switch (memberIdent.Name)
                    {
                        case "Length": return "Integer";
                        case "get": return ExtractArrayElementType(targetType); // "Array[Integer]" -> "Integer"
                        case "set": return "void";
                        case "toList": return "List" + ExtractArrayElementType(targetType); // "List[Integer]"
                    }
                }
            }
            
            // Если target - это класс (например, this или переменная класса), проверяем методы
            if (memberAccess.Member is IdentifierExpression memberIdentifier)
            {
                var methodName = memberIdentifier.Name;
                
                // Проверяем, есть ли такой метод в классе targetType
                if (_hierarchy.ClassExists(targetType))
                {
                    var method = FindMethod(methodName, targetType);
                    if (method != null && !string.IsNullOrEmpty(method.Header.ReturnType))
                    {
                        return method.Header.ReturnType;
                    }
                }
            }
            
            return "Unknown";
        }

        // Вспомогательные методы
        private string BuildFullTypeName(string baseName, string genericParam)
        {
            if (!string.IsNullOrEmpty(genericParam))
            {
                return $"{baseName}[{genericParam}]";
            }
            return baseName;
        }

        private string ExtractArrayElementType(string arrayType)
        {
            if (arrayType.StartsWith("Array[") && arrayType.EndsWith("]"))
            {
                return arrayType.Substring(6, arrayType.Length - 7);
            }
            return "Unknown";
        }
        private string InferBuiltInMethodReturnType(FunctionalCall funcCall, MemberAccessExpression memberAccess)
        {
            var targetType = InferExpressionType(memberAccess.Target);
            var methodName = ExtractMethodName(funcCall.Function);
            
            if (_hierarchy.IsBuiltInClass(targetType) && methodName != null)
            {
                var methods = _hierarchy.GetBuiltInMethods(targetType, methodName);
                if (methods.Any())
                {
                    // Возвращаемый тип первой подходящей перегрузки с подстановкой T для контейнеров
                    var ret = methods[0].ReturnType;
                    if (ret == "T")
                    {
                        if (targetType.StartsWith("Array["))
                        {
                            return ExtractArrayElementType(targetType);
                        }
                        if (targetType.StartsWith("List["))
                        {
                            return targetType.Substring(5, targetType.Length - 6);
                        }
                    }
                    if (ret == "List" && targetType.StartsWith("Array["))
                    {
                        // Array[T].toList(): List[T]
                        var inner = ExtractArrayElementType(targetType);
                        return $"List[{inner}]";
                    }
                    return ret;
                }
            }
            
            return "Unknown";
        }

        private bool AreTypesCompatible(string sourceType, string targetType)
        {
            if (sourceType == targetType) return true;
            if (sourceType == "Unknown" || targetType == "Unknown") return true;

            var source = ParseTypeName(sourceType);
            var target = ParseTypeName(targetType);
            return TypeFactory.IsAssignable(source, target);
        }

        private string ExtractMethodName(ExpressionNode function)
        {
            return function switch
            {
                IdentifierExpression ident => ident.Name,
                MemberAccessExpression memberAccess => ExtractMethodNameFromMemberAccess(memberAccess),
                _ => null
            };
        }
        private string ExtractMethodNameFromMemberAccess(MemberAccessExpression memberAccess)
        {
            // Для цепочек вида obj.method или arr.get
            if (memberAccess.Member is IdentifierExpression memberIdent)
            {
                return memberIdent.Name;
            }
            
            // Если Member тоже является MemberAccessExpression (для цепочек a.b.c.method)
            if (memberAccess.Member is MemberAccessExpression nestedMemberAccess)
            {
                return ExtractMethodNameFromMemberAccess(nestedMemberAccess);
            }
            
            return null;
        }

        private MethodDeclaration FindMethod(string methodName, string className)
        {
            var classDecl = _hierarchy.GetClass(className);
            return classDecl?.Members.OfType<MethodDeclaration>()
                .FirstOrDefault(m => m.Header.Name == methodName);
        }

        private string GetVariableType(string variableName)
        {
            return _symbolTable.Lookup(variableName)?.Type ?? "Unknown";
        }

        // ===== Symbol-based typing helpers (non-breaking integration) =====
        private ITypeSymbol ParseTypeName(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return PrimitiveTypeSymbol.AnyValue;
            switch (typeName)
            {
                case "Integer": return PrimitiveTypeSymbol.Integer;
                case "Real": return PrimitiveTypeSymbol.Real;
                case "Boolean": return PrimitiveTypeSymbol.Boolean;
                case "AnyValue": return PrimitiveTypeSymbol.AnyValue;
                case "AnyRef": return ReferenceTypeSymbol.AnyRef;
            }

            if (typeName.StartsWith("Array[") && typeName.EndsWith("]"))
            {
                var inner = typeName.Substring(6, typeName.Length - 7);
                return TypeFactory.ArrayOf(ParseTypeName(inner));
            }
            if (typeName.StartsWith("List[") && typeName.EndsWith("]"))
            {
                var inner = typeName.Substring(5, typeName.Length - 6);
                return TypeFactory.ListOf(ParseTypeName(inner));
            }

            return GetReferenceTypeSymbol(typeName);
        }

        private ReferenceTypeSymbol GetReferenceTypeSymbol(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return ReferenceTypeSymbol.AnyRef;
            if (typeName == "AnyRef") return ReferenceTypeSymbol.AnyRef;

            // Try resolve base type using hierarchy
            var cls = _hierarchy.GetClass(typeName);
            ReferenceTypeSymbol? baseType = null;
            if (cls != null && !string.IsNullOrEmpty(cls.Extension))
            {
                baseType = ReferenceTypeSymbol.Create(cls.Extension, null);
            }
            return ReferenceTypeSymbol.Create(typeName, baseType);
        }

        private void AddClassVariable(VariableDeclaration varDecl, ClassDeclaration classDecl = null)
        {
            if (varDecl.Expression is ConstructorInvocation constr)
            {
                _symbolTable.AddSymbol(varDecl.Identifier, 
                    new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
            }
            else if (varDecl.Expression is IdentifierExpression ident)
            {
                // Проверяем, является ли это generic-параметром класса
                var identName = ident.Name;
                if (classDecl != null && !string.IsNullOrEmpty(classDecl.GenericParameter) && 
                    identName == classDecl.GenericParameter)
                {
                    // Это generic-параметр класса - используем его как тип
                    _symbolTable.AddSymbol(varDecl.Identifier, 
                        new Symbol(varDecl.Identifier, identName, null));
                }
                else if (_hierarchy.IsBuiltInClass(identName) || _hierarchy.ClassExists(identName))
                {
                    // Это имя класса
                    _symbolTable.AddSymbol(varDecl.Identifier, 
                        new Symbol(varDecl.Identifier, identName, null));
                }
                else
                {
                    // Неизвестный идентификатор - выводим тип из выражения (вернет "Unknown")
                    var exprType = InferExpressionType(varDecl.Expression);
                    _symbolTable.AddSymbol(varDecl.Identifier, 
                        new Symbol(varDecl.Identifier, exprType, null));
                }
            }
            else if (varDecl.Expression is FunctionalCall funcCall && funcCall.Function is IdentifierExpression funcIdent)
            {
                // Обработка var current : Integer(0) - это FunctionalCall с IdentifierExpression
                var typeName = funcIdent.Name;
                if (_hierarchy.IsBuiltInClass(typeName) || _hierarchy.ClassExists(typeName))
                {
                    _symbolTable.AddSymbol(varDecl.Identifier, 
                        new Symbol(varDecl.Identifier, typeName, null));
                }
            }
            else
            {
                // Для других случаев выводим тип из выражения
                var exprType = InferExpressionType(varDecl.Expression);
                _symbolTable.AddSymbol(varDecl.Identifier, 
                    new Symbol(varDecl.Identifier, exprType, null));
            }
        }

        private void CheckConstructorDeclarations(ConstructorDeclaration constructor)
        {
            _currentMethod = "this";
            _symbolTable.EnterScope();
            
            // Добавляем параметры конструктора
            foreach (var param in constructor.Parameters)
            {
                _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
            }
            
            // Обрабатываем переменные в теле конструктора
            if (constructor.Body != null)
            {
                var constructorVariables = constructor.Body.Elements.OfType<VariableDeclaration>().ToList();
                
                foreach (var varDecl in constructorVariables)
                {
                    if (varDecl.Expression is ConstructorInvocation constr)
                    {
                        _symbolTable.AddSymbol(varDecl.Identifier, 
                            new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                    }
                    else if (varDecl.Expression is FunctionalCall funcCall && funcCall.Function is IdentifierExpression funcIdent)
                    {
                        // Обработка var a : Integer(10) - это FunctionalCall с IdentifierExpression
                        var typeName = funcIdent.Name;
                        if (_hierarchy.IsBuiltInClass(typeName) || _hierarchy.ClassExists(typeName))
                        {
                            _symbolTable.AddSymbol(varDecl.Identifier, 
                                new Symbol(varDecl.Identifier, typeName, null));
                        }
                        else
                        {
                            var exprType = InferExpressionType(varDecl.Expression);
                            _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                        }
                    }
                    else
                    {
                        var exprType = InferExpressionType(varDecl.Expression);
                        _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                    }
                }
                
                // Проверяем остальные элементы конструктора
                foreach (var element in constructor.Body.Elements)
                {
                    if (element is VariableDeclaration varDecl)
                    {
                        // Для переменных проверяем их инициализаторы (аргументы уже должны быть проверены)
                        CheckExpressionDeclarations(varDecl.Expression);
                    }
                    else
                    {
                        CheckElementDeclarations(element);
                    }
                }
            }
            
            _symbolTable.ExitScope();
        }

        private bool HasInheritanceCycle(ClassDeclaration classDecl, ProgramNode program)
        {
            var visited = new HashSet<string>();
            var current = classDecl;
            
            while (current != null && !string.IsNullOrEmpty(current.Extension))
            {
                if (visited.Contains(current.Name))
                    return true;
                    
                visited.Add(current.Name);
                current = program.Classes.FirstOrDefault(c => c.Name == current.Extension);
            }
            
            return false;
        }

        private bool AreParametersCompatible(List<ParameterDeclaration> params1, List<ParameterDeclaration> params2)
        {
            if (params1.Count != params2.Count) return false;
            
            for (int i = 0; i < params1.Count; i++)
            {
                if (params1[i].Type.Name != params2[i].Type.Name)
                    return false;
            }
            
            return true;
        }

        private bool AreMethodSignaturesEqual(MethodHeaderNode header1, MethodHeaderNode header2)
        {
            return header1.Name == header2.Name &&
                   header1.ReturnType == header2.ReturnType &&
                   AreParametersCompatible(header1.Parameters, header2.Parameters);
        }
        private bool IsBuiltInType(string name)
        {
            return _hierarchy.IsBuiltInClass(name) || name == "AnyValue" || name == "AnyRef";
        }
        

        private bool IsClassName(string name, string currentClass)
        {
            // Проверяем не является ли это именем класса в программе
            return _hierarchy.ClassExists(name);
        }
        private bool IsMethodExists(string methodName)
        {
            // Проверяем существует ли метод в текущем классе
            if (!string.IsNullOrEmpty(_currentClass))
            {
                var currentClassMethod = $"{_currentClass}.{methodName}";
                if (_symbolTable.IsMethodExists(currentClassMethod))
                {
                    return true;
                }
            }
            
            // Проверяем во всех классах иерархии
            return _hierarchy.GetAllClasses().Any(className => 
                _symbolTable.IsMethodExists($"{className}.{methodName}"));
        }

        // 10. Array Bound Checking
        private void CheckArrayBounds(ProgramNode program)
        {
            foreach (var classDecl in program.Classes)
            {
                _currentClass = classDecl.Name;
                _symbolTable.EnterScope();
                
                // Добавляем переменные уровня класса
                foreach (var varDecl in classDecl.Members.OfType<VariableDeclaration>())
                {
                    AddClassVariable(varDecl, classDecl);
                }
                
                foreach (var member in classDecl.Members)
                {
                    if (member is MethodDeclaration method)
                    {
                        CheckArrayBoundsInMethod(method);
                    }
                    else if (member is ConstructorDeclaration constructor)
                    {
                        CheckArrayBoundsInConstructor(constructor);
                    }
                }
                
                _symbolTable.ExitScope();
            }
        }

        private void CheckArrayBoundsInMethod(MethodDeclaration method)
        {
            _currentMethod = method.Header.Name;
            
            if (method.Body != null)
            {
                _symbolTable.EnterScope();
                
                // Добавляем параметры метода
                foreach (var param in method.Header.Parameters)
                {
                    _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
                }
                
                // Добавляем переменные метода
                foreach (var varDecl in method.Body.Elements.OfType<VariableDeclaration>())
                {
                    if (varDecl.Expression is ConstructorInvocation constr)
                    {
                        _symbolTable.AddSymbol(varDecl.Identifier, 
                            new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                    }
                    else
                    {
                        var exprType = InferExpressionType(varDecl.Expression);
                        _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                    }
                }
                
                foreach (var element in method.Body.Elements)
                {
                    CheckArrayBoundsInElement(element);
                }
                
                _symbolTable.ExitScope();
            }
        }

        private void CheckArrayBoundsInConstructor(ConstructorDeclaration constructor)
        {
            _currentMethod = "this";
            
            _symbolTable.EnterScope();
            
            // Добавляем параметры конструктора
            foreach (var param in constructor.Parameters)
            {
                _symbolTable.AddSymbol(param.Identifier, new Symbol(param.Identifier, param.Type.Name, param.Type.GenericParameter));
            }
            
            // Добавляем переменные конструктора
            if (constructor.Body != null)
            {
                foreach (var varDecl in constructor.Body.Elements.OfType<VariableDeclaration>())
                {
                    if (varDecl.Expression is ConstructorInvocation constr)
                    {
                        _symbolTable.AddSymbol(varDecl.Identifier, 
                            new Symbol(varDecl.Identifier, constr.ClassName, constr.GenericParameter));
                    }
                    else
                    {
                        var exprType = InferExpressionType(varDecl.Expression);
                        _symbolTable.AddSymbol(varDecl.Identifier, new Symbol(varDecl.Identifier, exprType, null));
                    }
                }
                
                foreach (var element in constructor.Body.Elements)
                {
                    CheckArrayBoundsInElement(element);
                }
            }
            
            _symbolTable.ExitScope();
        }

        private void CheckArrayBoundsInElement(BodyElement element)
        {
            switch (element)
            {
                case Assignment assignment:
                    CheckArrayBoundsInAssignment(assignment);
                    break;
                    
                case ExpressionStatement exprStmt:
                    CheckArrayBoundsInExpression(exprStmt.Expression);
                    break;
                    
                case VariableDeclaration varDecl:
                    CheckArrayBoundsInExpression(varDecl.Expression);
                    break;
                    
                case WhileLoop whileLoop:
                    CheckArrayBoundsInExpression(whileLoop.Condition);
                    CheckArrayBoundsInBody(whileLoop.Body);
                    break;
                    
                case IfStatement ifStmt:
                    CheckArrayBoundsInExpression(ifStmt.Condition);
                    CheckArrayBoundsInBody(ifStmt.ThenBody);
                    if (ifStmt.ElseBody != null)
                    {
                        CheckArrayBoundsInBody(ifStmt.ElseBody.Body);
                    }
                    break;
                    
                case ReturnStatement returnStmt:
                    if (returnStmt.Expression != null)
                    {
                        CheckArrayBoundsInExpression(returnStmt.Expression);
                    }
                    break;
            }
        }

        private void CheckArrayBoundsInBody(MethodBodyNode body)
        {
            foreach (var element in body.Elements)
            {
                CheckArrayBoundsInElement(element);
            }
        }

        private void CheckArrayBoundsInAssignment(Assignment assignment)
        {
            // Проверяем присваивание к элементу массива: arr.set(index) := value
            if (IsArraySetCall(assignment.Identifier))
            {
                CheckArrayIndexInSetCall(assignment.Identifier, assignment.Expression);
            }
            
            CheckArrayBoundsInExpression(assignment.Expression);
        }

        private void CheckArrayBoundsInExpression(ExpressionNode expr)
        {
            if (expr == null) return;
            
            switch (expr)
            {
                case FunctionalCall funcCall:
                    CheckArrayBoundsInFunctionCall(funcCall);
                    break;
                    
                case MemberAccessExpression memberAccessExpr:
                    CheckArrayBoundsInMemberAccess(memberAccessExpr);
                    break;
                    
                case ConstructorInvocation constr:
                    CheckArrayBoundsInConstructorCall(constr);
                    break;
            }
            
            // Рекурсивная проверка для вложенных выражений
            if (expr is MemberAccessExpression memberAccess)
            {
                CheckArrayBoundsInExpression(memberAccess.Target);
            }
            else if (expr is FunctionalCall funcCall)
            {
                CheckArrayBoundsInExpression(funcCall.Function);
                foreach (var arg in funcCall.Arguments)
                {
                    CheckArrayBoundsInExpression(arg);
                }
            }
        }

        private void CheckArrayBoundsInFunctionCall(FunctionalCall funcCall)
        {
            // Проверяем вызов arr.get(index)
            if (IsArrayGetCall(funcCall))
            {
                CheckArrayIndexInGetCall(funcCall);
            }
            
            // Проверяем вызов arr.set(index, value)
            if (IsArraySetCall(funcCall))
            {
                CheckArrayIndexInSetCall(funcCall);
            }
            
        }   

        private void CheckArrayBoundsInMemberAccess(MemberAccessExpression memberAccess)
        {
            // Проверяем доступ к свойству массива Length
            if (IsArrayLengthAccess(memberAccess))
            {
                // Можно добавить дополнительные проверки
            }
        }

        private void CheckArrayBoundsInConstructorCall(ConstructorInvocation constr)
        {
            // Проверяем создание массива: Array[Type](size)
            if (constr.ClassName == "Array" && constr.Arguments.Count == 1)
            {
                CheckArraySizeExpression(constr.Arguments[0]);
            }
        }

        // Вспомогательные методы для определения операций с массивами
        private bool IsArrayGetCall(FunctionalCall funcCall)
        {
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                var targetType = InferExpressionType(memberAccess.Target);
                var methodName = ExtractMethodName(funcCall.Function);
                
                // Проверяем что target - Array и вызывается метод get
                return (targetType == "Array" || targetType.StartsWith("Array[")) && 
                    methodName == "get" && 
                    funcCall.Arguments.Count == 1;
            }
            return false;
        }

        private bool IsArraySetCall(FunctionalCall funcCall)
        {
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                var targetType = InferExpressionType(memberAccess.Target);
                var methodName = ExtractMethodName(funcCall.Function);
                
                // Проверяем что target - Array и вызывается метод set
                return (targetType == "Array" || targetType.StartsWith("Array[")) && 
                    methodName == "set" && 
                    funcCall.Arguments.Count == 2;
            }
            return false;
        }

        private bool IsArraySetCall(string identifier)
        {
            // Для случая когда set вызывается как метод перед присваиванием
            return identifier.EndsWith(".set");
        }

        private bool IsArrayLengthAccess(MemberAccessExpression memberAccess)
        {
            return memberAccess.Member is IdentifierExpression ident && 
                ident.Name == "Length";
        }

        // Основные проверки границ
        private void CheckArrayIndexInGetCall(FunctionalCall getCall)
        {
            var indexExpr = getCall.Arguments[0];
            var arrayExpr = GetArrayExpressionFromGetCall(getCall);
            
            if (arrayExpr != null && indexExpr != null)
            {
                CheckArrayIndexBounds(arrayExpr, indexExpr, "array get");
            }
        }

        private void CheckArrayIndexInSetCall(FunctionalCall setCall)
        {
            var indexExpr = setCall.Arguments[0];
            var arrayExpr = GetArrayExpressionFromSetCall(setCall);
            
            if (arrayExpr != null && indexExpr != null)
            {
                CheckArrayIndexBounds(arrayExpr, indexExpr, "array set");
            }
        }

        private void CheckArrayIndexInSetCall(string setIdentifier, ExpressionNode valueExpr)
        {
            // Для случая: arr.set(i) := value
            // Нужно извлечь выражение массива и индекса из идентификатора
            var parts = setIdentifier.Split('.');
            if (parts.Length == 2 && parts[1] == "set")
            {
                var arrayVar = parts[0];
                // Здесь нужно найти выражение индекса - это сложнее,
                // так как индекс передается как аргумент метода set
                // В упрощенной версии можно проверить использование индекса в контексте
            }
        }

        private void CheckArrayIndexBounds(ExpressionNode arrayExpr, ExpressionNode indexExpr, string context)
        {
            var indexType = InferExpressionType(indexExpr);
            
            // Проверяем что индекс - целое число
            if (indexType != "Integer" && indexType != "Unknown")
            {
                _errors.Add($"Array index in {context} must be Integer, got: {indexType}");
                return;
            }
            
            // Статический анализ границ (базовый)
            CheckStaticArrayIndexBounds(arrayExpr, indexExpr, context);
            
            // Проверка на отрицательные индексы
            CheckNegativeArrayIndex(indexExpr, context);
        }

        private void CheckStaticArrayIndexBounds(ExpressionNode arrayExpr, ExpressionNode indexExpr, string context)
        {
            // Простая статическая проверка для константных индексов
            if (indexExpr is IntegerLiteral intLiteral)
            {
                // Используем безопасный парсинг
                if (int.TryParse(intLiteral.Value.ToString(), out int indexValue))
                {
                    if (indexValue < 0)
                    {
                        _errors.Add($"Array index in {context} is negative: {indexValue}");
                    }
                    
                    // Если можем определить размер массива статически
                    var arraySize = TryGetStaticArraySize(arrayExpr);
                    if (arraySize.HasValue && indexValue >= arraySize.Value)
                    {
                        _errors.Add($"Array index in {context} is out of bounds: {indexValue} >= {arraySize.Value}");
                    }
                }
            }
        }

        private void CheckNegativeArrayIndex(ExpressionNode indexExpr, string context)
        {
            // Проверяем явные отрицательные литералы
            if (indexExpr is IntegerLiteral intLiteral)
            {
                var value = int.Parse(intLiteral.Value.ToString());
                if (value < 0)
                {
                    _errors.Add($"Array index in {context} is negative: {value}");
                }
            }
        }

        private void CheckArraySizeExpression(ExpressionNode sizeExpr)
        {
            var sizeType = InferExpressionType(sizeExpr);
            
            if (sizeType != "Integer" && sizeType != "Unknown")
            {
                _errors.Add($"Array size must be Integer, got: {sizeType}");
                return;
            }
            
            // Проверяем неотрицательный размер
            if (sizeExpr is IntegerLiteral intLiteral)
            {
                var size = int.Parse(intLiteral.Value.ToString());
                if (size < 0)
                {
                    _errors.Add($"Array size cannot be negative: {size}");
                }
                if (size == 0)
                {
                    _warnings.Add($"Array with zero size created");
                }
            }
        }

        // Вспомогательные методы для работы с выражениями массивов
        private ExpressionNode GetArrayExpressionFromGetCall(FunctionalCall getCall)
        {
            if (getCall.Function is MemberAccessExpression memberAccess)
            {
                return memberAccess.Target;
            }
            return null;
        }

        private ExpressionNode GetArrayExpressionFromSetCall(FunctionalCall setCall)
        {
            if (setCall.Function is MemberAccessExpression memberAccess)
            {
                return memberAccess.Target;
            }
            return null;
        }

        private int? TryGetStaticArraySize(ExpressionNode arrayExpr)
        {
            // Пытаемся определить размер массива статически
            if (arrayExpr is IdentifierExpression ident)
            {
                var symbol = _symbolTable.Lookup(ident.Name);
                if (symbol != null)
                {
                    // Если массив создавался с константным размером, можно его получить
                    // Это требует отслеживания инициализаций массивов
                }
            }
            else if (arrayExpr is ConstructorInvocation constr && constr.ClassName == "Array")
            {
                if (constr.Arguments.Count == 1 && constr.Arguments[0] is IntegerLiteral sizeLiteral)
                {
                    return int.Parse(sizeLiteral.Value.ToString());
                }
            }
            
            return null;
        }

        private bool IsArrayMethodCall(FunctionalCall funcCall)
        {
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                var targetType = InferExpressionType(memberAccess.Target);
                var methodName = ExtractMethodName(funcCall.Function);
                
                bool isArrayCall = (targetType == "Array" || targetType.StartsWith("Array[")) && 
                                (methodName == "get" || methodName == "set" || methodName == "Length" || methodName == "toList");
                
                return isArrayCall;
            }
            return false;
        }

        private bool IsArrayType(string typeName)
        {
            return typeName != null && typeName.StartsWith("Array[");
        }

        private bool IsArrayMethod(string methodName)
        {
            return methodName == "get" || methodName == "set" || methodName == "Length";
        }

    }
}