using System;

namespace OCompiler.Semantic.Types
{
    public sealed class ArrayTypeSymbol : ITypeSymbol
    {
        public ITypeSymbol ElementType { get; }

        private ArrayTypeSymbol(ITypeSymbol elementType)
        {
            ElementType = elementType;
        }

        public static ArrayTypeSymbol Create(ITypeSymbol elementType) => new(elementType);

        public string Name => $"Array[{ElementType}]";

        public bool IsAssignableTo(ITypeSymbol targetType)
        {
            if (Equals(targetType)) return true;

            // Arrays are reference-like; AnyRef accepts them
            if (targetType is ReferenceTypeSymbol refTarget && ReferenceTypeSymbol.AnyRef.Equals(refTarget))
            {
                return true;
            }

            // Structural compatibility: allow assignment if element types are assignable (covariant by design here)
            if (targetType is ArrayTypeSymbol arrayTarget)
            {
                return ElementType.IsAssignableTo(arrayTarget.ElementType);
            }

            return false;
        }

        public bool Equals(ITypeSymbol? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return other is ArrayTypeSymbol a && ElementType.Equals(a.ElementType);
        }

        public override bool Equals(object? obj) => Equals(obj as ITypeSymbol);
        public override int GetHashCode() => HashCode.Combine("Array", ElementType);
        public override string ToString() => Name;
    }
}


