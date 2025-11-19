using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using OCompiler.Parser;

namespace OCompiler.CodeGen
{
    public class TypeMapper
    {
        private readonly ModuleBuilder _moduleBuilder;
        private readonly Dictionary<string, Type> _typeMap;

        public TypeMapper(ModuleBuilder moduleBuilder)
        {
            _moduleBuilder = moduleBuilder;
            _typeMap = new Dictionary<string, Type>();
            
            // Регистрация встроенных типов O -> .NET
            RegisterBuiltInTypes();
        }

        private void RegisterBuiltInTypes()
        {
            _typeMap["Integer"] = typeof(int);
            _typeMap["Real"] = typeof(double);
            _typeMap["Boolean"] = typeof(bool);
            _typeMap["AnyValue"] = typeof(object);
            _typeMap["AnyRef"] = typeof(object);
            
            // Array и List требуют generic маппинга
            // Будут обработаны специально
        }

        public void RegisterType(string oTypeName, Type netType)
        {
            _typeMap[oTypeName] = netType;
        }

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

            if (_typeMap.TryGetValue(oTypeName, out var netType))
            {
                return netType;
            }

            throw new InvalidOperationException($"Type '{oTypeName}' not found in type map");
        }

        public Type InferFieldType(ExpressionNode expression)
        {
            return expression switch
            {
                IntegerLiteral => typeof(int),
                RealLiteral => typeof(double),
                BooleanLiteral => typeof(bool),
                ConstructorInvocation ctor => GetNetType(ctor.ClassName),
                _ => typeof(object)
            };
        }
    }
}
