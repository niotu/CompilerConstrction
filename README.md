# The `O` language compiler

- Files 01-10: Intended to be valid (should pass typical syntax + semantic checks).
- Files 11-15: Intended to fail for specific reasons (syntax error, type error, forbidden field assignment, undefined method, ambiguous overload).

# How to run

- Install [dotnet](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-9.0.305-windows-x64-installer)
- Setup the env

``` shell
dotnet build
```

- Run

``` shell
dotnet run --project src/OCompiler tests/<test_name> --tokens-only
```

### Correct tests

|Filename| Description |
|-|-|
01_Hello.o   |        - Minimal valid class with constructor.
02_MaxInt.o   |       - Finds max in array (uses Array.get/Length).
03_ArraySquare.o|     - Fills an array with squares.
04_InheritanceValid.o|- Inheritance and method override.
05_OverloadValid.o  | - Method overloading by parameter types.
06_GenericsBox.o  |   - Generic class Box[T] with get/set.
07_ConstructorArgs.o | - Constructor with argument and field assignment.
08_RecursiveFactorial.o | - Recursive method using integer methods.
09_ListAppend.o |     - Using List[T] append/head.
10_Polymorphism.o  |   - Polymorphism via base reference to derived.

### Failed tests

|Filename| Description |
|-|-|
11_SyntaxError.o |     - Deliberate missing 'end' to cause syntax error.
12_TypeErrorReturn.o | - Method declared Integer returns a Real.
13_FieldAssignment.o| - Attempts direct external assignment to a field (should be forbidden).
14_UndefinedMethod.o |- Calls a non-existent method on Integer.
15_AmbiguousOverload.o| - Two methods with identical signatures (ambiguous).
