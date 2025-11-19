using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using OCompiler.Parser;
using OCompiler.Semantic;

namespace OCompiler.CodeGen
{
    public class MethodGenerator
    {
        private readonly TypeMapper _typeMapper;
        private readonly ClassHierarchy _hierarchy;
        private ILGenerator? _il;
        private Dictionary<string, LocalBuilder> _locals;

        public MethodGenerator(TypeMapper typeMapper, ClassHierarchy hierarchy)
        {
            _typeMapper = typeMapper;
            _hierarchy = hierarchy;
            _locals = new Dictionary<string, LocalBuilder>();
        }

        public void GenerateConstructor(TypeBuilder typeBuilder, ConstructorDeclaration ctorDecl, ClassDeclaration classDecl)
        {
            // Определяем параметры конструктора
            var paramTypes = ctorDecl.Parameters
                .Select(p => _typeMapper.GetNetType(p.Type.Name))
                .ToArray();

            var ctorBuilder = typeBuilder.DefineConstructor(
                MethodAttributes.Public,
                CallingConventions.Standard,
                paramTypes
            );

            _il = ctorBuilder.GetILGenerator();
            _locals.Clear();

            // Вызов конструктора базового класса
            _il.Emit(OpCodes.Ldarg_0); // this
            var baseConstructor = typeBuilder.BaseType!.GetConstructor(Type.EmptyTypes);
            _il.Emit(OpCodes.Call, baseConstructor!);

            // Генерация тела конструктора
            if (ctorDecl.Body != null)
            {
                GenerateMethodBody(ctorDecl.Body);
            }

            _il.Emit(OpCodes.Ret);
        }

        public void GenerateMethod(TypeBuilder typeBuilder, MethodDeclaration methodDecl)
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
                paramTypes
            );

            // Если метод без тела (abstract/interface), пропускаем
            if (methodDecl.Body == null)
                return;

            _il = methodBuilder.GetILGenerator();
            _locals.Clear();

            // Генерация тела метода
            GenerateMethodBody(methodDecl.Body);

            // Если метод void и нет явного return
            if (returnType == typeof(void))
            {
                _il.Emit(OpCodes.Ret);
            }
        }

        private void GenerateMethodBody(MethodBodyNode body)
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
                    // Pop результат, если он не используется
                    _il!.Emit(OpCodes.Pop);
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
            Type varType = _typeMapper.InferFieldType(varDecl.Expression);
            var local = _il!.DeclareLocal(varType);
            _locals[varDecl.Identifier] = local;

            // Генерация инициализатора
            GenerateExpression(varDecl.Expression);
            _il.Emit(OpCodes.Stloc, local);
        }

        private void GenerateAssignment(Assignment assignment)
        {
            // Генерация правой части
            GenerateExpression(assignment.Expression);

            // Сохранение в переменную
            if (_locals.TryGetValue(assignment.Identifier, out var local))
            {
                _il!.Emit(OpCodes.Stloc, local);
            }
            else
            {
                throw new InvalidOperationException($"Variable '{assignment.Identifier}' not found");
            }
        }

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
                    if (_locals.TryGetValue(ident.Name, out var local))
                    {
                        _il!.Emit(OpCodes.Ldloc, local);
                    }
                    break;

                case FunctionalCall funcCall:
                    GenerateFunctionCall(funcCall);
                    break;

                case ConstructorInvocation ctor:
                    GenerateConstructorInvocation(ctor);
                    break;

                // TODO: Добавить остальные типы выражений
            }
        }

        private void GenerateFunctionCall(FunctionalCall funcCall)
        {
            // Упрощенная версия - требует расширения
            if (funcCall.Function is MemberAccessExpression memberAccess)
            {
                // Генерация вызова метода на объекте
                GenerateExpression(memberAccess.Target);
                
                // Генерация аргументов
                foreach (var arg in funcCall.Arguments)
                {
                    GenerateExpression(arg);
                }

                // TODO: Получить MethodInfo и вызвать метод
                // _il!.Emit(OpCodes.Call, methodInfo);
            }
        }

        private void GenerateConstructorInvocation(ConstructorInvocation ctor)
        {
            Type type = _typeMapper.GetNetType(ctor.ClassName);
            
            // Для встроенных типов используем прямую инициализацию
            if (ctor.ClassName == "Integer" && ctor.Arguments.Count == 1)
            {
                GenerateExpression(ctor.Arguments[0]);
                return;
            }

            // Для Array
            if (ctor.ClassName == "Array" && ctor.Arguments.Count == 1)
            {
                GenerateExpression(ctor.Arguments[0]); // Размер
                Type elementType = _typeMapper.GetNetType(ctor.GenericParameter!);
                _il!.Emit(OpCodes.Newarr, elementType);
                return;
            }

            // TODO: Обработка остальных конструкторов
        }

        private void GenerateIfStatement(IfStatement ifStmt)
        {
            var elseLabel = _il!.DefineLabel();
            var endLabel = _il!.DefineLabel();

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
            var endLabel = _il!.DefineLabel();

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
