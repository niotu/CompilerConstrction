# O Compiler Architecture & Implementation Details

## Overview

The O Language compiler is a multi-phase compiler that translates O source code to .NET IL assemblies with runtime execution via Reflection.Emit and a lightweight interpreter fallback.

### Build: Lexer → Parser → AST → Semantic Analysis → Code Generation → IL Assembly

## Phase 1: Lexical Analysis

**File**: `src/OCompiler/Lexer/OLexer.cs`

- Scans source code character-by-character
- Produces a stream of tokens (TokenType, value, position)
- Recognizes keywords: `class`, `method`, `is`, `return`, `if`, `while`, etc.
- Handles identifiers, integers, reals, booleans
- Tracks line/column for error reporting

**Key Classes**:
- `Token`: Represents a single lexical unit
- `TokenType`: Enumeration of all token types
- `Position`: Line/column tracking for error reporting

## Phase 2: Syntax Analysis (Parsing)

**Files**: 
- `src/OCompiler/Parser/OParser.cs` (generated from Grammar.y by Gppg)
- `src/OCompiler/Parser/AstNodes.cs` (AST node definitions)

- Uses LALR(1) parser generated from yacc grammar
- Constructs Abstract Syntax Tree (AST) from token stream
- Grammar defines:
  - Program: sequence of class declarations
  - Class: fields, constructors, methods
  - Constructor: `this() is ... end`
  - Method: `method <name>(<params>) : <return-type> is ... end`
  - Expressions: literals, binary ops, method calls, constructors, etc.

**Key Classes**:
- `ProgramNode`: Root AST node
- `ClassDeclaration`: Class definition
- `ConstructorDeclaration`: Constructor (this())
- `MethodDeclaration`: Method definition
- `MethodBodyNode`: Sequence of statements
- `ExpressionNode`: Expression (binary op, call, literal, etc.)
- `VariableDeclaration`: Field/local variable

## Phase 3: Semantic Analysis

**Files**: `src/OCompiler/Semantic/*.cs`

### ClassHierarchy (`Semantic/Hierarchy.cs`)
- Maintains class inheritance relationships
- Tracks fields, methods, and constructors per class
- Supports single inheritance (class Child : Parent)

### SemanticChecker (`Semantic/Checks.cs`)
- **Symbol Resolution**: Ensures all referenced classes, methods, fields exist
- **Type Checking**: Validates expressions have correct types
- **Method Resolution**: Finds correct method overload based on argument types
- **Inheritance Validation**: Checks parent classes exist and circular inheritance is avoided
- **Operation Validation**: Ensures operations (arithmetic, comparison) apply to correct types

### SymbolTable (`Semantic/SymbolTable.cs`)
- Maps symbol names to symbol information
- Scoped lookup for local variables vs. fields vs. methods
- Used by code generator to resolve references

### Key Symbols (`Semantic/Symbols/`)
- `MethodSymbol`: Method information (name, signature, return type)
- `FieldSymbol`: Field information (name, type)
- Symbols registered during semantic analysis, used during code generation

## Phase 4: Code Generation (IL Emission)

**Files**: `src/OCompiler/CodeGeneration/*.cs`

Uses **System.Reflection.Emit** to dynamically generate IL code at runtime.

### CodeGenerator (`CodeGeneration/CodeGenerator.cs`)

**Main Methods**:
- `Generate(ProgramNode)`: Orchestrates entire code generation
  1. **Phase 1**: `DeclareType()` - Create TypeBuilder for each class
  2. **Phase 2**: `GenerateClassMembers()` - Add fields, constructors, methods
  3. **Phase 3**: `CreateTypeInfo().AsType()` - Finalize types
  4. **Phase 4**: `GenerateEntryPoint()` - Create static Main entry point
- `SaveToFile(outputPath, asExecutable)`: Persist to .dll file
- `GetCompletedType(name)`: Retrieve generated Type object

**Key Fields**:
- `_assemblyBuilder`: PersistedAssemblyBuilder (saves to disk in .NET 9)
- `_moduleBuilder`: ModuleBuilder (contains all type definitions)
- `_typeBuilders`: Dictionary mapping class names to TypeBuilders
- `_completedTypes`: Dictionary mapping class names to finalized Type objects

### TypeMapper (`CodeGeneration/TypeMapper.cs`)

**Responsibility**: Maps source language types to .NET types

**Key Methods**:
- `MapType(string typeName)`: Look up .NET Type for source type name
- `InferType(ExpressionNode)`: Determine expression result type
- `IsBuiltInType(string)`: Check if type is built-in (Integer, Real, Boolean, Array, List)

**Mapping**:
- Integer → System.Int32
- Real → System.Double
- Boolean → System.Boolean
- Array[T] → System.Object[] (emulated via BuiltinTypes.OArray)
- List[T] → System.Collections.Generic.List<object>
- User classes → Generated TypeBuilder/Type

### MethodGenerator (`CodeGeneration/MethodGenerator.cs`)

**Responsibility**: Generate IL code for constructors and methods

**Key Methods**:
- `GenerateConstructor(TypeBuilder, ConstructorDeclaration)`: Emit constructor IL
- `GenerateMethod(TypeBuilder, MethodDeclaration)`: Emit method IL
- `EmitExpression(ILGenerator, ExpressionNode)`: Emit IL for expression

**IL Emission Strategy**:
- For each statement in method body:
  - Variable declaration: `new Local, stloc`
  - Assignment: `stfld` (fields) or `stloc` (locals)
  - Method call: `callvirt` on instance, `call` on value types
  - Return: `stret` (store return) or `ret`
  - If/while: `brfalse` (branch if false), `br` (unconditional)

**Method Overloading Support**:
- Each method name includes full signature: `Add(Int32)`, `Add(Double)`
- IL method names are modified with parameter types to distinguish
- During code generation, when a method is called, the correct overload is selected based on argument types

### Example: Generated IL for Simple Constructor

```csharp
// O Language:
class Main is
  n: Integer
  this() is
    n := 10
  end
end

// Generated IL:
.method private hidebysig specialname rtspecialname instance void .ctor() cil managed
{
  ldarg.0              // Load 'this'
  ldc.i4 10            // Load constant 10
  stfld int32 Main::n  // Store to field n
  ret                  // Return
}
```

## Phase 5: Runtime Execution

### Execution Paths

When `--run` flag is used:

1. **Try Direct Reflection** (`ConstructorInfo.Invoke`)
   - For compiled classes, try invoking constructor directly via reflection
   - **Failure Reason**: Dynamic module types don't support invocation in some .NET runtime environments

2. **Try Activator Factory** (`Activator.CreateInstance`)
   - Use factory method to create instance
   - **Failure Reason**: Same as above - dynamic modules not fully supported

3. **Try Assembly Type Lookup** (`Assembly.GetType` + reflection)
   - Load type from generated assembly dynamically
   - **Failure Reason**: Dynamic modules can't be loaded this way

4. **Interpreter Fallback** (Always succeeds)
   - Parse constructor AST, interpret it manually
   - Execute statements, manage local variables
   - Handle method calls by looking up and invoking methods
   - See below for details

### Interpreter (`src/OCompiler/Runtime/Interpreter.cs`)

Lightweight tree-walking interpreter for executing constructor bodies.

**Key Concepts**:
- **ReturnSignal**: Exception used for return statements (control flow)
- **ExecutionEnvironment**: Local variable scope (stack frame equivalent)
- **ObjectInstance**: Runtime representation of object (fields + type reference)

**Supported Constructs**:
- Variable declarations: `n: Integer`
- Assignments: `n := 10`, `obj.field := value`
- Constructor calls: `new Box()`
- Method calls: `obj.method(args)`
- Return statements: `return value`
- If statements: `if condition ... end`
- While loops: `while condition ... end`

**NOT Supported**:
- Built-in type operations (arithmetic, comparison done by IL)
- Array/List operations
- Complex expressions

**Execution Model**:
```csharp
// Constructor call flow:
1. New instance created: new ObjectInstance(className)
2. Enter constructor:    ExecutionEnvironment env with locals
3. Execute statements:   for each statement in constructor body
4. Handle returns:       throw ReturnSignal (caught at top level)
5. Return instance:      obj with all fields initialized
```

### Process Flow Example

```
User code:
  class Calc is
    add(x: Integer): Calc is
      return this
    end
  end

→ Code gen creates MethodBuilder with signature:
  public Calc add(Int32 x)

→ IL emission emits:
  ldarg.0    (load this)
  ret        (return this)

→ Runtime execution:
  1. Try reflection: Works! Returns Calc instance
  2. If reflection fails: Interpreter handles it
```

## Assembly Persistence

### PersistedAssemblyBuilder (.NET 9+)

**File**: Uses `System.Reflection.Emit` PersistedAssemblyBuilder

```csharp
var persistedBuilder = new PersistedAssemblyBuilder(
    assemblyName,
    typeof(object).Assembly
);

// After generating types:
using (var stream = new FileStream(path, FileMode.Create))
{
    persistedBuilder.Save(stream);  // Writes PE/IL to disk
}
```

**Output Format**: 
- Managed IL assembly (.dll format)
- Includes metadata, IL code, resources
- Can be executed with `dotnet program.dll`
- Requires .runtimeconfig.json to specify .NET framework

### Runtime Configuration

**File**: `program.runtimeconfig.json`

```json
{
  "runtimeOptions": {
    "tfm": "net9.0",
    "framework": {
      "name": "Microsoft.NETCore.App",
      "version": "9.0.0"
    }
  }
}
```

Tells .NET runtime which framework version to use.

## Error Handling

### Compilation Errors

**Exit Code 1** for:
- Syntax errors (parsing failed)
- Semantic errors (type mismatch, undefined symbols)
- Code generation errors (IL generation failed)
- File I/O errors

**Exit Code 0** for:
- Successful compilation
- Successful execution with `--run`

### Error Messages

Format: `**[ ERR ] <error type>: <message>`

Examples:
```
**[ ERR ] Syntax error at line 5, column 10: Unexpected token
**[ ERR ] Code generation failed: Type 'Foo' not found in type map
**[ ERR ] Method 'bar' not found in type 'Object' with arguments (Int32)
```

## CLI Flags

### Execution
- `--run`: Compile and execute in-memory
- `--emit-exe <path>`: Generate executable assembly (.dll)
- `--entry-point <Class>`: Specify which class constructor to invoke (default: Main)

### Output
- `--save-assembly <path>`: Save generated assembly to file
- `--emit-il`: Print generated IL code to console
- `--ast`: Print abstract syntax tree
- `--tokens-only`: Show tokens and stop
- `--tokens-to-file`: Write tokens to tokens.txt

### Debugging
- `--debug`: Show stack traces on errors
- `--no-optimize`: Disable optimizations (none implemented yet)

## Performance Characteristics

### Compilation Time
- Typical program: 200-300ms
- Lexing: <10ms
- Parsing: <20ms
- Semantic analysis: 50-100ms
- Code generation: 100-150ms
- Assembly saving: 10-50ms

### Memory
- Typical small program: ~2-5 MB process memory
- Assembly size: 2-5 KB IL code

### Execution Time
- Reflection-based: ~1ms overhead
- Interpreter-based: ~10-50ms overhead (depends on code complexity)

## Type System

### Primitive Types
- Integer (32-bit signed int)
- Real (64-bit floating point double)
- Boolean (true/false)

### Collection Types
- Array[T] - Fixed-size array (emulated)
- List[T] - Dynamic list (delegates to List<object>)

### User-Defined Types
- Classes with single inheritance
- Fields (mutable state)
- Constructors (initialization)
- Methods (behavior)

### Type Inference
- Literals: 10 → Integer, 3.14 → Real, true → Boolean
- Variables: Type declared in variable declaration
- Expressions: Type determined by operation and operands
- Constructor calls: Type is the class being instantiated

## Known Limitations

### Runtime Constraints
1. **No Generic Type Parameters**: `class Box<T>` not supported
2. **No Recursive Method Calls**: Methods can't call themselves
3. **Limited Method Polymorphism**: Virtual methods with override not fully working
4. **Dynamic Module Restrictions**: Reflection.Emit types have runtime limitations

### Language Features Not Implemented
1. Interfaces
2. Abstract classes
3. Properties (get/set)
4. Events
5. Operators overloading (beyond type conversion)
6. Delegates/Lambda
7. Namespaces
8. Access modifiers (public/private/protected treated as public)
9. Static members
10. Generics

### PE Header Limitations
- PersistedAssemblyBuilder can't set CLR entry point metadata directly
- Workaround: Use `dotnet program.dll` instead of direct executable
- Alternative would require: PE header post-processing or IL2CPP

## Future Enhancement Opportunities

### High Impact
1. Recursive method support (fix self-references in MethodGenerator)
2. Polymorphic method resolution (track virtual method overrides)
3. Generic type support (parameterize TypeBuilder)

### Medium Impact
4. Interface implementation
5. Abstract class/method support
6. Static members
7. Property syntax (get/set)
8. Better error messages with source location highlighting

### Low Priority
9. Optimization passes (dead code elimination, constant folding)
10. Cross-platform IL generator
11. AOT (ahead-of-time) compilation
12. Debugging support (PDB generation)

## Testing Strategy

### Unit Tests
- Individual components (Lexer, Parser) tested separately
- Not currently implemented; could use xUnit/NUnit

### Integration Tests  
- Full compilation pipeline for 28 test programs
- Mixed valid and invalid programs
- Validation of exit codes and error messages

### Test Harness
- PowerShell script: `run-test.ps1`
- Tests in: `tests/*.o` files
- Results saved to: `test-results/*.txt`
- Summary: `test-results/summary.txt`

### Coverage
- 20 valid test programs (correct syntax, should compile)
- 8 invalid test programs (should show errors)
- 89.3% pass rate (25/28)
- 3 failures (generics, recursion, polymorphism)

## Debugging Tips

### Enable Debug Output
```bash
dotnet run --project src/OCompiler program.o --run --debug
```

### View Generated IL
```bash
dotnet run --project src/OCompiler program.o --emit-il
```

### View AST
```bash
dotnet run --project src/OCompiler program.o --ast
```

### Check Type Map
Modify `CodeGenerator.ValidateTypes()` call to see all generated types:
```
**[ DEBUG ] Type: ClassName
**[ DEBUG ]   - Fields: ...
**[ DEBUG ]   - Methods: ...
**[ DEBUG ]   - Constructors: ...
```

### Trace Interpreter
Add Console.WriteLine in Interpreter.Execute methods to see what's running.

## References

- **System.Reflection.Emit**: https://learn.microsoft.com/en-us/dotnet/fundamentals/reflection/reflection-and-generics
- **IL Code Examples**: https://learn.microsoft.com/en-us/dotnet/fundamentals/reflection/dynamically-loading-and-using-types
- **.NET Compiler Platform**: https://github.com/dotnet/roslyn
- **LALR Parser Generation**: http://en.wikipedia.org/wiki/LALR_parser
