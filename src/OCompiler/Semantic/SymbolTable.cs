using System;
using System.Collections.Generic;
using System.Linq;
using OCompiler.Parser;
namespace OCompiler.Semantic
{
    public class SymbolTable
    {
        private readonly Stack<Dictionary<string, Symbol>> _scopes = new();
        private readonly Dictionary<string, ClassDeclaration> _classes = new();
        private readonly Dictionary<string, MethodDeclaration> _methods = new();

        public SymbolTable()
        {
            EnterScope(); // Глобальная область видимости
        }

        public void EnterScope() => _scopes.Push(new Dictionary<string, Symbol>());
        public void ExitScope() => _scopes.Pop();

        public void AddClass(string name, ClassDeclaration classDecl) => _classes[name] = classDecl;
        public void AddMethod(string name, MethodDeclaration methodDecl) => _methods[name] = methodDecl;
        
        public void AddSymbol(string name, Symbol symbol)
        {
            if (_scopes.Count > 0)
            {
                var currentScope = _scopes.Peek();
                
                if (currentScope.ContainsKey(name))
                {
                    var existing = currentScope[name];
                    
                    // ВРЕМЕННО: НЕ позволяем перезаписывать Array на Unknown
                    if (existing.Type == "Array" && symbol.Type == "Unknown")
                    {
                        return; // Не перезаписываем!
                    }
                }
                
                currentScope[name] = symbol;
            }
        }
        
        public Symbol? Lookup(string name)
        {
            foreach (var scope in _scopes)
            {
                if (scope.TryGetValue(name, out var symbol))
                {
                    return symbol;
                }
            }
            
            return null;
        }

        // Проверка только в текущем scope (для обнаружения дубликатов)
        public bool ExistsInCurrentScope(string name)
        {
            if (_scopes.Count > 0)
            {
                var currentScope = _scopes.Peek();
                return currentScope.ContainsKey(name);
            }
            return false;
        }

        public ClassDeclaration? LookupClass(string name) => _classes.GetValueOrDefault(name);
        public MethodDeclaration? LookupMethod(string name) => _methods.GetValueOrDefault(name);
        
        public bool IsClassExists(string name) => _classes.ContainsKey(name);
        public bool IsMethodExists(string name) => _methods.ContainsKey(name);
        public void UpdateSymbol(string name, Symbol newSymbol)
        {
            if (_scopes.Count > 0)
            {
                var currentScope = _scopes.Peek();
                if (currentScope.ContainsKey(name))
                {
                    currentScope[name] = newSymbol;
                }
            }
        }
    }

    public class Symbol(string name, string type, string? genericParam = null)
    {
        public string Name { get; } = name;
        public string Type { get; } = type;
        public bool IsInitialized { get; set; } = false;
        public bool IsUsed { get; set; } = false;
        public string GenericParameter { get; } = genericParam;
        public int? ArraySize { get; set; } = null;
        public ExpressionNode? Initializer { get; set; }

        public string GetFullTypeName()
        {
            if (!string.IsNullOrEmpty(GenericParameter))
            {
                return $"{Type}[{GenericParameter}]";
            }
            return Type;
        }
        
    }
    

    
}