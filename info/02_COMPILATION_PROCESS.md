# Процесс компиляции O Language

## Общая схема

Компиляция программы на языке O проходит через 6 основных этапов:

```
Исходный код (.o файл)
    ↓
[ЭТАП 1] Лексический анализ
    ↓
[ЭТАП 2] Синтаксический анализ
    ↓
[ЭТАП 3] Семантический анализ
    ↓
[ЭТАП 4] Оптимизации AST
    ↓
[ЭТАП 5] Генерация IL-кода
    ↓
[ЭТАП 6] Сохранение сборки
    ↓
Исполняемый файл (.dll)
```

---

## ЭТАП 1: Лексический анализ (Tokenization)

### Файлы
- `src/OCompiler/Lexer/OLexer.cs`
- `src/OCompiler/Lexer/Token.cs`
- `src/OCompiler/Lexer/TokenType.cs`

### Входные данные
- Исходный текст программы (строка)

### Выходные данные
- Список токенов (List&lt;Token&gt;)

### Процесс

1. **Чтение исходного кода посимвольно**
   - Отслеживание позиции (строка, столбец)
   - Пропуск пробелов и переносов строк

2. **Распознавание токенов**:
   - **Ключевые слова**: `class`, `method`, `is`, `end`, `if`, `while`, `return`, `this`, `var`, `extends`, `loop`, `then`, `else`
   - **Идентификаторы**: `[a-zA-Z_][a-zA-Z0-9_]*`
   - **Литералы**:
     - Integer: `[0-9]+`
     - Real: `[0-9]+\.[0-9]+`
     - Boolean: `true`, `false`
   - **Операторы**: `:=`, `=>`, `.`, `:`, `,`
   - **Разделители**: `(`, `)`, `[`, `]`
   - **Комментарии**: `// текст до конца строки`

3. **Создание токенов**
   ```csharp
   new Token(TokenType.CLASS, "class", Position(line: 1, col: 1))
   new Token(TokenType.IDENTIFIER, "Main", Position(line: 1, col: 7))
   ```

4. **Обработка ошибок**
   - Неожиданные символы → LexerException
   - Некорректные числа → LexerException

### Пример

**Входной код**:
```o
class Main is
    this() is
        var x : Integer(10)
    end
end
```

**Результат токенизации**:
```
1:  CLASS           'class'         @ 1:1
2:  IDENTIFIER      'Main'          @ 1:7
3:  IS              'is'            @ 1:12
4:  THIS            'this'          @ 2:5
5:  LPAREN          '('             @ 2:9
6:  RPAREN          ')'             @ 2:10
7:  IS              'is'            @ 2:12
8:  VAR             'var'           @ 3:9
9:  IDENTIFIER      'x'             @ 3:13
10: COLON           ':'             @ 3:15
11: IDENTIFIER      'Integer'       @ 3:17
12: LPAREN          '('             @ 3:24
13: INTEGER_LITERAL '10'            @ 3:25
14: RPAREN          ')'             @ 3:27
15: END             'end'           @ 4:5
16: END             'end'           @ 5:1
17: EOF                             @ 5:4
```

---

## ЭТАП 2: Синтаксический анализ (Parsing)

### Файлы
- `src/OCompiler/Parser/Grammar.y` (описание грамматики)
- `src/OCompiler/Parser/OParser.cs` (сгенерированный парсер)
- `src/OCompiler/Parser/AstNodes.cs` (узлы AST)

### Входные данные
- Список токенов

### Выходные данные
- Абстрактное синтаксическое дерево (AST) - `ProgramNode`

### Процесс

1. **Парсинг с использованием LALR(1) автомата**
   - Грамматика описана в Grammar.y
   - GPPG генерирует парсер из грамматики
   - Shift-Reduce стратегия разбора

2. **Построение AST-дерева**
   - Каждое правило грамматики создает соответствующий узел
   - Узлы связываются в иерархическую структуру

3. **Обработка ошибок**
   - Синтаксические ошибки → Parse error
   - Несоответствие грамматике → Compilation failed

### Структура AST

```
ProgramNode
├── ClassDeclaration "Main"
│   ├── ConstructorDeclaration this()
│   │   └── MethodBodyNode
│   │       └── VariableDeclaration "x"
│   │           └── ConstructorInvocation Integer(10)
│   └── ...
└── ...
```

### Основные типы узлов AST

| Узел | Описание |
|------|----------|
| `ProgramNode` | Корневой узел (список классов) |
| `ClassDeclaration` | Объявление класса |
| `ConstructorDeclaration` | Конструктор класса |
| `MethodDeclaration` | Метод класса |
| `VariableDeclaration` | Объявление переменной |
| `Assignment` | Присваивание |
| `IfStatement` | Условный оператор |
| `WhileLoop` | Цикл |
| `ReturnStatement` | Возврат значения |
| `ExpressionNode` | Выражение (вызов, литерал, бинарная операция) |

---

## ЭТАП 3: Семантический анализ

### Файлы
- `src/OCompiler/Semantic/Checks.cs`
- `src/OCompiler/Semantic/Hierarchy.cs`
- `src/OCompiler/Semantic/SymbolTable.cs`

### Входные данные
- AST (ProgramNode)

### Выходные данные
- Валидированный и аннотированный AST
- Список ошибок (если есть)

### Процесс

#### 3.1. Построение иерархии классов
```csharp
ClassHierarchy:
  Class → AnyValue → Integer, Real, Boolean
  Class → AnyRef → Array, List
  Main → Class (пользовательский)
```

#### 3.2. Проверки

**1. Проверка дублирования классов**
```csharp
class A is end
class A is end  // ОШИБКА: дублирование
```

**2. Проверка наследования**
```csharp
class A extends NonExistent  // ОШИБКА: базовый класс не найден
```

**3. Проверка типов**
```csharp
var x : Integer
x := Real(3.14)  // ОШИБКА: несовместимые типы
```

**4. Разрешение символов**
```csharp
method foo() is
    return unknownVariable  // ОШИБКА: переменная не объявлена
end
```

**5. Проверка вызовов методов**
```csharp
var x : Integer(5)
x.NonExistentMethod()  // ОШИБКА: метод не найден
```

**6. Проверка сигнатур методов**
```csharp
method add(x: Integer) is end
method add(x: Integer) is end  // ОШИБКА: дублирование
```

**7. Проверка return statements**
```csharp
method getValue() : Integer is
    // ОШИБКА: нет return
end
```

### Результат

- **Успех**: Семантически корректный AST
- **Провал**: Список ошибок с описанием и позициями

---

## ЭТАП 4: Оптимизации AST

### Файлы
- `src/OCompiler/Semantic/Optimizations.cs`

### Процесс

#### 4.1. Constant Folding (Сворачивание констант)
```o
var x : Integer(5).Plus(Integer(3))
// Оптимизация →
var x : Integer(8)
```

#### 4.2. Dead Code Elimination
```o
if false then
    // Удаляется
end
```

#### 4.3. Normalization
- Преобразование FunctionalCall → ConstructorInvocation
- Упрощение выражений

### Флаг отключения
```bash
dotnet run --project src/OCompiler -- program.o --no-optimize
```

---

## ЭТАП 5: Генерация IL-кода

### Файлы
- `src/OCompiler/CodeGeneration/CodeGenerator.cs`
- `src/OCompiler/CodeGeneration/MethodGenerator.cs`
- `src/OCompiler/CodeGeneration/TypeMapper.cs`

### Входные данные
- Оптимизированный AST

### Выходные данные
- In-memory Assembly (System.Reflection.Assembly)

### Процесс

#### Фаза 5.1: Объявление типов (Type Declaration)

```csharp
foreach (class in AST) {
    TypeBuilder tb = moduleBuilder.DefineType(
        className,
        TypeAttributes.Public | TypeAttributes.Class,
        baseType
    );
    RegisterType(className, tb);
}
```

**Результат**: Созданы TypeBuilder для всех классов

#### Фаза 5.2: Генерация членов класса (Member Generation)

**1. Генерация полей**
```csharp
FieldBuilder field = typeBuilder.DefineField(
    fieldName,
    fieldType,
    FieldAttributes.Public
);
```

**2. Генерация конструкторов**
```csharp
ConstructorBuilder ctor = typeBuilder.DefineConstructor(
    MethodAttributes.Public,
    CallingConventions.Standard,
    parameterTypes
);

ILGenerator il = ctor.GetILGenerator();
// Генерация IL-инструкций конструктора
il.Emit(OpCodes.Ldarg_0);  // this
il.Emit(OpCodes.Call, baseConstructor);
// ... инициализация полей
il.Emit(OpCodes.Ret);
```

**3. Генерация методов**
```csharp
MethodBuilder method = typeBuilder.DefineMethod(
    methodName,
    MethodAttributes.Public,
    returnType,
    parameterTypes
);

ILGenerator il = method.GetILGenerator();
// Генерация IL-инструкций тела метода
GenerateMethodBody(method.Body, il);
```

#### Фаза 5.3: Финализация типов

```csharp
foreach (var typeBuilder in typeBuilders) {
    Type completedType = typeBuilder.CreateTypeInfo().AsType();
    RegisterCompletedType(className, completedType);
}
```

**Результат**: Все типы завершены и готовы к использованию

#### Фаза 5.4: Генерация точки входа (EntryPoint)

```csharp
// Создание класса <Program> со статическим Main
class <Program> {
    static void Main(string[] args) {
        // Создание экземпляра входного класса
        new Main();  // this() выполнится автоматически
    }
}
```

### Маппинг типов O → .NET

| O Type | .NET Type |
|--------|-----------|
| Integer | System.Int32 |
| Real | System.Double |
| Boolean | System.Boolean |
| Array[T] | BuiltinTypes.OArray |
| List[T] | System.Collections.Generic.List&lt;object&gt; |
| User class | Dynamic TypeBuilder → Type |

### Пример генерации IL

**O код**:
```o
class Calc is
    n: Integer
    this() is
        n := Integer(10)
    end
end
```

**Сгенерированный IL**:
```il
.class public Calc extends [mscorlib]System.Object
{
    .field public int32 n
    
    .method public hidebysig specialname rtspecialname 
            instance void .ctor() cil managed
    {
        .maxstack 8
        ldarg.0              // this
        call instance void [mscorlib]System.Object::.ctor()
        ldarg.0              // this
        ldc.i4 10            // push 10
        stfld int32 Calc::n  // store to field
        ret
    }
}
```

---

## ЭТАП 6: Сохранение сборки

### Файлы
- `src/OCompiler/CodeGeneration/CodeGenerator.cs` (метод SaveToFile)
- `src/OCompiler/CodeGeneration/CecilHelpers.cs` (постобработка)

### Процесс

#### 6.1. Использование PersistedAssemblyBuilder (.NET 9+)

```csharp
var assemblyBuilder = new PersistedAssemblyBuilder(
    assemblyName,
    typeof(object).Assembly
);

// ... генерация типов

using (var stream = new FileStream(outputPath, FileMode.Create)) {
    assemblyBuilder.Save(stream);
}
```

#### 6.2. Постобработка PE-файла (Mono.Cecil)

```csharp
// Установка точки входа в PE header
CecilHelpers.SetEntryPointWithCecil(dllPath);
```

#### 6.3. Создание runtimeconfig.json

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

### Результат

- **output.dll** - исполняемая сборка .NET
- **output.runtimeconfig.json** - конфигурация runtime

### Запуск

```bash
dotnet output.dll
```

---

## Обработка ошибок на каждом этапе

| Этап | Тип ошибки | Код выхода |
|------|------------|------------|
| Лексинг | LexerException | 1 |
| Парсинг | Parse error | 1 |
| Семантика | Semantic error | 1 |
| Генерация кода | Code generation error | 1 |
| Сохранение | File I/O error | 1 |

---

## Временные характеристики

**Типичная компиляция** (для программы ~100 строк):

```
Лексинг:           5-10ms    (3-5%)
Парсинг:          10-20ms    (5-8%)
Семантика:        50-100ms   (20-35%)
Оптимизация:      20-40ms    (10-15%)
Генерация IL:     100-150ms  (40-50%)
Сохранение:       10-50ms    (5-15%)
─────────────────────────────────────
ИТОГО:           195-370ms   (100%)
```

---

## Диагностика процесса компиляции

### Просмотр токенов
```bash
dotnet run --project src/OCompiler -- program.o --tokens-only
```

### Просмотр AST
```bash
dotnet run --project src/OCompiler -- program.o --ast
```

### Просмотр оптимизированного AST
```bash
dotnet run --project src/OCompiler -- program.o --ast --no-optimize
```

### Просмотр IL-кода
```bash
dotnet run --project src/OCompiler -- program.o --emit-il
```

### Детальная отладка
```bash
dotnet run --project src/OCompiler -- program.o --debug
```

---

*Документация процесса компиляции O Language Compiler*
