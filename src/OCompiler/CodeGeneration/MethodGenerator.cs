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
        private Dictionary<string, Type>? _completedTypes; // НОВОЕ: Завершённые типы из CodeGenerator
        private Dictionary<string, TypeBuilder>? _typeBuilders; // НОВОЕ: TypeBuilders для незавершённых типов
        private Dictionary<string, Dictionary<string, ConstructorBuilder>>? _constructorsBySignature; // НОВОЕ: Сохранённые конструкторы по сигнатуре
        private Dictionary<string, Dictionary<string, MethodBuilder>>? _methodsBySignature; // НОВОЕ: Сохранённые методы по сигнатуре
        private ILGenerator? _il;
        private Dictionary<string, LocalBuilder>? _locals;
        private Dictionary<string, Type>? _localTypes; // НОВОЕ: Типы локальных переменных
        private Dictionary<string, FieldBuilder>? _fields;
        private Dictionary<string, int>? _parameters;
        private Dictionary<string, Type>? _parameterTypes; // НОВОЕ: Типы параметров

        public MethodGenerator(TypeMapper typeMapper, ClassHierarchy hierarchy)
        {
            _typeMapper = typeMapper;
            _hierarchy = hierarchy;
            _completedTypes = new Dictionary<string, Type>();
            _typeBuilders = new Dictionary<string, TypeBuilder>();
            _constructorsBySignature = new Dictionary<string, Dictionary<string, ConstructorBuilder>>();
            _methodsBySignature = new Dictionary<string, Dictionary<string, MethodBuilder>>();
        }

        /// <summary>
        /// Устанавливает словарь завершённых типов (вызывается из CodeGenerator после CreateType).
        /// </summary>
        public void SetCompletedTypes(Dictionary<string, Type> completedTypes)
        {
            _completedTypes = completedTypes;
        }

        /// <summary>
        /// Устанавливает словарь TypeBuilders (вызывается из CodeGenerator в Phase 1).
        /// </summary>
        public void SetTypeBuilders(Dictionary<string, TypeBuilder> typeBuilders)
        {
            _typeBuilders = typeBuilders;
        }

        /// <summary>
        /// Сохраняет конструктор для последующего поиска.
        /// Сигнатура: "ClassName|param1Type|param2Type|..."
        /// </summary>
        public void RegisterConstructor(string className, Type[] paramTypes, ConstructorBuilder ctorBuilder)
        {
            if (!_constructorsBySignature!.ContainsKey(className))
            {
                _constructorsBySignature[className] = new Dictionary<string, ConstructorBuilder>();
            }
            
            string signature = string.Join("|", paramTypes.Select(t => t.FullName));
            _constructorsBySignature[className][signature] = ctorBuilder;
        }

        /// <summary>
        /// Сохраняет метод для последующего поиска.
        /// </summary>
        public void RegisterMethod(string className, string methodName, Type[] paramTypes, MethodBuilder methodBuilder)
        {
            if (!_methodsBySignature!.ContainsKey(className))
            {
                _methodsBySignature[className] = new Dictionary<string, MethodBuilder>();
            }
            
            string signature = $"{methodName}|{string.Join("|", paramTypes.Select(t => t.FullName))}";
            _methodsBySignature[className][signature] = methodBuilder;
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
            var paramTypes = ctorDecl.Parameters
                .Select(p => _typeMapper.GetNetType(p.Type.Name))
                .ToArray();

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes);

            // Сохраняем конструктор для последующего использования
            RegisterConstructor(typeBuilder.Name, paramTypes, ctorBuilder);

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

            // ИСПРАВЛЕНИЕ: Безопасное получение конструктора базового класса
            ConstructorInfo? baseConstructor;
            
            if (typeBuilder.BaseType == typeof(object))
            {
                baseConstructor = typeof(object).GetConstructor(Type.EmptyTypes);
            }
            else
            {
                // Для завершённых типов можно использовать GetConstructor
                // Для TypeBuilder - нужна альтернатива
                try
                {
                    baseConstructor = typeBuilder.BaseType!.GetConstructor(Type.EmptyTypes);
                }
                catch (NotSupportedException)
                {
                    // Если базовый тип - TypeBuilder, используем object
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
        /// Генерирует метод класса (определение и IL всё в одном).
        /// </summary>
        public void GenerateMethod(
            TypeBuilder typeBuilder, 
            MethodDeclaration methodDecl,
            Dictionary<string, FieldBuilder> fields)
        {
            Type returnType = string.IsNullOrEmpty(methodDecl.Header?.ReturnType)
                ? typeof(void)
                : _typeMapper.GetNetType(methodDecl.Header.ReturnType);

            var paramTypes = methodDecl.Header?.Parameters
                .Select(p => _typeMapper.GetNetType(p.Type.Name))
                .ToArray() ?? Type.EmptyTypes;

            var methodBuilder = typeBuilder.DefineMethod(
                methodDecl.Header?.Name ?? "Unknown",
                MethodAttributes.Public,
                returnType,
                paramTypes);

            // Сохраняем метод для последующего использования
            RegisterMethod(typeBuilder.Name, methodDecl.Header?.Name ?? "Unknown", paramTypes, methodBuilder);

            if (methodDecl.Body == null)
            {
                Console.WriteLine($"**[ DEBUG ]   Method (forward): {methodDecl.Header?.Name}");
                return;
            }

            _il = methodBuilder.GetILGenerator();
            _locals = new Dictionary<string, LocalBuilder>();
            _localTypes = new Dictionary<string, Type>();
            _fields = fields;
            _parameters = new Dictionary<string, int>();
            _parameterTypes = new Dictionary<string, Type>();

            // Регистрируем параметры
            if (methodDecl.Header != null)
            {
                for (int i = 0; i < methodDecl.Header.Parameters.Count; i++)
                {
                    var param = methodDecl.Header.Parameters[i];
                    _parameters[param.Identifier] = i + 1;
                    _parameterTypes[param.Identifier] = paramTypes[i];
                }
            }

            // Генерация тела метода
            GenerateMethodBodyContent(methodDecl.Body);

            // Если метод void и нет явного return
            if (returnType == typeof(void))
            {
                _il.Emit(OpCodes.Ret);
            }

            Console.WriteLine($"**[ DEBUG ]   Method: {methodDecl.Header?.Name}({string.Join(", ", paramTypes.Select(t => t.Name))}) : {returnType.Name}");
        }

        /// <summary>
        /// Генерирует тело метода (внутренний метод для обработки элементов).
        /// </summary>
        private void GenerateMethodBodyContent(MethodBodyNode body)
        {
            foreach (var element in body.Elements)
            {
                GenerateBodyElement(element);
            }
        }

        /// <summary>
        /// Генерирует элемент тела метода.
        /// </summary>
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
                    // Pop результат, если он не используется
                    var exprType = _typeMapper.InferType(exprStmt.Expression);
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

        /// <summary>
        /// Генерирует объявление переменной.
        /// </summary>
        private void GenerateVariableDeclaration(VariableDeclaration varDecl)
        {
            Type varType = _typeMapper.InferType(varDecl.Expression);
            var local = _il!.DeclareLocal(varType);
            
            _locals![varDecl.Identifier] = local;
            _localTypes![varDecl.Identifier] = varType; // НОВОЕ: Сохраняем тип

            // Генерация инициализатора
            GenerateExpression(varDecl.Expression);
            _il.Emit(OpCodes.Stloc, local);
        }

/// <summary>
/// Определяет тип идентификатора (переменная, параметр или поле).
/// </summary>
        private Type GetIdentifierType(string identifier)
        {
            // 1. Проверяем локальные переменные
            if (_localTypes!.TryGetValue(identifier, out var localType))
            {
                return localType;
            }

            // 2. Проверяем параметры метода
            if (_parameterTypes!.TryGetValue(identifier, out var paramType))
            {
                return paramType;
            }

            // 3. Проверяем поля класса
            if (_fields!.TryGetValue(identifier, out var field))
            {
                return field.FieldType;
            }

            // 4. Не найдено - возвращаем object как fallback
            Console.WriteLine($"**[ WARN ] Could not determine type for identifier '{identifier}', using Object");
            return typeof(object);
        }


        /// <summary>
        /// Генерирует присваивание.
        /// </summary>
        private void GenerateAssignment(Assignment assignment)
        {
            // Проверяем, это локальная переменная, параметр или поле
            if (_locals!.TryGetValue(assignment.Identifier, out var local))
            {
                // Локальная переменная
                GenerateExpression(assignment.Expression);
                _il!.Emit(OpCodes.Stloc, local);
            }
            else if (_parameters!.TryGetValue(assignment.Identifier, out var paramIndex))
            {
                // Параметр метода
                GenerateExpression(assignment.Expression);
                _il!.Emit(OpCodes.Starg, paramIndex);
            }
            else if (_fields!.TryGetValue(assignment.Identifier, out var field))
            {
                // Поле класса
                _il!.Emit(OpCodes.Ldarg_0); // this
                GenerateExpression(assignment.Expression);
                _il.Emit(OpCodes.Stfld, field);
            }
            else
            {
                throw new InvalidOperationException($"Variable, parameter or field '{assignment.Identifier}' not found");
            }
        }

        /// <summary>
        /// Генерирует выражение.
        /// </summary>
        private void GenerateExpression(ExpressionNode expr)
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
            // Специальная обработка для примитивных типов
            // Integer(5) -> просто загружаем 5
            if (ctor.ClassName == "Integer")
            {
                if (ctor.Arguments.Count == 0)
                {
                    // Integer() -> 0
                    _il!.Emit(OpCodes.Ldc_I4_0);
                }
                else if (ctor.Arguments.Count == 1)
                {
                    // Integer(value) -> загружаем value
                    GenerateExpression(ctor.Arguments[0]);
                    
                    // Если аргумент не int, нужно конвертировать
                    var argType = _typeMapper.InferType(ctor.Arguments[0]);
                    if (argType == typeof(double))
                    {
                        _il!.Emit(OpCodes.Conv_I4); // Real -> Integer
                    }
                    else if (argType == typeof(bool))
                    {
                        // Boolean -> Integer (true=1, false=0)
                        // Уже на стеке как 0 или 1
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Integer constructor expects 0 or 1 argument, got {ctor.Arguments.Count}");
                }
                return;
            }

            // Real(3.14) -> загружаем 3.14
            if (ctor.ClassName == "Real")
            {
                if (ctor.Arguments.Count == 0)
                {
                    // Real() -> 0.0
                    _il!.Emit(OpCodes.Ldc_R8, 0.0);
                }
                else if (ctor.Arguments.Count == 1)
                {
                    // Real(value) -> загружаем value
                    GenerateExpression(ctor.Arguments[0]);
                    
                    // Если аргумент не double, конвертируем
                    var argType = _typeMapper.InferType(ctor.Arguments[0]);
                    if (argType == typeof(int))
                    {
                        _il!.Emit(OpCodes.Conv_R8); // Integer -> Real
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Real constructor expects 0 or 1 argument, got {ctor.Arguments.Count}");
                }
                return;
            }

            // Boolean(true) -> загружаем true/false
            if (ctor.ClassName == "Boolean")
            {
                if (ctor.Arguments.Count == 0)
                {
                    // Boolean() -> false
                    _il!.Emit(OpCodes.Ldc_I4_0);
                }
                else if (ctor.Arguments.Count == 1)
                {
                    // Boolean(value) -> загружаем value
                    GenerateExpression(ctor.Arguments[0]);
                }
                else
                {
                    throw new InvalidOperationException($"Boolean constructor expects 0 or 1 argument, got {ctor.Arguments.Count}");
                }
                return;
            }

            // Массивы: Array[Integer](10)
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
                GenerateExpression(ctor.Arguments[0]); // Размер массива
                _il!.Emit(OpCodes.Newarr, elementType);
                return;
            }

            // Списки: List[Integer]()
            if (ctor.ClassName == "List")
            {
                if (string.IsNullOrEmpty(ctor.GenericParameter))
                {
                    throw new InvalidOperationException("List constructor requires generic parameter: List[T]");
                }
                
                Type elementType = _typeMapper.GetNetType(ctor.GenericParameter);
                Type listType = typeof(List<>).MakeGenericType(elementType);
                var listCtor = listType.GetConstructor(Type.EmptyTypes);
                
                if (listCtor == null)
                {
                    throw new InvalidOperationException($"Could not find parameterless constructor for List<{elementType.Name}>");
                }
                
                _il!.Emit(OpCodes.Newobj, listCtor);
                return;
            }

            // Пользовательские классы
            var argTypes = ctor.Arguments.Select(a => _typeMapper.InferType(a)).ToArray();
            ConstructorInfo? constructor = null;
            
            // НОВОЕ: Сначала пытаемся получить завершённый тип
            if (_completedTypes?.TryGetValue(ctor.ClassName, out var completedType) == true)
            {
                constructor = completedType.GetConstructor(argTypes);
            }
            else if (_typeBuilders?.TryGetValue(ctor.ClassName, out var typeBuilder) == true)
            {
                // Для незавершённых типов - ищем в сохранённых конструкторах
                if (_constructorsBySignature!.TryGetValue(ctor.ClassName, out var ctorsBySignature))
                {
                    string signature = string.Join("|", argTypes.Select(t => t.FullName));
                    if (ctorsBySignature.TryGetValue(signature, out var ctorBuilder))
                    {
                        constructor = ctorBuilder as ConstructorInfo;
                    }
                }
            }
            else
            {
                // Fallback на TypeMapper
                try
                {
                    var type = _typeMapper.GetNetType(ctor.ClassName);
                    constructor = type.GetConstructor(argTypes);
                }
                catch (InvalidOperationException)
                {
                    // Если тип не найден вообще
                    throw new InvalidOperationException(
                        $"Cannot create instance of '{ctor.ClassName}': type not found. " +
                        $"Make sure the type is defined before being used.");
                }
            }
            
            if (constructor == null)
            {
                throw new InvalidOperationException(
                    $"Constructor not found for type '{ctor.ClassName}' " +
                    $"with arguments ({string.Join(", ", argTypes.Select(t => t.Name))})");
            }

            // Генерируем аргументы
            foreach (var arg in ctor.Arguments)
            {
                GenerateExpression(arg);
            }

            _il!.Emit(OpCodes.Newobj, constructor);
        }

        private void GenerateFunctionCall(FunctionalCall funcCall)
        {
            // СЛУЧАЙ 1: Вызов конструктора встроенного типа (Integer(0), Real(3.14), Boolean(true))
            if (funcCall.Function is IdentifierExpression identFunc)
            {
                var typeName = identFunc.Name;
                
                // Проверяем, это конструктор встроенного типа
                if (_typeMapper.IsBuiltInType(typeName))
                {
                    // Обрабатываем как конструктор
                    var pseudoConstructor = new ConstructorInvocation(
                        typeName,
                        null, // нет generic параметра
                        funcCall.Arguments
                    );
                    GenerateConstructorInvocation(pseudoConstructor);
                    return;
                }

                // НОВОЕ: Проверяем, это ли пользовательский тип (класс)
                // Если это вызов типа как функции, это конструктор
                if (_typeMapper.IsKnownType(typeName))
                {
                    // Пользовательский класс - это конструктор
                    var pseudoConstructor = new ConstructorInvocation(
                        typeName,
                        null,
                        funcCall.Arguments
                    );
                    GenerateConstructorInvocation(pseudoConstructor);
                    return;
                }
                
                // Это может быть вызов метода текущего класса или глобальной функции
                throw new NotImplementedException(
                    $"Direct function call '{typeName}' not recognized. " +
                    $"If this is a constructor, use 'new' keyword or ensure it's a known type.");
            }
            
            // СЛУЧАЙ 2: Вызов метода на объекте
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                var methodName = (memberAccess.Member as IdentifierExpression)?.Name;

                if (methodName == null)
                {
                    throw new InvalidOperationException("Method name not found in member access");
                }

                // ИСПРАВЛЕНИЕ: Получаем реальный тип
                Type targetType;
                
                if (memberAccess.Target is IdentifierExpression targetIdent)
                {
                    targetType = GetIdentifierType(targetIdent.Name);
                }
                else
                {
                    targetType = _typeMapper.InferType(memberAccess.Target);
                }

                // Генерируем целевой объект
                GenerateExpression(memberAccess.Target);

                // Обработка встроенных методов
                if (targetType == typeof(int))
                {
                    GenerateIntegerMethodCall(methodName, funcCall.Arguments);
                    return;
                }

                if (targetType == typeof(double))
                {
                    GenerateRealMethodCall(methodName, funcCall.Arguments);
                    return;
                }

                if (targetType == typeof(bool))
                {
                    GenerateBooleanMethodCall(methodName, funcCall.Arguments);
                    return;
                }

                // Обработка методов массива
                if (targetType.IsArray)
                {
                    GenerateArrayMethodCall(methodName, funcCall.Arguments, targetType);
                    return;
                }

                // Обработка методов списка
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>))
                {
                    GenerateListMethodCall(methodName, funcCall.Arguments, targetType);
                    return;
                }

                // Обработка пользовательских методов
                GenerateUserMethodCall(memberAccess.Target, methodName, funcCall.Arguments, targetType);
                return;
            }

            throw new NotImplementedException($"Function call pattern not recognized");
        }


        private void GenerateUserMethodCall(ExpressionNode target, string methodName, List<ExpressionNode> arguments, Type targetType)
        {
            // Целевой объект уже на стеке (загружен в GenerateFunctionCall)
            
            // Генерируем аргументы
            var argTypes = arguments.Select(arg => _typeMapper.InferType(arg)).ToArray();
            
            foreach (var arg in arguments)
            {
                GenerateExpression(arg);
            }
            
            MethodInfo? method = null;
            
            // НОВОЕ: Проверяем, является ли targetType TypeBuilder
            if (targetType is TypeBuilder targetTypeBuilder)
            {
                // Для TypeBuilder - ищем в сохранённых методах
                if (_methodsBySignature!.TryGetValue(targetTypeBuilder.Name, out var methodsBySignature))
                {
                    string signature = $"{methodName}|{string.Join("|", argTypes.Select(t => t.FullName))}";
                    if (methodsBySignature.TryGetValue(signature, out var methodBuilder))
                    {
                        method = methodBuilder as MethodInfo;
                    }
                }
            }
            else
            {
                // Для обычных типов
                method = targetType.GetMethod(
                    methodName,
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    argTypes,
                    null);
            }
            
            if (method == null)
            {
                throw new InvalidOperationException(
                    $"Method '{methodName}' not found in type '{targetType.Name}' " +
                    $"with arguments ({string.Join(", ", argTypes.Select(t => t.Name))})");
            }
            
            // Вызываем метод
            _il!.Emit(OpCodes.Callvirt, method);
        }

        private void GenerateMemberAccess(MemberAccessExpression memberAccess)
        {
            var memberName = (memberAccess.Member as IdentifierExpression)?.Name;

            if (memberName == null)
            {
                throw new InvalidOperationException("Member name not found");
            }

            // ИСПРАВЛЕНИЕ: Определяем реальный тип, учитывая локальные переменные
            Type targetType;
            
            if (memberAccess.Target is IdentifierExpression targetIdent)
            {
                // Сначала пытаемся получить точный тип из контекста
                targetType = GetIdentifierType(targetIdent.Name);
            }
            else
            {
                // Для сложных выражений используем TypeMapper
                targetType = _typeMapper.InferType(memberAccess.Target);
            }

            // Генерируем целевой объект
            GenerateExpression(memberAccess.Target);

            // Обработка свойств массива
            if (targetType.IsArray && memberName == "Length")
            {
                var arrayHelperType = typeof(BuiltinTypes.OArray);
                var elementType = targetType.GetElementType()!;
                var lengthMethod = arrayHelperType.GetMethod("GetLength")!.MakeGenericMethod(elementType);
                _il!.Emit(OpCodes.Call, lengthMethod);
                return;
            }

            // Обработка свойств списка
            if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(List<>) && memberName == "Length")
            {
                var listHelperType = typeof(BuiltinTypes.OList);
                var elementType = targetType.GetGenericArguments()[0];
                var lengthMethod = listHelperType.GetMethod("GetLength")!.MakeGenericMethod(elementType);
                _il!.Emit(OpCodes.Call, lengthMethod);
                return;
            }

            // Обработка полей пользовательских классов
            var field = targetType.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                _il!.Emit(OpCodes.Ldfld, field);
                return;
            }

            // Обработка свойств
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
                $"Member '{memberName}' not found on type '{targetType.Name}'. " +
                $"Available members: {string.Join(", ", targetType.GetMembers().Select(m => m.Name))}");
        }



        private void GenerateIntegerMethodCall(string methodName, List<ExpressionNode> arguments)
        {
            var integerType = typeof(BuiltinTypes.OInteger);
            
            switch (methodName)
            {
                case "Plus":
                    if (arguments.Count != 1) throw new InvalidOperationException("Plus requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var plusMethod = integerType.GetMethod("Plus");
                    _il!.Emit(OpCodes.Call, plusMethod!);
                    break;

                case "Minus":
                    if (arguments.Count != 1) throw new InvalidOperationException("Minus requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var minusMethod = integerType.GetMethod("Minus");
                    _il!.Emit(OpCodes.Call, minusMethod!);
                    break;

                case "Mult":
                    if (arguments.Count != 1) throw new InvalidOperationException("Mult requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var multMethod = integerType.GetMethod("Mult");
                    _il!.Emit(OpCodes.Call, multMethod!);
                    break;

                case "Div":
                    if (arguments.Count != 1) throw new InvalidOperationException("Div requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var divMethod = integerType.GetMethod("Div");
                    _il!.Emit(OpCodes.Call, divMethod!);
                    break;

                case "Rem":
                    if (arguments.Count != 1) throw new InvalidOperationException("Rem requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var remMethod = integerType.GetMethod("Rem");
                    _il!.Emit(OpCodes.Call, remMethod!);
                    break;

                case "UnaryMinus":
                    var unaryMethod = integerType.GetMethod("UnaryMinus");
                    _il!.Emit(OpCodes.Call, unaryMethod!);
                    break;

                case "Less":
                    if (arguments.Count != 1) throw new InvalidOperationException("Less requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var lessMethod = integerType.GetMethod("Less");
                    _il!.Emit(OpCodes.Call, lessMethod!);
                    break;

                case "LessEqual":
                    if (arguments.Count != 1) throw new InvalidOperationException("LessEqual requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var lessEqualMethod = integerType.GetMethod("LessEqual");
                    _il!.Emit(OpCodes.Call, lessEqualMethod!);
                    break;

                case "Greater":
                    if (arguments.Count != 1) throw new InvalidOperationException("Greater requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var greaterMethod = integerType.GetMethod("Greater");
                    _il!.Emit(OpCodes.Call, greaterMethod!);
                    break;

                case "GreaterEqual":
                    if (arguments.Count != 1) throw new InvalidOperationException("GreaterEqual requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var greaterEqualMethod = integerType.GetMethod("GreaterEqual");
                    _il!.Emit(OpCodes.Call, greaterEqualMethod!);
                    break;

                case "Equal":
                    if (arguments.Count != 1) throw new InvalidOperationException("Equal requires 1 argument");
                    GenerateExpression(arguments[0]);
                    var equalMethod = integerType.GetMethod("Equal");
                    _il!.Emit(OpCodes.Call, equalMethod!);
                    break;

                case "toReal":
                    var toRealMethod = integerType.GetMethod("ToReal");
                    _il!.Emit(OpCodes.Call, toRealMethod!);
                    break;

                default:
                    throw new NotImplementedException($"Integer method '{methodName}' not implemented");
            }
        }          

        private void GenerateRealMethodCall(string methodName, List<ExpressionNode> arguments)
        {
            var realType = typeof(BuiltinTypes.OReal);
            
            switch (methodName)
            {
                case "Plus":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Plus")!);
                    break;

                case "Minus":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Minus")!);
                    break;

                case "Mult":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Mult")!);
                    break;

                case "Div":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Div")!);
                    break;

                case "UnaryMinus":
                    _il!.Emit(OpCodes.Call, realType.GetMethod("UnaryMinus")!);
                    break;

                case "Less":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Less")!);
                    break;

                case "LessEqual":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("LessEqual")!);
                    break;

                case "Greater":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Greater")!);
                    break;

                case "GreaterEqual":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("GreaterEqual")!);
                    break;

                case "Equal":
                    GenerateExpression(arguments[0]);
                    _il!.Emit(OpCodes.Call, realType.GetMethod("Equal")!);
                    break;

                case "toInteger":
                    _il!.Emit(OpCodes.Call, realType.GetMethod("ToInteger")!);
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
                    
                    // Массив уже на стеке, добавляем индекс
                    GenerateExpression(arguments[0]);
                    
                    var getMethod = arrayHelperType.GetMethod("Get")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, getMethod);
                    break;

                case "set":
                    if (arguments.Count != 2) 
                        throw new InvalidOperationException("Array.set requires 2 arguments (index, value)");
                    
                    // Массив уже на стеке, добавляем индекс и значение
                    GenerateExpression(arguments[0]); // индекс
                    GenerateExpression(arguments[1]); // значение
                    
                    var setMethod = arrayHelperType.GetMethod("Set")!.MakeGenericMethod(elementType);
                    _il!.Emit(OpCodes.Call, setMethod);
                    // set возвращает void, ничего не остаётся на стеке
                    break;

                case "Length":
                    // Length уже обрабатывается в GenerateMemberAccess
                    // Но на всякий случай поддержим вызов как метод
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
                    if (arguments.Count != 1) throw new InvalidOperationException("List.append requires 1 argument");
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

        private void GenerateIfStatement(IfStatement ifStmt)
        {
            var elseLabel = _il!.DefineLabel();
            var endLabel = _il.DefineLabel();

            // Условие
            GenerateExpression(ifStmt.Condition);
            _il.Emit(OpCodes.Brfalse, elseLabel);

            // Then-ветка
            GenerateMethodBodyContent(ifStmt.ThenBody);
            _il.Emit(OpCodes.Br, endLabel);

            // Else-ветка
            _il.MarkLabel(elseLabel);
            if (ifStmt.ElseBody != null)
            {
                GenerateMethodBodyContent(ifStmt.ElseBody.Body);
            }

            _il.MarkLabel(endLabel);
        }

        private void GenerateWhileLoop(WhileLoop whileLoop)
        {
            var startLabel = _il!.DefineLabel();
            var endLabel = _il.DefineLabel();

            _il.MarkLabel(startLabel);

            // Условие
            GenerateExpression(whileLoop.Condition);
            _il.Emit(OpCodes.Brfalse, endLabel);

            // Тело цикла
            GenerateMethodBodyContent(whileLoop.Body);
            _il.Emit(OpCodes.Br, startLabel);

            _il.MarkLabel(endLabel);
        }
    }
}
