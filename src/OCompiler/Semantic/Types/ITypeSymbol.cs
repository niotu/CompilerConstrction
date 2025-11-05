using System;

namespace OCompiler.Semantic.Types
{
    public interface ITypeSymbol : IEquatable<ITypeSymbol>
    {
        string Name { get; }

        // True if this type can be assigned to targetType (this -> targetType)
        bool IsAssignableTo(ITypeSymbol targetType);
    }
}


