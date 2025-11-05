using System;

namespace OCompiler.Semantic.Types
{
    public sealed class ListTypeSymbol : ITypeSymbol
    {
        public ITypeSymbol ElementType { get; }

        private ListTypeSymbol(ITypeSymbol elementType)
        {
            ElementType = elementType;
        }

        public static ListTypeSymbol Create(ITypeSymbol elementType) => new(elementType);

        public string Name => $"List[{ElementType}]";

        public bool IsAssignableTo(ITypeSymbol targetType)
        {
            if (Equals(targetType)) return true;

            if (targetType is ReferenceTypeSymbol refTarget && ReferenceTypeSymbol.AnyRef.Equals(refTarget))
            {
                return true;
            }

            if (targetType is ListTypeSymbol listTarget)
            {
                return ElementType.IsAssignableTo(listTarget.ElementType);
            }

            return false;
        }

        public bool Equals(ITypeSymbol? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return other is ListTypeSymbol l && ElementType.Equals(l.ElementType);
        }

        public override bool Equals(object? obj) => Equals(obj as ITypeSymbol);
        public override int GetHashCode() => HashCode.Combine("List", ElementType);
        public override string ToString() => Name;
    }
}


