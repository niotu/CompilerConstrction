using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OCompiler.Parser;
using OCompiler.Semantic;

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Генератор IL-кода для методов и конструкторов.
    /// </summary>
    public class MethodGenerator
    {
        private readonly TypeMapper _typeMapper;
        private readonly ClassHierarchy _hierarchy;
        private readonly CodeGenerator? _codeGenerator;
        private readonly ProgramNode? _programAst;
        
        // Словари для кэширования информации о методах и конструкторах
        private readonly Dictionary<string, List<(ConstructorBuilder ctor, Type[] paramTypes)>> _constructors;
        private readonly Dictionary<string, List<(string methodName, Type[] paramTypes, Type returnType)>> _methodSignatures;
        private readonly Dictionary<string, List<(MethodBuilder methodBuilder, string methodName, Type[] paramTypes, Type returnType)>> _methodBuilders;
        private readonly Dictionary<string, Dictionary<string, FieldBuilder>> _classFields; // Поля всех классов
        
        private ILGenerator? _il;
        private Dictionary<string, LocalBuilder>? _locals;
        private Dictionary<string, Type>? _localTypes;
        private Dictionary<string, FieldBuilder>? _fields;
        private Dictionary<string, int>? _parameters;
        private Dictionary<string, Type>? _parameterTypes;
        private string? _currentClassName;  // Track which class we're currently generating

        public MethodGenerator(TypeMapper typeMapper, ClassHierarchy hierarchy, CodeGenerator? codeGenerator = null, ProgramNode? programAst = null)
        {
            _typeMapper = typeMapper;
            _hierarchy = hierarchy;
            _codeGenerator = codeGenerator;
            _programAst = programAst;
            
            // Инициализируем словари
            _constructors = new Dictionary<string, List<(ConstructorBuilder ctor, Type[] paramTypes)>>();
            _methodSignatures = new Dictionary<string, List<(string, Type[], Type)>>();
            _methodBuilders = new Dictionary<string, List<(MethodBuilder, string, Type[], Type)>>();
            _classFields = new Dictionary<string, Dictionary<string, FieldBuilder>>();
        }

        /// <summary>
        /// Helper to reconstruct full type name from ClassNameNode (handles generic types like Array[Integer])
        /// </summary>
        private string BuildTypeName(ClassNameNode typeNode)
        {
            if (string.IsNullOrEmpty(typeNode.GenericParameter))
            {
                return typeNode.Name;
            }
            return $"{typeNode.Name}[{typeNode.GenericParameter}]";
        }

        /// <summary>
        /// Регистрирует конструктор для последующего использования.
        /// </summary>
        public void RegisterConstructor(string className, Type[] paramTypes, ConstructorBuilder ctorBuilder)
        {
            if (!_constructors.ContainsKey(className))
            {
                _constructors[className] = new List<(ConstructorBuilder ctor, Type[] paramTypes)>();
            }

            _constructors[className].Add((ctorBuilder, paramTypes));
            Console.WriteLine($"**[ DEBUG ]   Registered constructor: {className}({string.Join(", ", paramTypes.Select(t => t.Name))})");
        }

        /// <summary>
        /// Регистрирует поля класса для последующего использования.
        /// </summary>
        public void RegisterFields(string className, Dictionary<string, FieldBuilder> fields)
        {
            _classFields[className] = fields;
        }

        /// <summary>
        /// Получает зарегистрированный конструктор по имени класса и типам параметров.
        /// </summary>
        public ConstructorBuilder? GetRegisteredConstructor(string className, Type[] paramTypes)
        {
            if (!_constructors.TryGetValue(className, out var list))
            {
                return null;
            }

            foreach (var (cb, pts) in list)
            {
                if (pts.Length != paramTypes.Length) continue;
                bool ok = true;
                for (int i = 0; i < pts.Length; i++)
                {
                    if (pts[i] != paramTypes[i]) { ok = false; break; }
                }
                if (ok) return cb;
            }

            return null;
        }


        /// <summary>
        /// Регистрирует сигнатуру метода для последующего использования.
        /// </summary>
        public void RegisterMethod(string className, string methodName, Type[] paramTypes, Type returnType)
        {
            if (!_methodSignatures.ContainsKey(className))
            {
                _methodSignatures[className] = new List<(string, Type[], Type)>();
            }
            
            _methodSignatures[className].Add((methodName, paramTypes, returnType));
            Console.WriteLine($"**[ DEBUG ]   Registered method: {className}.{methodName}({string.Join(", ", paramTypes.Select(t => t.Name))}) : {returnType.Name}");
        }

        /// <summary>
        /// Устанавливает контекст для генерации выражений (используется при генерации дефолтного конструктора).
        /// </summary>
        public void SetContext(
            ILGenerator il,
            Dictionary<string, LocalBuilder> locals,
            Dictionary<string, Type> localTypes,
            Dictionary<string, FieldBuilder> fields,
            Dictionary<string, int> parameters,
            Dictionary<string, Type> parameterTypes,
            string className)
        {
            _il = il;
            _locals = locals;
            _localTypes = localTypes;
            _fields = fields;
            _parameters = parameters;
            _parameterTypes = parameterTypes;
            _currentClassName = className;
        }

        /// <summary>
        /// Генерирует конструктор класса.
        /// </summary>
        public void GenerateConstructor(
            TypeBuilder typeBuilder, 
            ConstructorDeclaration ctorDecl, 
            ClassDeclaration classDecl,
            Dictionary<string, FieldBuilder> fields)
        {
            _currentClassName = classDecl.Name;  // Track current class
            var paramTypes = ctorDecl.Parameters
                .Select(p => _typeMapper.GetNetType(BuildTypeName(p.Type)))
                .ToArray();

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes);

            // Регистрируем конструктор
            RegisterConstructor(classDecl.Name, paramTypes, ctorBuilder);

            _il = ctorBuilder.GetILGenerator();
            _locals = new Dictionary<string, LocalBuilder>();
            _localTypes = new Dictionary<string, Type>();
            _fields = fields;
            _parameters = new Dictionary<string, int>();
            _parameterTypes = new Dictionary<string, Type>();

            // Регистрируем параметры
            for (int i = 0; i < ctorDecl.Parameters.Count; i++)
            {
                var param = ctorDecl.Parameters[i];
                _parameters[param.Identifier] = i + 1;
                _parameterTypes[param.Identifier] = paramTypes[i];
            }

            // Вызов конструктора базового класса
            _il.Emit(OpCodes.Ldarg_0); // this

            ConstructorInfo? baseConstructor;
            
            if (typeBuilder.BaseType == typeof(object))
            {
                baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
            }
            else
            {
                try
                {
                    baseConstructor = typeBuilder.BaseType!.GetConstructor(Type.EmptyTypes);
                }
                catch (NotSupportedException)
                {
                    Console.WriteLine($"**[ WARN ] Cannot get base constructor, using object()");
                    baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
                }
            }

            if (baseConstructor == null)
            {
                throw new InvalidOperationException(
                    $"Parameterless constructor not found for base type '{typeBuilder.BaseType?.Name}'");
            }

            _il.Emit(OpCodes.Call, baseConstructor);

            // Инициализация полей класса
            foreach (var varDecl in classDecl.Members.OfType<VariableDeclaration>())
            {
                if (fields.TryGetValue(varDecl.Identifier, out var field))
                {
                    _il.Emit(OpCodes.Ldarg_0); // this
                    GenerateExpression(varDecl.Expression);
                    _il.Emit(OpCodes.Stfld, field);
                }
            }

            // Генерация тела конструктора
            if (ctorDecl.Body != null)
            {
                GenerateMethodBodyContent(ctorDecl.Body);
            }

            _il.Emit(OpCodes.Ret);

            Console.WriteLine($"**[ DEBUG ]   Constructor: this({string.Join(", ", paramTypes.Select(t => t.Name))})");
        }

        /// <summary>
        /// Генерирует метод класса.
        /// </summary>
        // Объявление метода (создание сигнатуры и регистрация)
        public void DeclareMethod(
            TypeBuilder typeBuilder, 
            MethodDeclaration methodDecl,
            string className)
        {
            Type returnType = string.IsNullOrEmpty(methodDecl.Header.ReturnType)
                ? typeof(void)
                : _typeMapper.GetNetType(methodDecl.Header.ReturnType);

            var paramTypes = methodDecl.Header.Parameters
                .Select(p => _typeMapper.GetNetType(BuildTypeName(p.Type)))
                .ToArray();

            // Регистрируем метод в сигнатурах
            RegisterMethod(className, methodDecl.Header.Name, paramTypes, returnType);

            // Создаём MethodBuilder (без генерации тела)
            var methodBuilder = typeBuilder.DefineMethod(
                methodDecl.Header.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
                returnType,
                paramTypes);

            // Регистрируем MethodBuilder для последующего использования при вызовах
            if (!_methodBuilders.ContainsKey(className))
                _methodBuilders[className] = new List<(MethodBuilder, string, Type[], Type)>();

            _methodBuilders[className].Add((methodBuilder, methodDecl.Header.Name, paramTypes, returnType));

            Console.WriteLine($"**[ DEBUG ]   Registered method: {className}.{methodDecl.Header.Name}({string.Join(", ", paramTypes.Select(t => t.Name))}) : {returnType.Name}");
        }

        // Генерация тела метода
        public void GenerateMethodBody(
            TypeBuilder typeBuilder, 
            MethodDeclaration methodDecl,
            Dictionary<string, FieldBuilder> fields,
            string className)
        {
            _currentClassName = className;

            Type returnType = string.IsNullOrEmpty(methodDecl.Header.ReturnType)
                ? typeof(void)
                : _typeMapper.GetNetType(methodDecl.Header.ReturnType);

            var paramTypes = methodDecl.Header.Parameters
                .Select(p => _typeMapper.GetNetType(BuildTypeName(p.Type)))
                .ToArray();

            // Находим MethodBuilder, который создали в DeclareMethod
            if (!_methodBuilders.TryGetValue(className, out var builders))
            {
                throw new InvalidOperationException($"No method builders found for class {className}");
            }

            var methodInfo = builders.FirstOrDefault(b =>
                b.methodName == methodDecl.Header.Name &&
                b.paramTypes.SequenceEqual(paramTypes, new TypeComparer()));

            if (methodInfo.methodBuilder == null)
            {
                throw new InvalidOperationException($"Method {methodDecl.Header.Name} not found in builders for class {className}");
            }

            var methodBuilder = methodInfo.methodBuilder;

            if (methodDecl.Body == null)
            {
                Console.WriteLine($"**[ DEBUG ]   Method (forward): {methodDecl.Header.Name}");
                return;
            }

            _il = methodBuilder.GetILGenerator();
            _locals = new Dictionary<string, LocalBuilder>();
            _localTypes = new Dictionary<string, Type>();
            _fields = fields;
            _parameters = new Dictionary<string, int>();
            _parameterTypes = new Dictionary<string, Type>();

            // Регистрируем параметры
            for (int i = 0; i < methodDecl.Header.Parameters.Count; i++)
            {
                var param = methodDecl.Header.Parameters[i];
                _parameters[param.Identifier] = i + 1;
                _parameterTypes[param.Identifier] = paramTypes[i];
            }

            // Генерация тела метода
            GenerateMethodBodyContent(methodDecl.Body);

            // Если метод void и нет явного return
            if (returnType == typeof(void))
            {
                _il.Emit(OpCodes.Ret);
            }

            Console.WriteLine($"**[ DEBUG ]   Method: {methodDecl.Header.Name}({string.Join(", ", paramTypes.Select(t => t.Name))}) : {returnType.Name}");
        }

        // Старый метод для совместимости (если где-то ещё используется)
        public void GenerateMethod(
            TypeBuilder typeBuilder, 
            MethodDeclaration methodDecl,
            Dictionary<string, FieldBuilder> fields,
            string className)
        {
            DeclareMethod(typeBuilder, methodDecl, className);
            GenerateMethodBody(typeBuilder, methodDecl, fields, className);
        }

        private void GenerateMethodBodyContent(MethodBodyNode body)
        {
            foreach (var element in body.Elements)
            {
                GenerateBodyElement(element);
            }
        }

        private void GenerateBodyElement(BodyElement element)
        {
            switch (element)
            {
                case VariableDeclaration varDecl:
                    GenerateVariableDeclaration(varDecl);
                    break;

                case Assignment assignment:
                    GenerateAssignment(assignment);
                    break;

                case ExpressionStatement exprStmt:
                    GenerateExpression(exprStmt.Expression);
                    var exprType = InferExpressionType(exprStmt.Expression);
                    if (exprType != typeof(void))
                    {
                        _il!.Emit(OpCodes.Pop);
                    }
                    break;

                case ReturnStatement returnStmt:
                    if (returnStmt.Expression != null)
                    {
                        GenerateExpression(returnStmt.Expression);
                    }
                    _il!.Emit(OpCodes.Ret);
                    break;

                case IfStatement ifStmt:
                    GenerateIfStatement(ifStmt);
                    break;

                case WhileLoop whileLoop:
                    GenerateWhileLoop(whileLoop);
                    break;
            }
        }

        private void GenerateVariableDeclaration(VariableDeclaration varDecl)
        {
            Type varType = _typeMapper.InferType(varDecl.Expression);
            var local = _il!.DeclareLocal(varType);
            
            _locals![varDecl.Identifier] = local;
            _localTypes![varDecl.Identifier] = varType;

            Console.WriteLine($"**[ DEBUG ]     Variable: {varDecl.Identifier} : {varType.Name}");

            GenerateExpression(varDecl.Expression);
            _il.Emit(OpCodes.Stloc, local);
        }

        private void GenerateAssignment(Assignment assignment)
        {
            if (_locals!.TryGetValue(assignment.Identifier, out var local))
            {
                GenerateExpression(assignment.Expression);
                _il!.Emit(OpCodes.Stloc, local);
            }
            else if (_parameters!.TryGetValue(assignment.Identifier, out var paramIndex))
            {
                GenerateExpression(assignment.Expression);
                _il!.Emit(OpCodes.Starg, paramIndex);
            }
            else if (_fields!.TryGetValue(assignment.Identifier, out var field))
            {
                _il!.Emit(OpCodes.Ldarg_0); // this
                GenerateExpression(assignment.Expression);
                _il.Emit(OpCodes.Stfld, field);
            }
            else
            {
                throw new InvalidOperationException($"Variable, parameter or field '{assignment.Identifier}' not found");
            }
        }

        public void GenerateExpression(ExpressionNode expr)
        {
            switch (expr)
            {
                case IntegerLiteral intLit:
                    _il!.Emit(OpCodes.Ldc_I4, intLit.Value);
                    break;

                case RealLiteral realLit:
                    _il!.Emit(OpCodes.Ldc_R8, realLit.Value);
                    break;

                case BooleanLiteral boolLit:
                    _il!.Emit(boolLit.Value ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0);
                    break;

                case IdentifierExpression ident:
                    GenerateIdentifierLoad(ident);
                    break;

                case ConstructorInvocation ctor:
                    GenerateConstructorInvocation(ctor);
                    break;

                case FunctionalCall funcCall:
                    GenerateFunctionCall(funcCall);
                    break;

                case MemberAccessExpression memberAccess:
                    GenerateMemberAccess(memberAccess);
                    break;

                case ThisExpression:
                    _il!.Emit(OpCodes.Ldarg_0); // this
                    break;

                default:
                    throw new NotImplementedException($"Expression type {expr.GetType().Name} not implemented");
            }
        }

        private void GenerateIdentifierLoad(IdentifierExpression ident)
        {
            if (_locals!.TryGetValue(ident.Name, out var local))
            {
                _il!.Emit(OpCodes.Ldloc, local);
            }
            else if (_parameters!.TryGetValue(ident.Name, out var paramIndex))
            {
                _il!.Emit(OpCodes.Ldarg, paramIndex);
            }
            else if (_fields!.TryGetValue(ident.Name, out var field))
            {
                _il!.Emit(OpCodes.Ldarg_0); // this
                _il.Emit(OpCodes.Ldfld, field);
            }
            else
            {
                throw new InvalidOperationException($"Identifier '{ident.Name}' not found");
            }
        }

        private void GenerateConstructorInvocation(ConstructorInvocation ctor)
        {
            if (ctor.ClassName == "Integer")
            {
                if (ctor.Arguments.Count == 0)
                {
                    _il!.Emit(OpCodes.Ldc_I4_0);
                }
                else if (ctor.Arguments.Count == 1)
                {
                    GenerateExpression(ctor.Arguments[0]);
                    var argType = _typeMapper.InferType(ctor.Arguments[0]);
                    if (argType == typeof(double))
                    {
                        _il!.Emit(OpCodes.Conv_I4);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Integer constructor expects 0 or 1 argument, got {ctor.Arguments.Count}");
                }
                return;
            }

            if (ctor.ClassName == "Real")
            {
                if (ctor.Arguments.Count == 0)
                {
                    _il!.Emit(OpCodes.Ldc_R8, 0.0);
                }
                else if (ctor.Arguments.Count == 1)
                {
                    GenerateExpression(ctor.Arguments[0]);
                    var argType = _typeMapper.InferType(ctor.Arguments[0]);
                    if (argType == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Real constructor expects 0 or 1 argument, got {ctor.Arguments.Count}");
                }
                return;
            }

            if (ctor.ClassName == "Boolean")
            {
                if (ctor.Arguments.Count == 0)
                {
                    _il!.Emit(OpCodes.Ldc_I4_0);
                }
                else if (ctor.Arguments.Count == 1)
                {
                    GenerateExpression(ctor.Arguments[0]);
                }
                else
                {
                    throw new InvalidOperationException($"Boolean constructor expects 0 or 1 argument, got {ctor.Arguments.Count}");
                }
                return;
            }

            if (ctor.ClassName == "Array")
            {
                if (string.IsNullOrEmpty(ctor.GenericParameter))
                {
                    throw new InvalidOperationException("Array constructor requires generic parameter: Array[T]");
                }
                
                if (ctor.Arguments.Count != 1)
                {
                    throw new InvalidOperationException($"Array constructor expects 1 argument (size), got {ctor.Arguments.Count}");
                }
                
                Type elementType = _typeMapper.GetNetType(ctor.GenericParameter);
                GenerateExpression(ctor.Arguments[0]);
                _il!.Emit(OpCodes.Newarr, elementType);
                return;
            }

            if (ctor.ClassName == "List")
            {
                if (string.IsNullOrEmpty(ctor.GenericParameter))
                {
                    throw new InvalidOperationException("List constructor requires generic parameter: List[T]");
                }
                
                Type elementType = _typeMapper.GetNetType(ctor.GenericParameter);
                Type listType = typeof(List<>).MakeGenericType(elementType);
                ConstructorInfo? listCtor;
                
                if (ctor.Arguments.Count == 0)
                {
                    // List[T]() - пустой список
                    listCtor = listType.GetConstructor(Type.EmptyTypes);
                    if (listCtor == null)
                        throw new InvalidOperationException($"Could not find parameterless constructor for List<{elementType.Name}>");
                    _il!.Emit(OpCodes.Newobj, listCtor);
                }
                else if (ctor.Arguments.Count == 1)
                {
                    // List[T](element) - список с одним элементом
                    listCtor = listType.GetConstructor(Type.EmptyTypes);
                    if (listCtor == null)
                        throw new InvalidOperationException($"Could not find constructor for List<{elementType.Name}>");
                    
                    _il!.Emit(OpCodes.Newobj, listCtor);
                    _il.Emit(OpCodes.Dup); // дублируем список на стеке
                    GenerateExpression(ctor.Arguments[0]); // генерируем элемент
                    
                    // Вызываем list.Add(element)
                    var addMethod = listType.GetMethod("Add", new[] { elementType });
                    if (addMethod == null)
                        throw new InvalidOperationException($"Could not find Add method for List<{elementType.Name}>");
                    _il.Emit(OpCodes.Callvirt, addMethod);
                }
                else if (ctor.Arguments.Count == 2)
                {
                    // List[T](element, count) - список с повторяющимся элементом
                    listCtor = listType.GetConstructor(Type.EmptyTypes);
                    if (listCtor == null)
                        throw new InvalidOperationException($"Could not find constructor for List<{elementType.Name}>");
                    
                    _il!.Emit(OpCodes.Newobj, listCtor);
                    
                    // Генерируем цикл: for (int i = 0; i < count; i++) list.Add(element)
                    var loopCounter = _il.DeclareLocal(typeof(int));
                    var loopLabel = _il.DefineLabel();
                    var exitLabel = _il.DefineLabel();
                    
                    // i = 0
                    _il.Emit(OpCodes.Ldc_I4_0);
                    _il.Emit(OpCodes.Stloc, loopCounter);
                    
                    // loop start
                    _il.MarkLabel(loopLabel);
                    _il.Emit(OpCodes.Ldloc, loopCounter);
                    GenerateExpression(ctor.Arguments[1]); // count
                    _il.Emit(OpCodes.Bge, exitLabel); // if i >= count, exit
                    
                    // list.Add(element)
                    _il.Emit(OpCodes.Dup); // дублируем список
                    GenerateExpression(ctor.Arguments[0]); // element
                    var addMethod = listType.GetMethod("Add", new[] { elementType });
                    if (addMethod == null)
                        throw new InvalidOperationException($"Could not find Add method for List<{elementType.Name}>");
                    _il.Emit(OpCodes.Callvirt, addMethod);
                    
                    // i++
                    _il.Emit(OpCodes.Ldloc, loopCounter);
                    _il.Emit(OpCodes.Ldc_I4_1);
                    _il.Emit(OpCodes.Add);
                    _il.Emit(OpCodes.Stloc, loopCounter);
                    _il.Emit(OpCodes.Br, loopLabel);
                    
                    // exit
                    _il.MarkLabel(exitLabel);
                }
                else
                {
                    throw new InvalidOperationException($"List constructor accepts 0, 1, or 2 arguments, got {ctor.Arguments.Count}");
                }
                
                return;
            }

            // Пользовательские классы
            // Пользовательские классы
            Type type = _typeMapper.GetNetType(ctor.ClassName);

            if (type is TypeBuilder) // незавершённый тип — ищем среди зарегистрированных
            {
                // 1) Сгенерировать аргументы на стек
                var argTypes = ctor.Arguments.Select(a => _typeMapper.InferType(a)).ToArray();
                foreach (var arg in ctor.Arguments)
                {
                    GenerateExpression(arg);
                }

                // 2) Найти ConstructorBuilder по заранее сохранённым типам
                if (_constructors.TryGetValue(ctor.ClassName, out var list))
                {
                    ConstructorBuilder? match = null;
                    foreach (var (cb, pts) in list)
                    {
                        if (pts.Length != argTypes.Length) continue;
                        bool ok = true;
                        for (int i = 0; i < pts.Length; i++)
                        {
                            if (pts[i] != argTypes[i]) { ok = false; break; }
                        }
                        if (ok) { match = cb; break; }
                    }

                    if (match != null)
                    {
                        _il!.Emit(OpCodes.Newobj, match);
                        return;
                    }
                }

                throw new InvalidOperationException(
                    $"Constructor for '{ctor.ClassName}({string.Join(", ", argTypes.Select(t => t.Name))})' not registered.");
            }

            // Завершённый тип — обычная рефлексия
            var completedArgTypes = ctor.Arguments.Select(a => _typeMapper.InferType(a)).ToArray();
            var ci = type.GetConstructor(completedArgTypes);
            if (ci == null)
            {
                throw new InvalidOperationException(
                    $"Constructor not found for type '{ctor.ClassName}' with args ({string.Join(", ", completedArgTypes.Select(t => t.Name))})");
            }
            foreach (var arg in ctor.Arguments) GenerateExpression(arg);
            _il!.Emit(OpCodes.Newobj, ci);

        }


        private void GenerateFunctionCall(FunctionalCall funcCall)
        {
            // СЛУЧАЙ 1: Вызов конструктора встроенного типа
            if (funcCall.Function is IdentifierExpression identFunc)
            {
                var typeName = identFunc.Name;
                
                if (_typeMapper.IsBuiltInType(typeName))
                {
                    var pseudoConstructor = new ConstructorInvocation(
                        typeName,
                        string.Empty,
                        funcCall.Arguments
                    );
                    GenerateConstructorInvocation(pseudoConstructor);
                    return;
                }

                // СЛУЧАЙ 1b: Вызов метода того же класса (неявный this)
                if (_currentClassName != null && _methodSignatures.ContainsKey(_currentClassName))
                {
                    var methods = _methodSignatures[_currentClassName];
                    var matchingMethod = methods.FirstOrDefault(m => m.methodName == typeName);
                    if (matchingMethod.methodName != null)
                    {
                        // Это метод текущего класса, вызываем через this
                        Console.WriteLine($"**[ DEBUG ]       Calling {typeName} on implicit this");
                        _il?.Emit(OpCodes.Ldarg_0);  // this
                        
                        // Генерируем аргументы
                        foreach (var arg in funcCall.Arguments)
                        {
                            GenerateExpression(arg);
                        }
                        
                        // Ищем MethodBuilder для вызова
                        if (_methodBuilders.ContainsKey(_currentClassName))
                        {
                            var methodBuilders = _methodBuilders[_currentClassName];
                            var methodBuilder = methodBuilders.FirstOrDefault(m => m.methodName == typeName);
                            if (methodBuilder.methodBuilder != null)
                            {
                                _il?.Emit(OpCodes.Callvirt, methodBuilder.methodBuilder);
                                return;
                            }
                        }
                        
                        throw new InvalidOperationException($"MethodBuilder not found for {_currentClassName}.{typeName}");
                    }
                }
                
                throw new NotImplementedException(
                    $"Direct function call '{typeName}' not recognized.");
            }
            
            // СЛУЧАЙ 2: Вызов метода на объекте
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                var methodName = (memberAccess.Member as IdentifierExpression)?.Name;

                if (methodName == null)
                {
                    throw new InvalidOperationException("Method name not found in member access");
                }

                Type targetType = InferExpressionType(memberAccess.Target);

                Console.WriteLine($"**[ DEBUG ]       Calling {methodName} on type {targetType.Name}");

                if (targetType == typeof(int))
                {
                    GenerateExpression(memberAccess.Target);
                    GenerateIntegerMethodCall(methodName, funcCall.Arguments);
                    return;
                }

                if (targetType == typeof(double))
                {
                    GenerateExpression(memberAccess.Target);
                    GenerateRealMethodCall(methodName, funcCall.Arguments);
                    return;
                }

                if (targetType == typeof(bool))
                {
                    GenerateExpression(memberAccess.Target);
                    GenerateBooleanMethodCall(methodName, funcCall.Arguments);
                    return;
                }

                if (targetType.IsArray)
                {
                    GenerateExpression(memberAccess.Target);
                    GenerateArrayMethodCall(methodName, funcCall.Arguments, targetType);
                    return;
                }

                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    GenerateExpression(memberAccess.Target);
                    GenerateListMethodCall(methodName, funcCall.Arguments, targetType);
                    return;
                }

                GenerateUserMethodCall(memberAccess.Target, methodName, funcCall.Arguments, targetType);
                return;
            }

            throw new NotImplementedException($"Function call pattern not recognized");
        }

        private Type GetIdentifierType(string identifier)
        {
            if (_localTypes != null && _localTypes.TryGetValue(identifier, out var localType))
            {
                return localType;
            }

            if (_parameterTypes != null && _parameterTypes.TryGetValue(identifier, out var paramType))
            {
                return paramType;
            }

            if (_fields != null && _fields.TryGetValue(identifier, out var field))
            {
                return field.FieldType;
            }

            Console.WriteLine($"**[ WARN ] Could not determine type for identifier '{identifier}', using Object");
            return typeof(object);
        }

        private Type InferExpressionType(ExpressionNode expr)
        {
            switch (expr)
            {
                case IntegerLiteral:
                    return typeof(int);
                
                case RealLiteral:
                    return typeof(double);
                
                case BooleanLiteral:
                    return typeof(bool);
                
                case IdentifierExpression ident:
                    return GetIdentifierType(ident.Name);
                
                case ConstructorInvocation ctor:
                    return _typeMapper.InferType(ctor);
                
                case FunctionalCall funcCall:
                    return InferFunctionCallReturnType(funcCall);
                
                case MemberAccessExpression memberAccess:
                    return InferMemberAccessReturnType(memberAccess);
                
                case ThisExpression:
                    // Возвращаем TypeBuilder текущего класса
                    if (_currentClassName != null)
                    {
                        // Используем TypeMapper для получения типа (TypeBuilder или завершенный тип)
                        var currentType = _typeMapper.GetNetType(_currentClassName);
                        if (currentType != null)
                            return currentType;
                    }
                    return typeof(object);
                
                default:
                    return typeof(object);
            }
        }

        private Type InferFunctionCallReturnType(FunctionalCall funcCall)
        {
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                var targetType = InferExpressionType(memberAccess.Target);
                var methodName = (memberAccess.Member as IdentifierExpression)?.Name;

                if (methodName == null)
                    return typeof(object);

                if (targetType == typeof(int))
                {
                    return methodName switch
                    {
                        "Plus" or "Minus" or "Mult" or "Div" or "Rem" or "UnaryMinus" => typeof(int),
                        "Less" or "LessEqual" or "Greater" or "GreaterEqual" or "Equal" => typeof(bool),
                        "toReal" => typeof(double),
                        "Print" => typeof(void),
                        _ => typeof(object)
                    };
                }

                if (targetType == typeof(double))
                {
                    return methodName switch
                    {
                        "Plus" or "Minus" or "Mult" or "Div" or "UnaryMinus" => typeof(double),
                        "Less" or "LessEqual" or "Greater" or "GreaterEqual" or "Equal" => typeof(bool),
                        "toInteger" => typeof(int),
                        "Print" => typeof(void),
                        _ => typeof(object)
                    };
                }

                if (targetType == typeof(bool))
                {
                    return methodName switch
                    {
                        "And" or "Or" or "Xor" or "Not" or "Equal" => typeof(bool),
                        "Print" => typeof(void),
                        _ => typeof(object)
                    };
                }

                // Handle Array method calls
                if (targetType.IsArray)
                {
                    var elementType = targetType.GetElementType()!;
                    return methodName switch
                    {
                        "get" => elementType,
                        "set" => typeof(void),
                        "Length" => typeof(int),
                        "toList" => typeof(System.Collections.Generic.List<>).MakeGenericType(elementType),
                        _ => typeof(object)
                    };
                }

                // Handle List method calls
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.List<>))
                {
                    var elementType = targetType.GetGenericArguments()[0];
                    return methodName switch
                    {
                        "append" => targetType, // возвращает сам список
                        "head" => elementType,
                        "tail" => targetType,
                        "isEmpty" => typeof(bool),
                        _ => typeof(object)
                    };
                }

                var argTypes = funcCall.Arguments.Select(InferExpressionType).ToArray();
                
                // Для TypeBuilder сначала проверяем зарегистрированные сигнатуры в текущем классе и базовых
                if (targetType is TypeBuilder)
                {
                    Type? currentType = targetType;
                    while (currentType != null && currentType != typeof(object))
                    {
                        if (_methodSignatures.TryGetValue(currentType.Name, out var methodSigs))
                        {
                            var matchingSig = methodSigs.FirstOrDefault(m =>
                                m.methodName == methodName &&
                                m.paramTypes.Length == argTypes.Length &&
                                m.paramTypes.SequenceEqual(argTypes, new TypeComparer()));
                            
                            if (matchingSig != default)
                            {
                                Console.WriteLine($"**[ DEBUG ]       Method {currentType.Name}.{methodName} returns {matchingSig.returnType.Name} (from signatures)");
                                return matchingSig.returnType;
                            }
                        }
                        
                        currentType = currentType.BaseType;
                        if (currentType is not TypeBuilder)
                        {
                            break;
                        }
                    }
                }
                
                var astReturnType = GetMethodReturnTypeFromAst(targetType.Name, methodName, argTypes);
                if (astReturnType != null)
                    return astReturnType;

                try
                {
                    var method = targetType.GetMethod(
                        methodName,
                        BindingFlags.Public | BindingFlags.Instance,
                        Type.DefaultBinder,
                        argTypes,
                        null);

                    if (method != null)
                    {
                        Console.WriteLine($"**[ DEBUG ]       Method {targetType.Name}.{methodName} returns {method.ReturnType.Name} (from reflection)");
                        return method.ReturnType;
                    }
                }
                catch
                {
                    // Ignored
                }

                Console.WriteLine($"**[ WARN ] Could not determine return type for {targetType.Name}.{methodName}, using Object");
            }

            return typeof(object);
        }

        private Type? GetMethodReturnTypeFromAst(string typeName, string methodName, Type[] argumentTypes)
        {
            if (_programAst == null)
                return null;

            var classDecl = _programAst.Classes.FirstOrDefault(c => c.Name == typeName);
            if (classDecl == null)
                return null;

            foreach (var member in classDecl.Members.OfType<MethodDeclaration>())
            {
                if (member.Header.Name == methodName)
                {
                    if (member.Header.Parameters.Count == argumentTypes.Length)
                    {
                        bool match = true;
                        for (int i = 0; i < argumentTypes.Length; i++)
                        {
                            var expectedType = _typeMapper.GetNetType(member.Header.Parameters[i].Type.Name);
                            if (expectedType != argumentTypes[i])
                            {
                                match = false;
                                break;
                            }
                        }

                        if (match)
                        {
                            if (!string.IsNullOrEmpty(member.Header.ReturnType))
                            {
                                var returnType = _typeMapper.GetNetType(member.Header.ReturnType);
                                Console.WriteLine($"**[ DEBUG ]       Method {typeName}.{methodName} returns {returnType.Name} (from AST)");
                                return returnType;
                            }
                            else
                            {
                                // Метод void
                                Console.WriteLine($"**[ DEBUG ]       Method {typeName}.{methodName} returns void (from AST)");
                                return typeof(void);
                            }
                        }
                    }
                }
            }

            return null;
        }

        private Type InferMemberAccessReturnType(MemberAccessExpression memberAccess)
        {
            var targetType = InferExpressionType(memberAccess.Target);
            var memberName = (memberAccess.Member as IdentifierExpression)?.Name;

            if (memberName == null)
                return typeof(object);

            if (targetType.IsArray && memberName == "Length")
                return typeof(int);

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>) && memberName == "Length")
                return typeof(int);

            // Для TypeBuilder используем зарегистрированные поля
            if (targetType is TypeBuilder)
            {
                if (_classFields.TryGetValue(targetType.Name, out var fields))
                {
                    if (fields.TryGetValue(memberName, out var fieldInfo))
                    {
                        return fieldInfo.FieldType;
                    }
                }
                // Если не нашли в текущем классе, проверяем базовые классы
                var baseType = targetType.BaseType;
                while (baseType != null && baseType != typeof(object))
                {
                    if (_classFields.TryGetValue(baseType.Name, out var baseFields))
                    {
                        if (baseFields.TryGetValue(memberName, out var fieldInfo))
                        {
                            return fieldInfo.FieldType;
                        }
                    }
                    baseType = baseType.BaseType;
                }
                return typeof(object);
            }

            var field = targetType.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
                return field.FieldType;

            var property = targetType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
                return property.PropertyType;

            return typeof(object);
        }

        private void GenerateIntegerMethodCall(string methodName, List<ExpressionNode> arguments)
        {
            var integerType = typeof(BuiltinTypes.OInteger);
            
            switch (methodName)
            {
                case "Plus":
                    if (arguments.Count != 1) throw new InvalidOperationException("Plus requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var argTypePlus = InferExpressionType(arguments[0]);
                    if (argTypePlus == typeof(double))
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Plus", new[] { typeof(int), typeof(double) })!);
                    else
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Plus", new[] { typeof(int), typeof(int) })!);
                    break;

                case "Minus":
                    if (arguments.Count != 1) throw new InvalidOperationException("Minus requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var argTypeMinus = InferExpressionType(arguments[0]);
                    if (argTypeMinus == typeof(double))
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Minus", new[] { typeof(int), typeof(double) })!);
                    else
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Minus", new[] { typeof(int), typeof(int) })!);
                    break;

                case "Mult":
                    if (arguments.Count != 1) throw new InvalidOperationException("Mult requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var argTypeMult = InferExpressionType(arguments[0]);
                    if (argTypeMult == typeof(double))
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Mult", new[] { typeof(int), typeof(double) })!);
                    else
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Mult", new[] { typeof(int), typeof(int) })!);
                    break;

                case "Div":
                    if (arguments.Count != 1) throw new InvalidOperationException("Div requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var argTypeDiv = InferExpressionType(arguments[0]);
                    if (argTypeDiv == typeof(double))
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Div", new[] { typeof(int), typeof(double) })!);
                    else
                        _il!.Emit(OpCodes.Call, integerType.GetMethod("Div", new[] { typeof(int), typeof(int) })!);
                    break;

                case "Rem":
                    if (arguments.Count != 1) throw new InvalidOperationException("Rem requires 1 argument");
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("Rem", new[] { typeof(int), typeof(int) })!);
                    break;

                case "UnaryMinus":
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("UnaryMinus")!);
                    break;

                case "Less":
                    if (arguments.Count != 1) throw new InvalidOperationException("Less requires 1 argument");
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("Less")!);
                    break;

                case "LessEqual":
                    if (arguments.Count != 1) throw new InvalidOperationException("LessEqual requires 1 argument");
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("LessEqual")!);
                    break;

                case "Greater":
                    if (arguments.Count != 1) throw new InvalidOperationException("Greater requires 1 argument");
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("Greater")!);
                    break;

                case "GreaterEqual":
                    if (arguments.Count != 1) throw new InvalidOperationException("GreaterEqual requires 1 argument");
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("GreaterEqual")!);
                    break;

                case "Equal":
                    if (arguments.Count != 1) throw new InvalidOperationException("Equal requires 1 argument");
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("Equal")!);
                    break;

                case "toReal":
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("ToReal")!);
                    break;
                case "Print":
                    if (arguments.Count != 0) throw new InvalidOperationException("Print does not accept arguments");
                    _il!.Emit(OpCodes.Call, integerType.GetMethod("Print")!);
                    break;
                default:
                    throw new NotImplementedException($"Integer method '{methodName}' not implemented");
            }
        }

        private void GenerateRealMethodCall(string methodName, List<ExpressionNode> arguments)
        {
            var realType = typeof(BuiltinTypes.OReal);
            var integerType = typeof(BuiltinTypes.OInteger);
            
            switch (methodName)
            {
                case "Plus":
                    GenerateExpression(arguments[0]);
                    // Если аргумент Integer, конвертируем в Real
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Plus")!);
                    break;

                case "Minus":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Minus")!);
                    break;

                case "Mult":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Mult")!);
                    break;

                case "Div":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Div")!);
                    break;

                case "UnaryMinus":
                    _il!.Emit(OpCodes.Call, realType.GetMethod("UnaryMinus")!);
                    break;

                case "Less":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Less")!);
                    break;

                case "LessEqual":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("LessEqual")!);
                    break;

                case "Greater":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Greater")!);
                    break;

                case "GreaterEqual":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("GreaterEqual")!);
                    break;

                case "Equal":
                    GenerateExpression(arguments[0]);
                    if (InferExpressionType(arguments[0]) == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8);
                    }
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Equal")!);
                    break;

                case "toInteger":
                    _il!.Emit(OpCodes.Call, realType.GetMethod("ToInteger")!);
                    break;
                case "Print":
                    if (arguments.Count != 0) throw new InvalidOperationException("Print does not accept arguments");
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Print")!);
                    break;
                default:
                    throw new NotImplementedException($"Real method '{methodName}' not implemented");
            }
        }

        private void GenerateBooleanMethodCall(string methodName, List<ExpressionNode> arguments)
        {
            var boolType = typeof(BuiltinTypes.OBoolean);
            
            switch (methodName)
            {
                case "And":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, boolType.GetMethod("And")!);
                    break;

                case "Or":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, boolType.GetMethod("Or")!);
                    break;

                case "Xor":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, boolType.GetMethod("Xor")!);
                    break;

                case "Not":
                    _il!.Emit(OpCodes.Call, boolType.GetMethod("Not")!);
                    break;

                case "Equal":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, boolType.GetMethod("Equal")!);
                    break;
                    
                case "toInteger":
                    if (arguments.Count != 0) throw new InvalidOperationException("toInteger does not accept arguments");
                    _il!.Emit(OpCodes.Call, boolType.GetMethod("toInteger")!);
                    break;
                    
                case "Print":
                    if (arguments.Count != 0) throw new InvalidOperationException("Print does not accept arguments");
                    _il!.Emit(OpCodes.Call, boolType.GetMethod("Print")!);
                    break;
                default:
                    throw new NotImplementedException($"Boolean method '{methodName}' not implemented");
            }
        }

        private void GenerateArrayMethodCall(string methodName, List<ExpressionNode> arguments, Type arrayType)
        {
            var elementType = arrayType.GetElementType()!;
            var arrayHelperType = typeof(BuiltinTypes.OArray);
            
            switch (methodName)
            {
                case "get":
                    if (arguments.Count != 1) 
                        throw new InvalidOperationException("Array.get requires 1 argument (index)");
                    
                    GenerateExpression(arguments[0]);
                    var getMethod = arrayHelperType.GetMethod("Get")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, getMethod);
                    break;

                case "set":
                    if (arguments.Count != 2) 
                        throw new InvalidOperationException("Array.set requires 2 arguments (index, value)");
                    
                    GenerateExpression(arguments[0]);
                    GenerateExpression(arguments[1]);
                    var setMethod = arrayHelperType.GetMethod("Set")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, setMethod);
                    break;

                case "Length":
                    var lengthMethod = arrayHelperType.GetMethod("GetLength")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, lengthMethod);
                    break;

                case "toList":
                    var toListMethod = arrayHelperType.GetMethod("ToList")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, toListMethod);
                    break;

                default:
                    throw new NotImplementedException($"Array method '{methodName}' not implemented");
            }
        }

        private void GenerateListMethodCall(string methodName, List<ExpressionNode> arguments, Type listType)
        {
            var elementType = listType.GetGenericArguments()[0];
            var listHelperType = typeof(BuiltinTypes.OList);
            
            switch (methodName)
            {
                case "append":
                    if (arguments.Count != 1) 
                        throw new InvalidOperationException("List.append requires 1 argument (value)");
                    
                    GenerateExpression(arguments[0]);
                    var appendMethod = listHelperType.GetMethod("Append")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, appendMethod);
                    break;

                case "head":
                    var headMethod = listHelperType.GetMethod("Head")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, headMethod);
                    break;

                case "tail":
                    var tailMethod = listHelperType.GetMethod("Tail")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, tailMethod);
                    break;

                case "isEmpty":
                    var isEmptyMethod = listHelperType.GetMethod("IsEmpty")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, isEmptyMethod);
                    break;

                case "Length":
                    var lengthMethod = listHelperType.GetMethod("GetLength")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, lengthMethod);
                    break;

                default:
                    throw new NotImplementedException($"List method '{methodName}' not implemented");
            }
        }

        private void GenerateUserMethodCall(ExpressionNode target, string methodName, List<ExpressionNode> arguments, Type targetType)
        {
            Console.WriteLine($"**[ DEBUG ]       Calling user method {methodName} on type {targetType.Name}");

            // Генерируем выражение target
            GenerateExpression(target);
    
            // Для TypeBuilder типов — используем сохранённые сигнатуры методов
            if (targetType is TypeBuilder targetTypeBuilder)
            {
                // ИСПРАВЛЕНИЕ: используем InferExpressionType вместо _typeMapper.InferType
                // чтобы правильно определить типы аргументов (включая локальные переменные и параметры)
                var argTypes = arguments.Select(a => InferExpressionType(a)).ToArray();
                
                // Ищем метод в текущем классе и всех базовых классах
                Type? currentType = targetTypeBuilder;
                while (currentType != null && currentType != typeof(object))
                {
                    var className = currentType.Name;
                    
                    if (_methodSignatures.TryGetValue(className, out var methodList))
                    {
                        // Ищем подходящий метод по имени и сигнатуре
                        var matchingMethod = methodList.FirstOrDefault(m => 
                            m.methodName == methodName && 
                            m.paramTypes.Length == argTypes.Length &&
                            m.paramTypes.SequenceEqual(argTypes, new TypeComparer()));
                        
                        if (matchingMethod != default)
                        {
                            // Генерируем аргументы
                            foreach (var arg in arguments)
                            {
                                GenerateExpression(arg);
                            }
                            Console.WriteLine($"**[ DEBUG ]       Found method signature in {className}: {methodName}({string.Join(", ", argTypes.Select(t => t.Name))})");

                            // Попробуем найти MethodBuilder, зарегистрированный при создании метода
                            if (_methodBuilders.TryGetValue(className, out var builders))
                            {
                                var found = builders.FirstOrDefault(b =>
                                    b.methodName == methodName &&
                                    b.paramTypes.Length == argTypes.Length &&
                                    b.paramTypes.SequenceEqual(argTypes, new TypeComparer()));

                                if (found.methodBuilder != null)
                                {
                                    _il!.Emit(OpCodes.Callvirt, found.methodBuilder);
                                    return;
                                }
                            }
                        }
                    }
                    
                    // Переходим к базовому классу
                    currentType = currentType.BaseType;
                    if (currentType is not TypeBuilder)
                    {
                        break;
                    }
                }
                
                throw new InvalidOperationException(
                    $"Method '{methodName}' not found for type '{targetTypeBuilder.Name}' or its base types with given argument types");
            }

            // Для завершённых типов — используем обычную рефлексию
            var argTypesCompleted = arguments.Select(a => _typeMapper.InferType(a)).ToArray();
            var method = targetType.GetMethod(methodName, argTypesCompleted);
            
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Method '{methodName}' not found in type '{targetType.Name}' " +
                    $"with arguments ({string.Join(", ", argTypesCompleted.Select(t => t.Name))})");
            }

            // Генерируем аргументы
            foreach (var arg in arguments)
            {
                GenerateExpression(arg);
            }

            _il!.Emit(OpCodes.Callvirt, method);
        }

        // Вспомогательный класс для сравнения Type[]
        private class TypeComparer : IEqualityComparer<Type>
        {
            public bool Equals(Type? x, Type? y) => x == y;
            public int GetHashCode(Type obj) => obj.GetHashCode();
        }


        private void GenerateMemberAccess(MemberAccessExpression memberAccess)
        {
            var memberName = (memberAccess.Member as IdentifierExpression)?.Name;

            if (memberName == null)
            {
                throw new InvalidOperationException("Member name not found");
            }

            Type targetType;
            
            if (memberAccess.Target is IdentifierExpression targetIdent)
            {
                targetType = GetIdentifierType(targetIdent.Name);
            }
            else
            {
                targetType = InferExpressionType(memberAccess.Target);
            }

            GenerateExpression(memberAccess.Target);

            if (targetType.IsArray && memberName == "Length")
            {
                var arrayHelperType = typeof(BuiltinTypes.OArray);
                var elementType = targetType.GetElementType()!;
                var lengthMethod = arrayHelperType.GetMethod("GetLength")!.MakeGenericMethod(elementType);
                _il!.Emit(OpCodes.Call, lengthMethod);
                return;
            }

            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>) && memberName == "Length")
            {
                var listHelperType = typeof(BuiltinTypes.OList);
                var elementType = targetType.GetGenericArguments()[0];
                var lengthMethod = listHelperType.GetMethod("GetLength")!.MakeGenericMethod(elementType);
                _il!.Emit(OpCodes.Call, lengthMethod);
                return;
            }

            // Для TypeBuilder ищем поле в зарегистрированных полях
            if (targetType is TypeBuilder)
            {
                var className = targetType.Name;
                if (_classFields.TryGetValue(className, out var classFields) && classFields.TryGetValue(memberName, out var fieldBuilder))
                {
                    _il!.Emit(OpCodes.Ldfld, fieldBuilder);
                    return;
                }
                
                // Проверяем базовые классы
                Type? currentType = targetType.BaseType;
                while (currentType != null && currentType != typeof(object))
                {
                    if (currentType is TypeBuilder && _classFields.TryGetValue(currentType.Name, out var baseFields) && baseFields.TryGetValue(memberName, out var baseField))
                    {
                        _il!.Emit(OpCodes.Ldfld, baseField);
                        return;
                    }
                    currentType = currentType.BaseType;
                }
            }

            var field = targetType.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                _il!.Emit(OpCodes.Ldfld, field);
                return;
            }

            var property = targetType.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
            if (property != null)
            {
                var getter = property.GetGetMethod();
                if (getter != null)
                {
                    _il!.Emit(OpCodes.Callvirt, getter);
                    return;
                }
            }

            throw new InvalidOperationException(
                $"Member '{memberName}' not found on type '{targetType.Name}'.");
        }

        private void GenerateIfStatement(IfStatement ifStmt)
        {
            var elseLabel = _il!.DefineLabel();
            var endLabel = _il.DefineLabel();

            GenerateExpression(ifStmt.Condition);
            _il.Emit(OpCodes.Brfalse, elseLabel);

            // Generate then block
            GenerateMethodBodyContent(ifStmt.ThenBody);
            
            // Check if then block ends with return
            bool thenEndsWithReturn = ifStmt.ThenBody.Elements.Count > 0 && 
                                      ifStmt.ThenBody.Elements[^1] is ReturnStatement;
            
            // Only emit Br if then block doesn't end with return
            if (!thenEndsWithReturn)
            {
                _il.Emit(OpCodes.Br, endLabel);
            }

            _il.MarkLabel(elseLabel);
            if (ifStmt.ElseBody != null)
            {
                GenerateMethodBodyContent(ifStmt.ElseBody.Body);
            }

            // Only mark end label if it's reachable (then doesn't end with return)
            if (!thenEndsWithReturn)
            {
                _il.MarkLabel(endLabel);
            }
        }

        private void GenerateWhileLoop(WhileLoop whileLoop)
        {
            var startLabel = _il!.DefineLabel();
            var endLabel = _il.DefineLabel();

            _il.MarkLabel(startLabel);

            GenerateExpression(whileLoop.Condition);
            _il.Emit(OpCodes.Brfalse, endLabel);

            GenerateMethodBodyContent(whileLoop.Body);
            _il.Emit(OpCodes.Br, startLabel);

            _il.MarkLabel(endLabel);
        }
    }
}
