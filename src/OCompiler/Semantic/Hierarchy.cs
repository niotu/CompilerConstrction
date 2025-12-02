using OCompiler.Parser;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OCompiler.Semantic;

public class ClassHierarchy
{
    private readonly Dictionary<string, ClassDeclaration> _classes = new();
    private readonly HashSet<string> _standardClasses = new() 
    { 
        "Integer", "Real", "Boolean", "Array", "List", "AnyValue", "AnyRef" 
    };
    private readonly Dictionary<string, BuiltInClassInfo> _builtInClasses = new();

    public ClassHierarchy()
    {
        InitializeBuiltInClasses();
    }

    public void AddClass(ClassDeclaration classDecl) => _classes[classDecl.Name] = classDecl;
    
    public bool ClassExists(string? className) => 
        !string.IsNullOrEmpty(className) && (_classes.ContainsKey(className) || _standardClasses.Contains(className));
    
    public ClassDeclaration? GetClass(string className) => _classes.GetValueOrDefault(className);
    
    public ClassDeclaration? GetBaseClass(ClassDeclaration classDecl)
    {
        return string.IsNullOrEmpty(classDecl.Extension) ? 
            null : _classes.GetValueOrDefault(classDecl.Extension);
    }
    
    public bool IsStandardLibraryClass(string? className) => 
        !string.IsNullOrEmpty(className) && _standardClasses.Contains(className);
    
    public bool IsFinalClass(string? className) => 
        className == "Integer" || className == "Real" || className == "Boolean";
    
    public bool IsAssignable(string fromType, string toType)
    {
        if (fromType == toType) return true;
        
        // Правила присваивания для стандартных типов
        if (toType == "Real" && fromType == "Integer") return true;
        if (toType == "AnyValue" && (fromType == "Integer" || fromType == "Real" || fromType == "Boolean")) 
            return true;
        if (toType == "AnyRef" && (fromType == "Array" || fromType == "List")) 
            return true;
            
        // Проверка наследования
        return IsSubclassOf(fromType, toType);
    }
    
    public bool IsSubclassOf(string derived, string baseClass)
    {
        var current = _classes.GetValueOrDefault(derived);
        while (current != null)
        {
            if (current.Name == baseClass) return true;
            current = GetBaseClass(current);
        }
        return false;
    }

    public MethodDeclaration? FindMethodInHierarchy(string methodName, string className)
    {
        var current = _classes.GetValueOrDefault(className);
        while (current != null)
        {
            var method = current.Members
                .OfType<MethodDeclaration>()
                .FirstOrDefault(m => m.Header.Name == methodName);
            
            if (method != null)
                return method;
            
            // Ищем в базовом классе
            current = string.IsNullOrEmpty(current.Extension) ? null : GetBaseClass(current);
        }
        return null;
    }

    public VariableDeclaration? FindFieldInHierarchy(string fieldName, string className)
    {
        var current = _classes.GetValueOrDefault(className);
        while (current != null)
        {
            var field = current.Members
                .OfType<VariableDeclaration>()
                .FirstOrDefault(f => f.Identifier == fieldName);
            
            if (field != null)
                return field;
            
            current = string.IsNullOrEmpty(current.Extension) ? null : GetBaseClass(current);
        }
        return null;
    }

    public bool HasCyclicDependency(ClassDeclaration classDecl, out string? cycle)
    {
        cycle = null;
        var visited = new HashSet<string>();
        var recursionStack = new HashSet<string>();
        
        return HasCyclicDependencyHelper(classDecl.Name, visited, recursionStack, out cycle);
    }

    private bool HasCyclicDependencyHelper(string className, HashSet<string> visited, 
                                          HashSet<string> recursionStack, out string? cycle)
    {
        cycle = null;
        
        if (recursionStack.Contains(className))
        {
            cycle = className;
            return true;
        }
        
        if (visited.Contains(className))
            return false;
        
        visited.Add(className);
        recursionStack.Add(className);
        
        var classDecl = _classes.GetValueOrDefault(className);
        if (classDecl != null && !string.IsNullOrEmpty(classDecl.Extension))
        {
            if (HasCyclicDependencyHelper(classDecl.Extension, visited, recursionStack, out cycle))
            {
                return true;
            }
        }
        
        recursionStack.Remove(className);
        return false;
    }

    public IEnumerable<string> GetAllClasses()
    {
        return _classes.Keys.Concat(_standardClasses);
    }


    
    public bool IsBuiltInClass(string? className)
    {
        if (string.IsNullOrEmpty(className)) return false;
        var key = NormalizeClassName(className);
        return _builtInClasses.ContainsKey(key);
    }
    
    public BuiltInClassInfo? GetBuiltInClass(string className)
    {
        var key = NormalizeClassName(className);
        return _builtInClasses.GetValueOrDefault(key);
    }
    
    public List<BuiltInMethodInfo> GetBuiltInMethods(string className, string methodName)
    {
        var key = NormalizeClassName(className);
        if (_builtInClasses.TryGetValue(key, out var classInfo))
        {
            var list = new List<BuiltInMethodInfo>();

            if (classInfo.Methods.TryGetValue(methodName, out var exact))
            {
                list.Add(exact);
            }
            list.AddRange(classInfo.Methods
                .Where(kv => kv.Key.StartsWith($"{methodName}("))
                .Select(kv => kv.Value));

            return list;
        }
        return new List<BuiltInMethodInfo>();
    }

    public bool HasBuiltInMethod(string className, string methodName)
    {
        var key = NormalizeClassName(className);
        if (!_builtInClasses.TryGetValue(key, out var classInfo)) return false;
        if (classInfo.Methods.ContainsKey(methodName)) return true; // без параметров
        return classInfo.Methods.Any(kv => kv.Key.StartsWith($"{methodName}("));
    }

    private static string NormalizeClassName(string className)
    {
        if (string.IsNullOrEmpty(className)) return className;
        // Strip generic instantiation: Array[T] -> Array, List[T] -> List
        if (className.StartsWith("Array[")) return "Array";
        if (className.StartsWith("List[")) return "List";
        return className;
    }
    
    public bool IsValidBuiltInConstructor(string className, int argumentCount)
    {
        if (_builtInClasses.TryGetValue(className, out var classInfo))
        {
            return classInfo.Constructors.Any(constr => constr.Parameters.Count == argumentCount);
        }
        return false;
    }
    
    public List<BuiltInConstructorInfo> GetBuiltInConstructors(string className)
    {
        return _builtInClasses.TryGetValue(className, out var classInfo) ? 
            classInfo.Constructors : new List<BuiltInConstructorInfo>();
    }

    private void InitializeBuiltInClasses()
    {
        InitializeIntegerClass();
        InitializeRealClass();
        InitializeBooleanClass();
        InitializeArrayClass();
        InitializeListClass();
    }

    private void InitializeIntegerClass()
    {
        var integer = new BuiltInClassInfo { Name = "Integer", BaseClass = "AnyValue" };
        
        // Конструкторы Integer
        integer.Constructors.AddRange(new[]
        {
            new BuiltInConstructorInfo(), // Конструктор без аргументов (по умолчанию 0)
            new BuiltInConstructorInfo 
            { 
                Parameters = { new BuiltInParameterInfo { Name = "value", Type = "Integer" } } 
            },
            new BuiltInConstructorInfo 
            { 
                Parameters = { new BuiltInParameterInfo { Name = "value", Type = "Real" } } 
            }
        });

        // Методы Integer
        integer.Methods.Add("toReal", new BuiltInMethodInfo { ReturnType = "Real" });
        integer.Methods.Add("toBoolean", new BuiltInMethodInfo { ReturnType = "Boolean" });
        integer.Methods.Add("UnaryMinus", new BuiltInMethodInfo { ReturnType = "Integer" });
        integer.Methods.Add("Print", new BuiltInMethodInfo { ReturnType = "void" });
        
        // Арифметические методы
        AddBinaryMethod(integer, "Plus", "Integer", "Integer");
        AddBinaryMethod(integer, "Plus", "Real", "Real");
        AddBinaryMethod(integer, "Minus", "Integer", "Integer");
        AddBinaryMethod(integer, "Minus", "Real", "Real");
        AddBinaryMethod(integer, "Mult", "Integer", "Integer");
        AddBinaryMethod(integer, "Mult", "Real", "Real");
        AddBinaryMethod(integer, "Div", "Integer", "Integer");
        AddBinaryMethod(integer, "Div", "Real", "Real");
        AddBinaryMethod(integer, "Rem", "Integer", "Integer");
        
        // Методы сравнения
        AddBinaryMethod(integer, "Less", "Integer", "Boolean");
        AddBinaryMethod(integer, "Less", "Real", "Boolean");
        AddBinaryMethod(integer, "LessEqual", "Integer", "Boolean");
        AddBinaryMethod(integer, "LessEqual", "Real", "Boolean");
        AddBinaryMethod(integer, "Greater", "Integer", "Boolean");
        AddBinaryMethod(integer, "Greater", "Real", "Boolean");
        AddBinaryMethod(integer, "GreaterEqual", "Integer", "Boolean");
        AddBinaryMethod(integer, "GreaterEqual", "Real", "Boolean");
        AddBinaryMethod(integer, "Equal", "Integer", "Boolean");
        AddBinaryMethod(integer, "Equal", "Real", "Boolean");

        _builtInClasses["Integer"] = integer;
    }

    private void InitializeRealClass()
    {
        var real = new BuiltInClassInfo { Name = "Real", BaseClass = "AnyValue" };
        
        // Конструкторы Real
        real.Constructors.AddRange(new[]
        {
            new BuiltInConstructorInfo(), // Конструктор без аргументов (по умолчанию 0.0)
            new BuiltInConstructorInfo 
            { 
                Parameters = { new BuiltInParameterInfo { Name = "value", Type = "Real" } } 
            },
            new BuiltInConstructorInfo 
            { 
                Parameters = { new BuiltInParameterInfo { Name = "value", Type = "Integer" } } 
            }
        });

        // Методы Real
        real.Methods.Add("toInteger", new BuiltInMethodInfo { ReturnType = "Integer" });
        real.Methods.Add("UnaryMinus", new BuiltInMethodInfo { ReturnType = "Real" });
        real.Methods.Add("Print", new BuiltInMethodInfo { ReturnType = "void" });
        
        // Арифметические методы
        AddBinaryMethod(real, "Plus", "Real", "Real");
        AddBinaryMethod(real, "Plus", "Integer", "Real");
        AddBinaryMethod(real, "Minus", "Real", "Real");
        AddBinaryMethod(real, "Minus", "Integer", "Real");
        AddBinaryMethod(real, "Mult", "Real", "Real");
        AddBinaryMethod(real, "Mult", "Integer", "Real");
        AddBinaryMethod(real, "Div", "Integer", "Real");
        AddBinaryMethod(real, "Div", "Real", "Real");
        AddBinaryMethod(real, "Rem", "Integer", "Real");
        
        // Методы сравнения
        AddBinaryMethod(real, "Less", "Real", "Boolean");
        AddBinaryMethod(real, "Less", "Integer", "Boolean");
        AddBinaryMethod(real, "LessEqual", "Real", "Boolean");
        AddBinaryMethod(real, "LessEqual", "Integer", "Boolean");
        AddBinaryMethod(real, "Greater", "Real", "Boolean");
        AddBinaryMethod(real, "Greater", "Integer", "Boolean");
        AddBinaryMethod(real, "GreaterEqual", "Real", "Boolean");
        AddBinaryMethod(real, "GreaterEqual", "Integer", "Boolean");
        AddBinaryMethod(real, "Equal", "Real", "Boolean");
        AddBinaryMethod(real, "Equal", "Integer", "Boolean");

        _builtInClasses["Real"] = real;
    }

    private void InitializeBooleanClass()
    {
        var boolean = new BuiltInClassInfo { Name = "Boolean", BaseClass = "AnyValue" };
        
        // Конструкторы Boolean
        boolean.Constructors.AddRange(new[]
        {
            new BuiltInConstructorInfo(), // Конструктор без аргументов (по умолчанию false)
            new BuiltInConstructorInfo 
            { 
                Parameters = { new BuiltInParameterInfo { Name = "value", Type = "Boolean" } } 
            }
        });

        // Методы Boolean
        boolean.Methods.Add("toInteger", new BuiltInMethodInfo { ReturnType = "Integer" });
        boolean.Methods.Add("Print", new BuiltInMethodInfo { ReturnType = "void" });
        AddBinaryMethod(boolean, "Or", "Boolean", "Boolean");
        AddBinaryMethod(boolean, "And", "Boolean", "Boolean");
        AddBinaryMethod(boolean, "Xor", "Boolean", "Boolean");
        boolean.Methods.Add("Not", new BuiltInMethodInfo { ReturnType = "Boolean" });

        _builtInClasses["Boolean"] = boolean;
    }

    private void InitializeArrayClass()
    {
        var array = new BuiltInClassInfo { Name = "Array", BaseClass = "AnyRef" };
        
        // Конструктор Array
        array.Constructors.Add(new BuiltInConstructorInfo 
        { 
            Parameters = { new BuiltInParameterInfo { Name = "size", Type = "Integer" } } 
        });

        // Методы Array
        array.Methods.Add("toList", new BuiltInMethodInfo { ReturnType = "List" });
        array.Methods.Add("Length", new BuiltInMethodInfo { ReturnType = "Integer" });
        
        // get и set методы (используют generic параметр T)
        array.Methods.Add("get", new BuiltInMethodInfo 
        { 
            ReturnType = "T",
            Parameters = { new BuiltInParameterInfo { Name = "index", Type = "Integer" } }
        });
        
        array.Methods.Add("set", new BuiltInMethodInfo 
        { 
            ReturnType = "void",
            Parameters = 
            { 
                new BuiltInParameterInfo { Name = "index", Type = "Integer" },
                new BuiltInParameterInfo { Name = "value", Type = "T" }
            }
        });

        _builtInClasses["Array"] = array;
    }

    private void InitializeListClass()
    {
        var list = new BuiltInClassInfo { Name = "List", BaseClass = "AnyRef" };
        
        // Конструкторы List
        list.Constructors.AddRange(new[]
        {
            new BuiltInConstructorInfo(),
            new BuiltInConstructorInfo 
            { 
                Parameters = { new BuiltInParameterInfo { Name = "element", Type = "T" } } 
            },
            new BuiltInConstructorInfo 
            { 
                Parameters = 
                { 
                    new BuiltInParameterInfo { Name = "element", Type = "T" },
                    new BuiltInParameterInfo { Name = "count", Type = "Integer" }
                } 
            }
        });

        // Методы List
        list.Methods.Add("append", new BuiltInMethodInfo 
        { 
            ReturnType = "List",
            Parameters = { new BuiltInParameterInfo { Name = "element", Type = "T" } }
        });
        
        list.Methods.Add("head", new BuiltInMethodInfo { ReturnType = "T" });
        list.Methods.Add("tail", new BuiltInMethodInfo { ReturnType = "List" });

        _builtInClasses["List"] = list;
    }

    private void AddBinaryMethod(BuiltInClassInfo classInfo, string methodName, 
                           string paramType, string returnType, string paramName = "other")
    {
        string uniqueKey = $"{methodName}({paramType})";
        
        classInfo.Methods.Add(uniqueKey, new BuiltInMethodInfo 
        { 
            ReturnType = returnType,
            Parameters = { new BuiltInParameterInfo { Name = paramName, Type = paramType } }
        });
    }
}

public class BuiltInClassInfo
{
    public string Name { get; set; } = "";
    public string BaseClass { get; set; } = "";
    public Dictionary<string, BuiltInMethodInfo> Methods { get; set; } = new();
    public Dictionary<string, string> Fields { get; set; } = new();
    public List<BuiltInConstructorInfo> Constructors { get; set; } = new();
}

public class BuiltInMethodInfo
{
    public string ReturnType { get; set; } = "";
    public List<BuiltInParameterInfo> Parameters { get; set; } = new();
}

public class BuiltInConstructorInfo
{
    public List<BuiltInParameterInfo> Parameters { get; set; } = new();
}

public class BuiltInParameterInfo
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
}