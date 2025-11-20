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

            return typeof(object);
        }

        private Type InferMemberAccessType(MemberAccessExpression member)
        {
            return typeof(object);
        }
    }
}
