# Стек технологий O Language Compiler

## Обзор технологий

Компилятор O Language построен на современном стеке .NET технологий с использованием проверенных библиотек для генерации парсеров и манипуляции IL-кодом.

---

## Таблица технологий

| Имя | Применение | Польза для конкретной задачи | Наша имплементация технологии |
|-----|------------|------------------------------|-------------------------------|
| **.NET SDK 9.0** | Платформа разработки и выполнения | Предоставляет runtime для компилятора и сгенерированных сборок; поддержка C# 12.0; кросс-платформенность | Используется как основная платформа; компилятор запускается через `dotnet run`; сгенерированные DLL требуют .NET runtime |
| **C# 12.0** | Язык реализации компилятора | Современный type-safe язык с мощными возможностями для работы с рефлексией и метапрограммированием | Весь компилятор написан на C#; использованы: pattern matching, records, nullable reference types, LINQ |
| **System.Reflection.Emit** | Динамическая генерация IL-кода | Позволяет создавать .NET типы и методы в runtime без промежуточных файлов | `CodeGenerator.cs`: создание AssemblyBuilder, ModuleBuilder, TypeBuilder; `MethodGenerator.cs`: генерация IL-инструкций через ILGenerator |
| **PersistedAssemblyBuilder** | Сохранение динамических сборок | В .NET 9+ позволяет сохранять динамически созданные сборки в PE-файлы (в старых версиях это было невозможно) | `CodeGenerator.SaveToFile()`: использование PersistedAssemblyBuilder для записи сборки в .dll файл |
| **System.Reflection.Metadata** | Работа с метаданными PE | Чтение и анализ метаданных .NET сборок; доступ к TypeDef, MethodDef, MetadataTokens | Используется для постобработки PE-файлов и установки EntryPoint в CLR header |
| **GPPG (Gardens Point Parser Generator)** | Генерация LALR(1) парсера | Автоматическое создание парсера из BNF-грамматики; проверенный инструмент для academic проектов | `Grammar.y` → `OParser.cs`; GPPG генерирует shift-reduce парсер с обработкой ошибок |
| **Mono.Cecil** | Постобработка PE-файлов | Позволяет читать и модифицировать .NET сборки на уровне CIL; установка EntryPoint в PE header | `CecilHelpers.cs`: установка entry point method token в CLR header PE-файла для создания executable |
| **System.Collections.Generic** | Структуры данных | Эффективные коллекции для хранения токенов, AST, символов | `Dictionary<>` для symbol tables, type maps, method caches; `List<>` для токенов, AST nodes |
| **LINQ** | Запросы к коллекциям | Удобный синтаксис для фильтрации и трансформации данных | Используется повсеместно для работы с AST, поиска классов/методов, фильтрации токенов |
| **Fish Shell** | Командная оболочка | Современный shell для тестирования на Linux/macOS | Скрипты `run-tests.sh` написаны для fish; поддержка параметров, цветного вывода, форматирования |
| **Bash** | Резервная командная оболочка | POSIX-совместимый shell для широкой совместимости | `run-tests.sh` совместим с bash; используется на серверах и CI/CD |

---

## Детальное описание ключевых технологий

### 1. System.Reflection.Emit

**Версия**: 4.7.0+  
**Пакет**: `System.Reflection.Emit`, `System.Reflection.Emit.ILGeneration`

#### Что это?
Библиотека для динамической генерации .NET типов и IL-кода во время выполнения программы.

#### Зачем нужна?
Позволяет компилятору создавать настоящие .NET классы без необходимости писать промежуточный C# код или использовать внешние компиляторы.

#### Как используется?

**Создание сборки**:
```csharp
var assemblyName = new AssemblyName("GeneratedAssembly");
var assemblyBuilder = new PersistedAssemblyBuilder(
    assemblyName,
    typeof(object).Assembly
);
var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");
```

**Создание класса**:
```csharp
var typeBuilder = moduleBuilder.DefineType(
    "MyClass",
    TypeAttributes.Public | TypeAttributes.Class,
    typeof(object)
);
```

**Создание метода**:
```csharp
var methodBuilder = typeBuilder.DefineMethod(
    "MyMethod",
    MethodAttributes.Public,
    typeof(void),
    Type.EmptyTypes
);

var il = methodBuilder.GetILGenerator();
il.Emit(OpCodes.Ldstr, "Hello, World!");
il.Emit(OpCodes.Call, typeof(Console).GetMethod("WriteLine", new[] { typeof(string) }));
il.Emit(OpCodes.Ret);
```

**Финализация**:
```csharp
Type completedType = typeBuilder.CreateTypeInfo().AsType();
```

#### Польза для проекта
- **Прямая генерация .NET типов** без промежуточного C# кода
- **Высокая производительность** сгенерированного кода
- **Полная совместимость** с .NET ecosystem
- **Поддержка всех .NET конструкций**: классы, методы, поля, конструкторы, generic типы

---

### 2. GPPG (Gardens Point Parser Generator)

**Версия**: 1.5.3.1  
**Пакет**: `YaccLexTools.Gppg`

#### Что это?
Генератор LALR(1) парсеров из BNF-грамматик, аналог yacc/bison для .NET.

#### Зачем нужен?
Автоматическое создание парсера из декларативного описания грамматики вместо ручного написания shift-reduce автомата.

#### Как используется?

**Грамматика (Grammar.y)**:
```yacc
%token CLASS IDENTIFIER IS END

ClassDeclaration
    : CLASS IDENTIFIER IS MemberList END
    {
        $$ = new ClassDeclaration($2, "", "", $4);
    }
    ;
```

**Генерация парсера**:
```bash
gppg /gplex Grammar.y > OParser.cs
```

**Использование**:
```csharp
var scanner = new ManualLexerAdapter(tokens);
var parser = new Parser(scanner);
bool success = parser.Parse();
var ast = (ProgramNode)parser.CurrentSemanticValue.ast;
```

#### Польза для проекта
- **Декларативное описание** синтаксиса языка
- **Автоматическая генерация** парсера и обработчика ошибок
- **LALR(1) алгоритм** - эффективный и проверенный
- **Легкость расширения** - добавление новых конструкций требует только изменения грамматики

---

### 3. Mono.Cecil

**Версия**: 0.11.4  
**Пакет**: `Mono.Cecil`

#### Что это?
Библиотека для чтения, модификации и создания .NET сборок на уровне CIL (Common Intermediate Language).

#### Зачем нужна?
PersistedAssemblyBuilder не предоставляет прямого способа установки EntryPoint в PE header. Cecil позволяет постобработать .dll и установить EntryPoint.

#### Как используется?

```csharp
public static void SetEntryPointWithCecil(string dllPath)
{
    var assembly = AssemblyDefinition.ReadAssembly(dllPath);
    
    // Поиск метода Main
    var programType = assembly.MainModule.Types
        .FirstOrDefault(t => t.Name == "<Program>");
    
    var mainMethod = programType?.Methods
        .FirstOrDefault(m => m.Name == "Main");
    
    if (mainMethod != null)
    {
        assembly.EntryPoint = mainMethod;
        assembly.Write(dllPath);
    }
}
```

#### Польза для проекта
- **Установка EntryPoint** в PE header для создания executable
- **Модификация метаданных** без пересборки всей сборки
- **Кросс-платформенность** - работает на Windows/Linux/macOS

---

### 4. PersistedAssemblyBuilder (.NET 9+)

**Версия**: .NET 9.0  
**Namespace**: `System.Reflection.Emit`

#### Что это?
Новый класс в .NET 9, позволяющий сохранять динамически созданные сборки в файлы.

#### Зачем нужен?
До .NET 9 динамические сборки (AssemblyBuilder) можно было использовать только в памяти. PersistedAssemblyBuilder решает эту проблему.

#### Как используется?

```csharp
var assemblyBuilder = new PersistedAssemblyBuilder(
    new AssemblyName("MyAssembly"),
    typeof(object).Assembly
);

// ... создание типов

using (var stream = new FileStream("output.dll", FileMode.Create))
{
    assemblyBuilder.Save(stream);
}
```

#### Польза для проекта
- **Сохранение сборок в файлы** (.dll/.exe)
- **Создание standalone executables**
- **Интеграция с dotnet CLI** (`dotnet output.dll`)

---

## Конфигурация проекта

### OCompiler.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <RootNamespace>OCompiler</RootNamespace>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="YaccLexTools.Gppg" Version="1.5.3.1" />
    <PackageReference Include="System.Reflection.Emit" Version="4.7.0" />
    <PackageReference Include="System.Reflection.Metadata" Version="8.0.0" />
    <PackageReference Include="System.Reflection.Emit.ILGeneration" Version="4.7.0" />
    <PackageReference Include="System.Reflection.Emit.Lightweight" Version="4.7.0" />
    <PackageReference Include="Mono.Cecil" Version="0.11.4" />
  </ItemGroup>
</Project>
```

---

## Зависимости и совместимость

### Требования к окружению

| Компонент | Минимальная версия | Рекомендуемая версия |
|-----------|-------------------|---------------------|
| .NET SDK | 9.0 | 9.0+ |
| ОС | Windows 10, Linux kernel 4.x, macOS 10.15 | Любая с .NET 9 |
| Память | 512 MB | 1 GB+ |
| Дисковое пространство | 100 MB | 200 MB+ |

### Платформы

- **x64**: Полная поддержка
- **ARM64**: Поддержка через .NET runtime
- **x86**: Не тестировалось

---

## Сравнение с альтернативами

### Альтернативные подходы к генерации кода

| Подход | Преимущества | Недостатки | Почему не выбрали |
|--------|--------------|------------|-------------------|
| **Roslyn (C# Compiler API)** | Полная поддержка C#, IntelliSense, debugging | Зависимость от C# синтаксиса, overhead компиляции | Хотели прямую генерацию IL без промежуточного C# |
| **LLVM** | Высокая оптимизация, кросс-платформенность | Сложность интеграции с .NET, нет прямой поддержки .NET типов | Требует биндингов, не интегрируется с .NET ecosystem |
| **CodeDOM** | Абстрактная модель кода, генерация разных языков | Устаревшая технология, ограниченная функциональность | Reflection.Emit более мощный и современный |
| **T4 Templates** | Кодогенерация на этапе компиляции | Только compile-time, нет runtime генерации | Нужна runtime генерация для JIT-компиляции |

---

## Статистика использования технологий

### Распределение кода по технологиям

| Технология | Файлов | Строк кода | % от проекта |
|------------|--------|------------|--------------|
| Reflection.Emit | 4 | ~2,500 | 17% |
| GPPG/Parser | 3 | ~1,200 | 8% |
| Semantic Analysis | 5 | ~3,500 | 23% |
| Lexer | 5 | ~800 | 5% |
| AST | 1 | ~400 | 3% |
| Runtime/Utils | 3 | ~500 | 3% |
| Tests | 30 | ~1,500 | 10% |
| Scripts | 2 | ~600 | 4% |
| Documentation | 5 | ~4,000 | 27% |

---

*Документация технологического стека O Language Compiler*
