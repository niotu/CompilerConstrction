# Флаги командной строки O Language Compiler

## Обзор

O Language Compiler поддерживает широкий набор флагов для управления процессом компиляции, отладки и генерации кода.

---

## Базовое использование

```bash
dotnet run --project src/OCompiler -- <входной_файл.o> [флаги]
```

**Пример**:
```bash
dotnet run --project src/OCompiler -- tests/01_Hello.o --emit-dll output
```

---

## Флаги компиляции

### `--emit-dll <имя>`

**Описание**: Генерирует исполняемую .dll сборку с указанным именем.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --emit-dll myapp
```

**Результат**:
- Создается файл `build/myapp.dll`
- Создается файл `build/myapp.runtimeconfig.json`

**Запуск**:
```bash
dotnet build/myapp.dll
```

**Детали**:
- Автоматически создается точка входа `<Program>.Main()`
- Вызывается конструктор `Main.this()` (или другого класса, если указан `--entry-point`)
- Сборка совместима с .NET CLI

---

### `--entry-point <Класс>`

**Описание**: Указывает класс, который будет использован как точка входа программы.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --emit-dll app --entry-point MyClass
```

**Поведение**:
- По умолчанию используется класс `Main`
- Указанный класс должен существовать в программе
- Должен иметь конструктор без параметров `this()`
- При запуске будет вызван конструктор указанного класса

**Пример**:
```o
class MyClass is
    this() is
        // Точка входа программы
    end
end
```

```bash
dotnet run --project src/OCompiler -- program.o --entry-point MyClass --emit-dll app
dotnet build/app.dll  # Выполнится MyClass.this()
```

---

### `--save-assembly <путь>`

**Описание**: Сохраняет сгенерированную сборку в указанный файл (устаревший флаг, используйте `--emit-dll`).

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --save-assembly output.dll
```

**Замечание**: Предпочтительно использовать `--emit-dll`.

---

### `--no-codegen`

**Описание**: Пропускает этап генерации IL-кода. Полезно для тестирования парсера и семантики.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --no-codegen
```

**Результат**:
- Выполняется лексинг, парсинг, семантический анализ
- Генерация IL-кода НЕ выполняется
- Код выхода 0, если семантика корректна

**Применение**:
- Быстрая проверка синтаксиса и семантики
- Отладка semantic analysis без накладных расходов на codegen

---

### `--no-optimize`

**Описание**: Отключает оптимизации AST (constant folding, dead code elimination).

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --no-optimize
```

**Результат**:
- Пропускается этап оптимизаций
- AST передается в codegen "как есть"

**Применение**:
- Отладка оптимизатора
- Сравнение производительности оптимизированного и неоптимизированного кода

---

### `--semantic-only`

**Описание**: Останавливает компиляцию после семантического анализа.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --semantic-only
```

**Результат**:
- Выполняется: лексинг → парсинг → семантика
- НЕ выполняется: оптимизации, генерация кода

**Применение**:
- Быстрая проверка семантической корректности
- Тестирование semantic checker

---

## Флаги отладки

### `--debug`

**Описание**: Включает детальный отладочный вывод со stack traces.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --debug
```

**Вывод**:
- Полные stack traces при ошибках
- Детальная информация о генерируемых типах
- Отладочные сообщения от каждого этапа компиляции

**Пример вывода**:
```
**[ DEBUG ] Type: Main
**[ DEBUG ]   - Full name: Main
**[ DEBUG ]   - Type class: RuntimeTypeHandle
**[ DEBUG ]   - Assembly: GeneratedAssembly
**[ DEBUG ]   - Base type: Object
**[ DEBUG ]   - Constructors: 1
**[ DEBUG ]     - .ctor()
**[ DEBUG ]   - Fields: 1
**[ DEBUG ]     - Int32 x
```

---

### `--ast`

**Описание**: Выводит абстрактное синтаксическое дерево (AST) до и после оптимизаций.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --ast
```

**Вывод**:
```
**[ DEBUG ] Abstract Syntax Tree:
ProgramNode
  Class: Main, Extends: null
    ConstructorDeclaration: this()
      MethodBody:
        VariableDeclaration: x
          ConstructorInvocation: Integer
            IntegerLiteralNode: 10
```

**Применение**:
- Проверка корректности парсинга
- Отладка оптимизаций (сравнение до/после)
- Понимание структуры программы

---

### `--emit-il`

**Описание**: Выводит сгенерированный IL-код в текстовом виде.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --emit-il
```

**Вывод**:
```
**[ DEBUG ] Generated MSIL Code:
========================================
.assembly GeneratedAssembly { }

.class public Calc extends [mscorlib]System.Object
{
    .field public int32 n
    
    .method public hidebysig specialname rtspecialname 
            instance void .ctor() cil managed
    {
        .maxstack 8
        ldarg.0
        call instance void [mscorlib]System.Object::.ctor()
        ldarg.0
        ldc.i4 10
        stfld int32 Calc::n
        ret
    }
}
========================================
```

**Применение**:
- Проверка корректности генерации IL
- Обучение IL-кодированию
- Оптимизация сгенерированного кода

---

### `--tokens-only`

**Описание**: Выводит список токенов и останавливает компиляцию после лексинга.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --tokens-only
```

**Вывод**:
```
**[ INFO ] Tokens detected:

  1: CLASS           'class'         @ 1:1
  2: IDENTIFIER      'Main'          @ 1:7
  3: IS              'is'            @ 1:12
  4: THIS            'this'          @ 2:5
  5: LPAREN          '('             @ 2:9
  6: RPAREN          ')'             @ 2:10
  ...
  
**[ INFO ] Tokens calculated: 16 (except EOF)
```

**Применение**:
- Отладка лексера
- Проверка корректности токенизации
- Обучение работе лексического анализатора

---

### `--tokens-to-file`

**Описание**: Сохраняет список токенов в файл `parsing_result<timestamp>.txt`.

**Использование**:
```bash
dotnet run --project src/OCompiler -- program.o --tokens-to-file
```

**Результат**:
- Создается файл `parsing_result20251130_143025.txt` в текущей директории
- Содержит полный список токенов

**Применение**:
- Сохранение результатов лексинга для анализа
- Сравнение токенизации разных версий
- Документирование примеров

---

## Комбинирование флагов

Флаги можно комбинировать:

### Пример 1: Полная отладка
```bash
dotnet run --project src/OCompiler -- program.o --debug --ast --emit-il
```
**Результат**: Вывод AST, сгенерированного IL и детальной отладочной информации.

### Пример 2: Проверка семантики без кодогенерации
```bash
dotnet run --project src/OCompiler -- program.o --semantic-only --ast --debug
```
**Результат**: Анализ до codegen с выводом AST и отладочной информации.

### Пример 3: Компиляция с отладкой и сохранением
```bash
dotnet run --project src/OCompiler -- program.o --emit-dll app --debug --emit-il
```
**Результат**: Генерация DLL с выводом IL-кода и отладочной информации.

---

## Таблица всех флагов

| Флаг | Аргумент | Категория | Описание |
|------|----------|-----------|----------|
| `--emit-dll` | `<имя>` | Компиляция | Генерирует исполняемый .dll файл |
| `--entry-point` | `<класс>` | Компиляция | Указывает класс точки входа (default: Main) |
| `--save-assembly` | `<путь>` | Компиляция | Сохраняет сборку (устаревший, используйте --emit-dll) |
| `--no-codegen` | - | Компиляция | Пропускает генерацию IL-кода |
| `--no-optimize` | - | Компиляция | Отключает оптимизации AST |
| `--semantic-only` | - | Компиляция | Останавливает после семантического анализа |
| `--debug` | - | Отладка | Включает детальный отладочный вывод |
| `--ast` | - | Отладка | Выводит абстрактное синтаксическое дерево |
| `--emit-il` | - | Отладка | Выводит сгенерированный IL-код |
| `--tokens-only` | - | Отладка | Выводит токены и останавливается |
| `--tokens-to-file` | - | Отладка | Сохраняет токены в файл |

---

## Коды выхода

| Код | Значение |
|-----|----------|
| 0 | Успешная компиляция/выполнение |
| 1 | Ошибка компиляции (лексинг/парсинг/семантика/codegen) |
| 1 | Ошибка файловой системы (файл не найден, нет прав) |

---

## Примеры использования

### 1. Базовая компиляция в DLL
```bash
dotnet run --project src/OCompiler -- tests/01_Hello.o --emit-dll hello
dotnet build/hello.dll
```

### 2. Компиляция с другой точкой входа
```bash
dotnet run --project src/OCompiler -- tests/29_CustomEntryPoint.o --entry-point CustomEntry --emit-dll custom
dotnet build/custom.dll
```

### 3. Проверка синтаксиса и семантики
```bash
dotnet run --project src/OCompiler -- program.o --semantic-only
```

### 4. Отладка лексера
```bash
dotnet run --project src/OCompiler -- program.o --tokens-only
```

### 5. Полная отладка компиляции
```bash
dotnet run --project src/OCompiler -- program.o --debug --ast --emit-il --emit-dll debug_app
```

### 6. Проверка оптимизаций
```bash
# С оптимизациями
dotnet run --project src/OCompiler -- program.o --ast > with_opt.txt

# Без оптимизаций
dotnet run --project src/OCompiler -- program.o --ast --no-optimize > without_opt.txt

# Сравнение
diff with_opt.txt without_opt.txt
```

### 7. Быстрая проверка синтаксиса
```bash
dotnet run --project src/OCompiler -- program.o --no-codegen
```

---

## Переменные окружения

Компилятор не использует специальные переменные окружения, но учитывает стандартные:

- `DOTNET_CLI_TELEMETRY_OPTOUT=1` - отключение телеметрии .NET
- `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1` - пропуск первого запуска

---

## Troubleshooting

### Проблема: "Compiler not found"
**Решение**: Выполните `dotnet build src/OCompiler/OCompiler.csproj`

### Проблема: "Assembly saving not implemented"
**Решение**: Убедитесь, что используется .NET 9.0+

### Проблема: "Entry point not found"
**Решение**: Проверьте, что класс имеет конструктор `this()` без параметров

---

*Документация флагов командной строки O Language Compiler*
