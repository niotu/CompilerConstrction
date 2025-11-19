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
        private ILGenerator? _il;
        private Dictionary<string, LocalBuilder>? _locals;
        private Dictionary<string, FieldBuilder>? _fields;

        public MethodGenerator(TypeMapper typeMapper, ClassHierarchy hierarchy)
        {
            _typeMapper = typeMapper;
            _hierarchy = hierarchy;
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
            // Определяем параметры конструктора
            var paramTypes = ctorDecl.Parameters
                .Select(p => _typeMapper.GetNetType(p.Type.Name))
                .ToArray();

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes);

            _il = ctorBuilder.GetILGenerator();
            _locals = new Dictionary<string, LocalBuilder>();
            _fields = fields;

            // Вызов конструктора базового класса
            _il.Emit(OpCodes.Ldarg_0); // this
            var baseConstructor = typeBuilder.BaseType!.GetConstructor(Type.EmptyTypes);
            _il.Emit(OpCodes.Call, baseConstructor!);

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
                GenerateMethodBody(ctorDecl.Body);
            }

            _il.Emit(OpCodes.Ret);

            Console.WriteLine($"**[ DEBUG ]   Constructor: this({string.Join(", ", paramTypes.Select(t => t.Name))})");
        }

        /// <summary>
        /// Генерирует метод класса.
        /// </summary>
        public void GenerateMethod(
            TypeBuilder typeBuilder, 
            MethodDeclaration methodDecl,
            Dictionary<string, FieldBuilder> fields)
        {
            // Определяем возвращаемый тип
            Type returnType = string.IsNullOrEmpty(methodDecl.Header.ReturnType)
                ? typeof(void)
                : _typeMapper.GetNetType(methodDecl.Header.ReturnType);

            // Определяем параметры
            var paramTypes = methodDecl.Header.Parameters
                .Select(p => _typeMapper.GetNetType(p.Type.Name))
                .ToArray();

            var methodBuilder = typeBuilder.DefineMethod(
                methodDecl.Header.Name,
                MethodAttributes.Public,
                returnType,
                paramTypes);

            // Если метод без тела (forward declaration), пропускаем
            if (methodDecl.Body == null)
            {
                Console.WriteLine($"**[ DEBUG ]   Method (forward): {methodDecl.Header.Name}");
                return;
            }

            _il = methodBuilder.GetILGenerator();
            _locals = new Dictionary<string, LocalBuilder>();
            _fields = fields;

            // Генерация тела метода
            GenerateMethodBody(methodDecl.Body);

            // Если метод void и нет явного return
            if (returnType == typeof(void))
            {
                _il.Emit(OpCodes.Ret);
            }

            Console.WriteLine($"**[ DEBUG ]   Method: {methodDecl.Header.Name}({string.Join(", ", paramTypes.Select(t => t.Name))}) : {returnType.Name}");
        }

        /// <summary>
        /// Генерирует тело метода.
        /// </summary>
        private void GenerateMethodBody(MethodBodyNode body)
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
                    if (!IsVoidExpression(exprStmt.Expression))
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

        private bool IsVoidExpression(ExpressionNode expr)
        {
            // Проверяем, возвращает ли выражение значение
            return false; // TODO: Реализовать проверку
        }

        /// <summary>
        /// Генерирует объявление переменной.
        /// </summary>
        private void GenerateVariableDeclaration(VariableDeclaration varDecl)
        {
            Type varType = _typeMapper.InferType(varDecl.Expression);
            var local = _il!.DeclareLocal(varType);
            _locals![varDecl.Identifier] = local;

            // Генерация инициализатора
            GenerateExpression(varDecl.Expression);
            _il.Emit(OpCodes.Stloc, local);
        }

        /// <summary>
        /// Генерирует присваивание.
        /// </summary>
        private void GenerateAssignment(Assignment assignment)
        {
            // Проверяем, это локальная переменная или поле
            if (_locals!.TryGetValue(assignment.Identifier, out var local))
            {
                // Локальная переменная
                GenerateExpression(assignment.Expression);
                _il!.Emit(OpCodes.Stloc, local);
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
                throw new InvalidOperationException($"Variable or field '{assignment.Identifier}' not found");
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
            Type type = _typeMapper.GetNetType(ctor.ClassName);

            // Специальная обработка для примитивных типов
            if (ctor.ClassName == "Integer" && ctor.Arguments.Count == 1)
            {
                GenerateExpression(ctor.Arguments[0]);
                return;
            }

            if (ctor.ClassName == "Real" && ctor.Arguments.Count == 1)
            {
                GenerateExpression(ctor.Arguments[0]);
                _il!.Emit(OpCodes.Conv_R8);
                return;
            }

            if (ctor.ClassName == "Boolean" && ctor.Arguments.Count == 1)
            {
                GenerateExpression(ctor.Arguments[0]);
                return;
            }

            // Массивы: Array[Integer](10)
            if (ctor.ClassName == "Array" && ctor.Arguments.Count == 1)
            {
                GenerateExpression(ctor.Arguments[0]); // Размер
                Type elementType = _typeMapper.GetNetType(ctor.GenericParameter!);
                _il!.Emit(OpCodes.Newarr, elementType);
                return;
            }

            // Списки: List[Integer]()
            if (ctor.ClassName == "List")
            {
                Type elementType = _typeMapper.GetNetType(ctor.GenericParameter!);
                Type listType = typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);
                var listCtor = listType.GetConstructor(Type.EmptyTypes);
                _il!.Emit(OpCodes.Newobj, listCtor!);
                return;
            }

            // Пользовательские классы
            var argTypes = ctor.Arguments.Select(a => _typeMapper.InferType(a)).ToArray();
            var constructor = type.GetConstructor(argTypes);
            
            if (constructor == null)
            {
                throw new InvalidOperationException($"Constructor not found for type '{ctor.ClassName}'");
            }

            foreach (var arg in ctor.Arguments)
            {
                GenerateExpression(arg);
            }

            _il!.Emit(OpCodes.Newobj, constructor);
        }

        private void GenerateFunctionCall(FunctionalCall funcCall)
        {
            // TODO: Полная реализация вызовов методов
            throw new NotImplementedException("Function calls not fully implemented yet");
        }

        private void GenerateMemberAccess(MemberAccessExpression memberAccess)
        {
            // TODO: Полная реализация доступа к членам
            throw new NotImplementedException("Member access not fully implemented yet");
        }

        private void GenerateIfStatement(IfStatement ifStmt)
        {
            var elseLabel = _il!.DefineLabel();
            var endLabel = _il.DefineLabel();

            // Условие
            GenerateExpression(ifStmt.Condition);
            _il.Emit(OpCodes.Brfalse, elseLabel);

            // Then-ветка
            GenerateMethodBody(ifStmt.ThenBody);
            _il.Emit(OpCodes.Br, endLabel);

            // Else-ветка
            _il.MarkLabel(elseLabel);
            if (ifStmt.ElseBody != null)
            {
                GenerateMethodBody(ifStmt.ElseBody.Body);
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
            GenerateMethodBody(whileLoop.Body);
            _il.Emit(OpCodes.Br, startLabel);

            _il.MarkLabel(endLabel);
        }
    }
}
