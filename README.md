# The `O` language compiler

## How to run

1. Install [dotnet](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-9.0.305-windows-x64-installer)

2. Setup the env

``` shell
dotnet build
```

3. Run(generate executable and run it)

```shell
dotnet run --project src/OCompiler -- tests/01_Hello.o --emit-exe app.exe
dotnet build/output.dll
```

4. Learn the flags

| Flagname | function |
| - | - |
| `--debug` | see clear `C#` output and validate errors |
| `--run` | run code without generation an executable file(depricated) |
| `--emit-exe <name>` | generate a `.dll` executable file |
| `--save-assembly <name>` | generate a `.dll` executable file(depricated) |
| `--emit-il` | prints an IL for a dotnet executable file |
| `--no-codegen` | skip an IL code generation |
| `--ast` | show an exact AST before and after optimizations |
| `--no-optimize` | skip optimizations step |
| `--tokens-only` | show parsed tokens from lexer |
| `--tokens-to-file` | prints all parsed tokens to a file `parsing_results<date>.txt` |

-----
## Integrated Tests

### Correct tests

| Filename | Description |
| - | - |
| 01_Hello.o | Minimal valid class with constructor. |
| 02_MaxInt.o | Finds max in array (uses Array.get/Length). |
| 03_ArraySquare.o | Fills an array with squares. |
| 04_InheritanceValid.o | Inheritance and method override. |
| 05_OverloadValid.o | Method overloading by parameter types. |
| 06_GenericsBox.o | Generic class Box[T] with get/set. |
| 07_ConstructorArgs.o | Constructor with argument and field assignment. |
| 08_RecursiveFactorial.o | Recursive method using integer methods. |
| 09_ListAppend.o | Using List[T] append/head. |
| 10_Polymorphism.o | Polymorphism via base reference to derived. |

### Failed tests

| Filename | Description |
| - | - |
| 11_SyntaxError.o | Deliberate missing 'end' to cause syntax error. |
| 12_TypeErrorReturn.o | Method declared Integer returns a Real. |
| 13_FieldAssignment.o | Attempts direct external assignment to a field (should be forbidden). |
| 14_UndefinedMethod.o | Calls a non-existent method on Integer. |
| 15_AmbiguousOverload.o | Two methods with identical signatures (ambiguous). |
