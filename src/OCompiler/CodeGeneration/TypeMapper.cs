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

        private void RegisterBuiltInTypes()
        {
            _typeMap["Integer"] = typeof(int);
            _typeMap["Real"] = typeof(double);
            _typeMap["Boolean"] = typeof(bool);
            _typeMap["Class"] = typeof(object);
            _typeMap["AnyValue"] = typeof(object);
            _typeMap["AnyRef"] = typeof(object);
        }

        public void RegisterUserType(string oTypeName, Type netType)
        {
            _typeMap[oTypeName] = netType;
        }

        public bool IsBuiltInType(string typeName)
        {
            return typeName is "Integer" or "Real" or "Boolean" or 
                   "Class" or "AnyValue" or "AnyRef" or "Array" or "List";
        }

        public Type GetNetType(string oTypeName)
        {
            if (oTypeName.StartsWith("Array[") && oTypeName.EndsWith("]"))
            {
                var elementTypeName = oTypeName.Substring(6, oTypeName.Length - 7);
                var elementType = GetNetType(elementTypeName);
                return elementType.MakeArrayType();
            }

            if (oTypeName.StartsWith("List[") && oTypeName.EndsWith("]"))
            {
                var elementTypeName = oTypeName.Substring(5, oTypeName.Length - 6);
                var elementType = GetNetType(elementTypeName);
                return typeof(System.Collections.Generic.List<>).MakeGenericType(elementType);
            }

            if (_typeMap.TryGetValue(oTypeName, out var netType))
            {
                return netType;
            }

            throw new InvalidOperationException($"Type '{oTypeName}' not found in type map");
        }

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
                _ => typeof(object)
            };
        }

        private Type GetConstructorType(ConstructorInvocation ctor)
        {
            if (ctor.ClassName == "Integer")
                return typeof(int);
            
            if (ctor.ClassName == "Real")
                return typeof(double);
            
            if (ctor.ClassName == "Boolean")
                return typeof(bool);
            
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

            try
            {
                return GetNetType(ctor.ClassName);
            }
            catch
            {
                Console.WriteLine($"**[ WARN ] Could not resolve type for constructor '{ctor.ClassName}', using Object");
                return typeof(object);
            }
        }

        private Type GetIdentifierType(IdentifierExpression ident)
        {
            if (_typeMap.ContainsKey(ident.Name))
            {
                return _typeMap[ident.Name];
            }

            return typeof(object);
        }

        private Type InferFunctionCallType(FunctionalCall funcCall)
        {
            if (funcCall.Function is IdentifierExpression ident)
            {
                if (_typeMap.ContainsKey(ident.Name))
                {
                    return _typeMap[ident.Name];
                }
            }
            // Если функция — обращение к члену (например calc.Add(...))
            if (funcCall.Function is MemberAccessExpression member)
            {
                // Попробуем определить тип целевого объекта, если это простая ссылка на тип
                if (member.Target is IdentifierExpression targetIdent)
                {
                    // Если идентификатор — имя типа (зарегистрировано в _typeMap)
                    if (_typeMap.TryGetValue(targetIdent.Name, out var netType))
                    {
                        // Найдём сигнатуру метода в иерархии классов и вернём его возвращаемый тип
                        var methodName = (member.Member as IdentifierExpression)?.Name;
                        if (!string.IsNullOrEmpty(methodName))
                        {
                            try
                            {
                                var returnType = GetMethodReturnType(targetIdent.Name, methodName);
                                if (returnType != null)
                                    return returnType;
                            }
                            catch
                            {
                                // Игнорируем и падаем к fallback
                            }
                        }
                    }
                }
            }

            return typeof(object);
        }

        /// <summary>
        /// Возвращает имя O-типа по .NET типу, если он зарегистрирован.
        /// </summary>
        public string? GetOTypeName(Type netType)
        {
            foreach (var kv in _typeMap)
            {
                if (kv.Value == netType) return kv.Key;
            }
            return null;
        }

        /// <summary>
        /// Находит возвращаемый .NET-тип метода по имени класса O и имени метода.
        /// Возвращает null, если метод не найден или тип не может быть сопоставлен.
        /// </summary>
        public Type? GetMethodReturnType(string oClassName, string methodName)
        {
                        // Built-in types
            if (oClassName == "Integer")
            {
                if (methodName == "Print") return typeof(void);
                if (methodName == "toReal") return typeof(double);
            }

            if (oClassName == "Real")
            {
                if (methodName == "Print") return typeof(void);
                if (methodName == "toInteger") return typeof(int);
            }

            if (oClassName == "Boolean")
            {
                if (methodName == "Print") return typeof(void);
            }
            var methodDecl = _hierarchy.FindMethodInHierarchy(methodName, oClassName);
            if (methodDecl == null) return null;

            var returnTypeName = methodDecl.Header.ReturnType;
            if (string.IsNullOrEmpty(returnTypeName)) return typeof(void);

            try
            {
                return GetNetType(returnTypeName);
            }
            catch
            {
                return null;
            }
        }

        private Type InferMemberAccessType(MemberAccessExpression member)
        {
            return typeof(object);
        }
    }
}
