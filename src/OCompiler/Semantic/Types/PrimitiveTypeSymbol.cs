using System;

namespace OCompiler.Semantic.Types
{
    public sealed class PrimitiveTypeSymbol : ITypeSymbol
    {
        public string Name { get; }

        private PrimitiveTypeSymbol(string name)
        {
            Name = name;
        }

        public static readonly PrimitiveTypeSymbol Integer = new("Integer");
        public static readonly PrimitiveTypeSymbol Real = new("Real");
        public static readonly PrimitiveTypeSymbol Boolean = new("Boolean");
        public static readonly PrimitiveTypeSymbol AnyValue = new("AnyValue");

        public bool IsAssignableTo(ITypeSymbol targetType)
        {
            if (Equals(targetType)) return true;

            // Integer -> Real widening
            if (ReferenceEquals(this, Integer) && ReferenceEquals(targetType, Real)) return true;

            // Primitive to AnyValue
            if (ReferenceEquals(targetType, AnyValue)) return ReferenceEquals(this, Integer) || ReferenceEquals(this, Real) || ReferenceEquals(this, Boolean);

            return false;
        }

        public bool Equals(ITypeSymbol? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return other is PrimitiveTypeSymbol p && p.Name == Name;
        }

        public override bool Equals(object? obj) => Equals(obj as ITypeSymbol);
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
        public override string ToString() => Name;
    }
}


