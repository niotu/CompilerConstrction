using System;
using System.Collections.Concurrent;

namespace OCompiler.Semantic.Types
{
    public static class TypeFactory
    {
        private static readonly ConcurrentDictionary<string, ReferenceTypeSymbol> ReferenceCache = new();

        public static PrimitiveTypeSymbol Integer => PrimitiveTypeSymbol.Integer;
        public static PrimitiveTypeSymbol Real => PrimitiveTypeSymbol.Real;
        public static PrimitiveTypeSymbol Boolean => PrimitiveTypeSymbol.Boolean;
        public static PrimitiveTypeSymbol AnyValue => PrimitiveTypeSymbol.AnyValue;

        public static ReferenceTypeSymbol AnyRef => ReferenceTypeSymbol.AnyRef;

        public static ReferenceTypeSymbol Class(string name, ReferenceTypeSymbol? baseType = null)
        {
            if (name == "AnyRef") return AnyRef;
            return ReferenceCache.GetOrAdd(name, _ => ReferenceTypeSymbol.Create(name, baseType));
        }

        public static ArrayTypeSymbol ArrayOf(ITypeSymbol elementType) => ArrayTypeSymbol.Create(elementType);
        public static ListTypeSymbol ListOf(ITypeSymbol elementType) => ListTypeSymbol.Create(elementType);

        public static bool IsAssignable(ITypeSymbol source, ITypeSymbol target) => source.IsAssignableTo(target);
    }
}


