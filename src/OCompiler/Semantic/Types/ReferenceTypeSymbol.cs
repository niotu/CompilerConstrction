using System;

namespace OCompiler.Semantic.Types
{
    public sealed class ReferenceTypeSymbol : ITypeSymbol
    {
        public string Name { get; }
        public ReferenceTypeSymbol? BaseType { get; }

        private readonly bool _isAnyRef;

        private ReferenceTypeSymbol(string name, ReferenceTypeSymbol? baseType, bool isAnyRef)
        {
            Name = name;
            BaseType = baseType;
            _isAnyRef = isAnyRef;
        }

        public static ReferenceTypeSymbol AnyRef { get; } = new("AnyRef", null, true);

        public static ReferenceTypeSymbol Create(string name, ReferenceTypeSymbol? baseType)
        {
            if (name == "AnyRef") return AnyRef;
            return new ReferenceTypeSymbol(name, baseType, false);
        }

        public bool IsAssignableTo(ITypeSymbol targetType)
        {
            if (Equals(targetType)) return true;

            if (targetType is ReferenceTypeSymbol refTarget)
            {
                if (refTarget._isAnyRef) return true; // AnyRef supertype for references

                // Walk up the base chain
                var current = this;
                while (current is not null)
                {
                    if (current.Equals(refTarget)) return true;
                    current = current.BaseType;
                }
            }

            return false;
        }

        public bool Equals(ITypeSymbol? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return other is ReferenceTypeSymbol r && r.Name == Name;
        }

        public override bool Equals(object? obj) => Equals(obj as ITypeSymbol);
        public override int GetHashCode() => Name.GetHashCode(StringComparison.Ordinal);
        public override string ToString() => Name;
    }
}


