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
                    Console.WriteLine($"DEBUG: ⚠️ SYMBOL OVERWRITE: '{name}' {existing.Type}[{existing.GenericParameter}] -> {symbol.Type}[{symbol.GenericParameter}]");
                    
                    // ВРЕМЕННО: НЕ позволяем перезаписывать Array на Unknown
                    if (existing.Type == "Array" && symbol.Type == "Unknown")
                    {
                        Console.WriteLine($"DEBUG: 🚫 BLOCKED overwrite of Array with Unknown for '{name}'");
                        return; // Не перезаписываем!
                    }
                }
                
                Console.WriteLine($"DEBUG: SymbolTable.AddSymbol: {name} = {symbol.Type}[{symbol.GenericParameter}]");
                currentScope[name] = symbol;
            }
        }
        
        public Symbol? Lookup(string name)
        {
            Console.WriteLine($"DEBUG: SymbolTable.Lookup('{name}') - scopes count: {_scopes.Count}");
            
            foreach (var scope in _scopes)
            {
                if (scope.TryGetValue(name, out var symbol))
                {
                    Console.WriteLine($"DEBUG: Found '{name}' in scope: {symbol.Type}[{symbol.GenericParameter}]");
                    return symbol;
                }
            }
            
            Console.WriteLine($"DEBUG: Symbol '{name}' not found in any scope");
            return null;
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
                    Console.WriteLine($"DEBUG: Updating symbol '{name}': {currentScope[name].Type} -> {newSymbol.Type}");
                    currentScope[name] = newSymbol;
                }
                else
                {
                    Console.WriteLine($"DEBUG: Cannot update - symbol '{name}' not found in current scope");
                }
            }
        }
    }

    public class Symbol
    {
        public string Name { get; }
        public string Type { get; }
        public bool IsInitialized { get; set; }
        public bool IsUsed { get; set; }
        public string GenericParameter { get; }
        public int? ArraySize { get; set; }
        public ExpressionNode? Initializer { get; set; }

        public Symbol(string name, string type, string genericParam = null)
        {
            Name = name;
            Type = type;
            IsInitialized = false;
            IsUsed = false;
            GenericParameter = genericParam;
            ArraySize = null;
        }
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