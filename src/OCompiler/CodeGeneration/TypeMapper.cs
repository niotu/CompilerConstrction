using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using OCompiler.Parser;
using OCompiler.Semantic;

namespace OCompiler.CodeGeneration
{
    /// <summary>
    /// Маппинг типов языка O на типы .NET.
    /// </summary>
    public class TypeMapper
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly ClassHierarchy _hierarchy;
        private readonly Dictionary<string, Type> _typeMap;

        public TypeMapper(ModuleBuilder moduleBuilder, ClassHierarchy hierarchy)
        {
            _moduleBuilder = moduleBuilder;
            _hierarchy = hierarchy;
            _typeMap = new Dictionary<string, Type>();
            
            RegisterBuiltInTypes();
        }

        /// <summary>
        /// Регистрация встроенных типов O -> .NET.
        /// </summary>
        private void RegisterBuiltInTypes()
        {
            // Примитивные типы
            _typeMap["Integer"] = typeof(int);
            _typeMap["Real"] = typeof(double);
            _typeMap["Boolean"] = typeof(bool);
            
            // Базовые типы иерархии
            _typeMap["Class"] = typeof(object);
            _typeMap["AnyValue"] = typeof(object);
            _typeMap["AnyRef"] = typeof(object);
            _typeMap["Array"] = typeof(Array);
            _typeMap["List"] = typeof(System.Collections.IList);

            // Array и List обрабатываются специально (generic типы)
        }

        /// <summary>
        /// Регистрация пользовательского типа.
        /// </summary>
        public void RegisterUserType(string oTypeName, Type netType)
        {
            _typeMap[oTypeName] = netType;
        }

        /// <summary>
        /// Проверка, является ли тип встроенным.
        /// </summary>
        public bool IsBuiltInType(string typeName)
        {
            return typeName is "Integer" or "Real" or "Boolean" or 
                   "Class" or "AnyValue" or "AnyRef" or "Array" or "List";
        }

        /// <summary>
        /// Проверка, является ли тип известным (встроенный или зарегистрированный пользовательский).
        /// </summary>
        public bool IsKnownType(string typeName)
        {
            // Проверяем встроенные типы
            if (IsBuiltInType(typeName))
                return true;
            
            // Проверяем зарегистрированные типы (пользовательские классы)
            return _typeMap.ContainsKey(typeName);
        }

        /// <summary>
        /// Получение .NET типа по имени типа O.
        /// </summary>
        public Type GetNetType(string oTypeName)
        {
            // Обработка generic типов: Array[Integer] -> int[]
            if (oTypeName.StartsWith("Array[") && oTypeName.EndsWith("]"))
            {
                var elementTypeName = oTypeName.Substring(6, oTypeName.Length - 7);
                var elementType = GetNetType(elementTypeName);
                return elementType.MakeArrayType();
            }

            // List[T] -> List<T>
            if (oTypeName.StartsWith("List[") && oTypeName.EndsWith("]"))
            {
                var elementTypeName = oTypeName.Substring(5, oTypeName.Length - 6);
                var elementType = GetNetType(elementTypeName);
                return typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);
            }

            // Поиск в зарегистрированных типах
            if (_typeMap.TryGetValue(oTypeName, out var netType))
            {
                return netType;
            }

            throw new InvalidOperationException($"Type '{oTypeName}' not found in type map");
        }

        /// <summary>
        /// Вывод типа из выражения.
        /// </summary>
        public Type InferType(ExpressionNode expression)
        {
            return expression switch
            {
                IntegerLiteral => typeof(int),
                RealLiteral => typeof(double),
                BooleanLiteral => typeof(bool),
                
                ConstructorInvocation ctor => GetConstructorType(ctor),
                
                IdentifierExpression ident => GetIdentifierType(ident),
                
                FunctionalCall funcCall => InferFunctionCallType(funcCall),
                
                MemberAccessExpression member => InferMemberAccessType(member),
                
                _ => typeof(object) // Fallback
            };
        }

        private Type GetConstructorType(ConstructorInvocation ctor)
        {
            if (ctor.ClassName == "Array" && !string.IsNullOrEmpty(ctor.GenericParameter))
            {
                var elementType = GetNetType(ctor.GenericParameter);
                return elementType.MakeArrayType();
            }

            if (ctor.ClassName == "List" && !string.IsNullOrEmpty(ctor.GenericParameter))
            {
                var elementType = GetNetType(ctor.GenericParameter);
                return typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);
            }

            return GetNetType(ctor.ClassName);
        }

        private Type GetIdentifierType(IdentifierExpression ident)
        {
            // Проверяем, не является ли это типом
            if (_typeMap.ContainsKey(ident.Name))
            {
                return _typeMap[ident.Name];
            }

            return typeof(object);
        }

        private Type InferFunctionCallType(FunctionalCall funcCall)
        {
            // Упрощённый вывод типа для вызовов функций
            // TODO: Полная реализация с учётом сигнатур методов
            
            if (funcCall.Function is IdentifierExpression ident)
            {
                // Вызов конструктора типа: Integer(5)
                if (_typeMap.ContainsKey(ident.Name))
                {
                    return _typeMap[ident.Name];
                }
            }

            return typeof(object);
        }

        private Type InferMemberAccessType(MemberAccessExpression member)
        {
            // Упрощённый вывод типа для доступа к членам
            // TODO: Полная реализация с учётом типов членов
            
            return typeof(object);
        }
    }
}
