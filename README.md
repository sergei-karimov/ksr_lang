# KSR

**KSR** is a statically-typed, Kotlin-inspired language that compiles to C# and runs on the .NET runtime.
It is designed to feel like Kotlin — concise, expressive, null-safe — while giving you full access to the entire .NET and NuGet ecosystem out of the box.

---

## Why KSR?

Kotlin is a great language, but targeting the JVM means carrying the JVM.
C# is powerful, but its syntax is verbose compared to modern languages.

KSR sits in the middle: **Kotlin-style syntax, .NET runtime, zero JVM overhead**.

- Write code that looks like Kotlin
- Use any NuGet package — Raylib, ASP.NET, Entity Framework, anything
- Compile to native via `dotnet publish`
- Zero runtime dependency beyond .NET itself
- Hooks into the standard `dotnet build` pipeline — no extra tools or steps

---

## Quick Start

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer

### Install

**Windows (PowerShell):**
```powershell
.\scripts\install.ps1
```

**Linux / macOS:**
```bash
./scripts/install.sh
```

The installer builds all packages from source and installs the `ksr` global tool, `dotnet new` templates, and the VS Code extension.

### Create a project

```bash
dotnet new ksr-console -n MyApp
cd MyApp
dotnet run
```

```
Hello from KSR!
```

---

## Language Tour

### Variables

```kotlin
val name = "Alice"         // immutable
var count = 0              // mutable
var score: Double = 9.5    // explicit type
val pi = 3.14159           // float literal
```

### Functions

```kotlin
fun add(a: Int, b: Int): Int {
    return a + b
}

fun greet(name: String) {
    println("Hello, ${name}!")
}

async fun fetchData(url: String): String {
    return await httpClient.getAsync(url)
}
```

### Data Classes

Sealed value types — like Kotlin data classes or Rust structs. No inheritance.

```kotlin
data class Point(x: Int, y: Int)
data class User(name: String, age: Int)

val p = Point(3, 4)
val u = User("Alice", 30)
```

### Extension Functions

Add behaviour to any type without modifying it.

```kotlin
fun Point.distanceSq(other: Point): Int {
    val dx = this.x - other.x
    val dy = this.y - other.y
    return dx * dx + dy * dy
}

fun User.greet() {
    println("Hello, ${this.name}! You are ${this.age} years old.")
}

fun User.isAdult(): Bool {
    return this.age >= 18
}
```

### Null Safety

```kotlin
var name: String? = null
val len = name?.length ?: 0    // safe call + elvis operator
```

### Control Flow

```kotlin
// if / else
if (x > 0) {
    println("positive")
} else {
    println("non-positive")
}

// while
var i = 10
while (i > 0) {
    i -= 1
}

// for — inclusive range
for (i in 1..10) {
    println("${i}")
}

// for — exclusive range
for (i in 0..<length) {
    println(items[i])
}
```

### Arrays

```kotlin
val cells = new Bool[160 * 90]   // zero-initialised
cells[0] = true
val first = cells[0]
```

### String Templates

```kotlin
val x = 42
println("The answer is ${x}!")
println("Sum: ${a + b}")
```

### Pattern Matching (`when`)

`when` is an expression that works like Kotlin's `when` / Rust's `match`.

```kotlin
// Subject form — match a value
val label = when (n) {
    1    -> "one"
    2    -> "two"
    else -> "many"
}

// Subject-less form — guard conditions
val sign = when {
    x > 0  -> "positive"
    x < 0  -> "negative"
    else   -> "zero"
}

// As a statement (side effects per arm)
when (code) {
    200 -> println("OK")
    404 -> println("not found")
    else -> println("error")
}
```

`when` compiles to a C# switch expression (subject form) or ternary chain (subject-less form) in value context, and to an `if / else-if / else` chain when used as a statement.

### Interfaces

Define a trait with `interface`, implement it with `implement ... for ...`.

```kotlin
interface Shape {
    fun area(): Double
    fun perimeter(): Double
}

data class Circle(r: Double)

implement Shape for Circle {
    fun area(): Double { return 3.14159 * this.r * this.r }
    fun perimeter(): Double { return 2.0 * 3.14159 * this.r }
}

fun printArea(s: Shape) {
    println("area = ${s.area()}")
}
```

A type can implement multiple interfaces:

```kotlin
interface Named { fun name(): String }

implement Named for Circle {
    fun name(): String { return "circle" }
}
```

The generated C# uses the standard `I`-prefix convention (`Shape` → `IShape`).

### Async / Await

```kotlin
// Async function — return type is the inner (unwrapped) type
async fun fetchGreeting(name: String): String {
    await Task.delay(100)
    return "Hello, ${name}!"
}

// Async void (no return value)
async fun logAsync(msg: String) {
    await Task.delay(1)
    println(msg)
}

// @ValueTask annotation — zero-allocation hot-path variant
@ValueTask
async fun fastCompute(n: Int): Int {
    await Task.delay(0)
    return n * 2
}

// Async main is fully supported
async fun main() {
    val greeting = await fetchGreeting("KSR")
    println(greeting)
    val result = await fastCompute(21)
    println("result = ${result}")
}
```

`async fun f(): T` compiles to `async Task<T> f()`.
`async fun f()` compiles to `async Task f()`.
`@ValueTask async fun f(): T` compiles to `async ValueTask<T> f()`.

The global flag `--async-return=valuetask` makes all async functions use `ValueTask` by default:

```bash
ksr myapp.ksr --async-return=valuetask
```

### Generic functions

Functions and extension functions can declare type parameters with `<T, U, ...>`:

```kotlin
fun <T> identity(x: T): T {
    return x
}

fun <T, U> first(a: T, b: U): T {
    return a
}

// Generic extension function on List<T>
fun <T> List<T>.second(): T {
    return this[1]
}

fun main() {
    println(identity(42))          // 42
    println(identity("hello"))     // hello

    val nums: List<Int> = [10, 20, 30]
    println(nums.second())         // 20
}
```

Type arguments are inferred by the C# compiler from call-site context — you never need to write `identity<Int>(42)`.

### Collections

KSR has immutable and mutable variants of list and map.

| KSR type | C# type | Semantics |
|---|---|---|
| `List<T>` | `IReadOnlyList<T>` | Immutable — default |
| `MutableList<T>` | `List<T>` | Mutable — call `.add()`, `.remove()`, etc. |
| `Map<K,V>` | `IReadOnlyDictionary<K,V>` | Immutable — default |
| `MutableMap<K,V>` | `Dictionary<K,V>` | Mutable — `map[key] = value` |

```kotlin
// Immutable (preferred)
val nums: List<Int> = [1, 2, 3]
val empty: List<String> = []
val scores: Map<String, Int> = ["alice": 10, "bob": 7]

// Mutable when you need to modify
var items: MutableList<String> = ["a", "b"]
items.add("c")                              // standard List<T>.Add

var lookup: MutableMap<String, Int> = []
lookup["key"] = 42
```

### Lambdas & LINQ

```kotlin
val alive = cells.count { it }              // implicit 'it'
val names = users.select { u -> u.name }
```

### Namespace Imports

```kotlin
use Raylib_cs
use System.Collections.Generic
```

### Standard Library

KSR ships three built-in modules. Add them with `use`:

#### `ksr.io` — file system and console I/O

```kotlin
use ksr.io

fun main() {
    // Console
    IO.print("Enter your name: ")
    val name: String? = IO.readLine()
    val n: Int?       = IO.readInt()

    // Files
    File.write("out.txt", "hello KSR")
    val text  = File.read("out.txt")
    val lines = File.lines("out.txt")
    val ok    = File.exists("out.txt")
    File.append("log.txt", "entry\n")
    File.delete("out.txt")

    // Paths
    val joined = Path.join("dir", "file.txt")  // "dir/file.txt"
    val ext    = Path.extension("archive.tar.gz")  // ".gz"
    val name2  = Path.fileName("/usr/bin/ksr")     // "ksr"
    val stem   = Path.fileStem("/usr/bin/ksr")     // "ksr"
    val abs    = Path.absolute("relative/path")
}
```

#### `ksr.text` — string utilities

```kotlin
use ksr.text

fun main() {
    val n: Int?    = Text.toInt("42")       // nullable parse
    val d: Double? = Text.toDouble("3.14")

    val s = Text.trim("  hello  ")          // "hello"
    val u = Text.toUpper("hello")           // "HELLO"
    val l = Text.toLower("HELLO")           // "hello"

    val ok  = Text.startsWith("hello", "he")
    val ok2 = Text.endsWith("hello", "lo")
    val ok3 = Text.contains("hello", "ell")

    val parts  = Text.split("a,b,c", ",")   // List<String>
    val joined = Text.join(" | ", parts)    // "a | b | c"

    val rep  = Text.repeat("ab", 3)         // "ababab"
    val repl = Text.replace("hello", "l", "r") // "herro"
    val sub  = Text.substring("hello", 1, 3)   // "ell"
    val len  = Text.length("hello")         // 5
}
```

#### `ksr.collections` — higher-order list and map operations

`ksr.collections` provides all operations as **extension methods** (fluent style) and as **static `Lst`/`Mp` helpers** (both work).

```kotlin
use ksr.collections

fun main() {
    val nums: List<Int> = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10]

    // ── Fluent style (recommended) ────────────────────────────────────────────
    val evens   = nums.filter { n -> n % 2 == 0 }         // [2, 4, 6, 8, 10]
    val squares = evens.map { n -> n * n }                 // [4, 16, 36, 64, 100]
    val total   = nums.fold(0) { acc, n -> acc + n }       // 55
    val top3    = nums.sortedBy { n -> -n }.take(3)        // [10, 9, 8]

    println(evens.joinToString(", "))    // 2, 4, 6, 8, 10
    println("any > 9? ${nums.any { n -> n > 9 }}")
    println("all > 0? ${nums.all { n -> n > 0 }}")

    // ── Static style (also available) ────────────────────────────────────────
    val top3   = Lst.take(Lst.sortedBy(nums, { n -> -n }), 3)  // [10, 9, 8]

    // Access
    val first = Lst.first(nums)       // 1
    val last  = Lst.last(nums)        // 10
    val found = Lst.find(nums, { n -> n > 7 })  // 8

    // Combining
    val a: List<Int> = [1, 2]
    val b: List<Int> = [3, 4]
    val ab = Lst.concat(a, b)         // [1, 2, 3, 4]
    val c  = Lst.plus(ab, 5)          // [1, 2, 3, 4, 5]

    // Grouping
    val words: List<String> = ["apple", "ant", "cherry"]
    val byLen = Lst.groupBy(words, { w -> Lst.size([w]) })

    // String
    println(Lst.joinToString(evens, ", "))   // "2, 4, 6, 8, 10"

    // Conversion
    val mutable = Lst.toMutable(nums)  // MutableList<Int>
    mutable.add(11)

    // Map operations
    val ages: Map<String, Int> = ["alice": 30, "bob": 25]

    val names    = Mp.keys(ages)                               // ["alice", "bob"]
    val doubled  = Mp.mapValues(ages, { a -> a * 2 })         // ["alice": 60, ...]
    val seniors  = Mp.filter(ages, { k, v -> v >= 28 })
    val aliceAge = Mp.getOrDefault(ages, "alice", 0)          // 30

    val mutableMap = Mp.toMutable(ages)   // MutableMap<String, Int>
    mutableMap["carol"] = 28
}
```

**`Lst` methods:**

| Method | Description |
|---|---|
| `map(list, fn)` | Transform each element |
| `filter(list, pred)` | Keep matching elements |
| `flatMap(list, fn)` | Map then flatten |
| `flatten(lists)` | Flatten list of lists |
| `fold(list, init, fn)` | Accumulate with initial value |
| `forEach(list, fn)` | Side-effect iteration |
| `any(list, pred)` | Any element matches |
| `all(list, pred)` | All elements match |
| `none(list, pred)` | No element matches |
| `count(list, pred)` | Count matching elements |
| `find(list, pred)` | First matching element (nullable) |
| `first(list)` / `last(list)` | First / last element |
| `get(list, i)` | Element by index |
| `size(list)` | Element count |
| `isEmpty(list)` | True if empty |
| `contains(list, item)` | Membership test |
| `sum(list)` / `min(list)` / `max(list)` | Numeric aggregates |
| `sorted(list)` | Natural-order sort (new list) |
| `sortedBy(list, fn)` | Sort by key (new list) |
| `reversed(list)` | Reversed (new list) |
| `take(list, n)` | First n elements |
| `drop(list, n)` | Skip first n elements |
| `takeWhile(list, pred)` | Elements while predicate holds |
| `dropWhile(list, pred)` | Skip while predicate holds |
| `distinct(list)` | Remove duplicates |
| `concat(a, b)` | Concatenate two lists |
| `plus(list, item)` | Append item (new list) |
| `zip(a, b)` | Pair elements from two lists |
| `groupBy(list, fn)` | Group into `Map<K, List<T>>` |
| `joinToString(list, sep)` | Join elements as string |
| `toMutable(list)` | Convert to `MutableList<T>` |
| `toList(iterable)` | Wrap `IEnumerable<T>` |

**`Mp` methods:**

| Method | Description |
|---|---|
| `keys(map)` | Keys as `List<K>` |
| `values(map)` | Values as `List<V>` |
| `containsKey(map, key)` | Key membership test |
| `get(map, key)` | Nullable value lookup |
| `getOrDefault(map, key, default)` | Value or fallback |
| `size(map)` | Entry count |
| `isEmpty(map)` | True if empty |
| `mapValues(map, fn)` | Transform values (new map) |
| `filter(map, fn)` | Keep matching entries (new map) |
| `forEach(map, fn)` | Side-effect iteration |
| `toMutable(map)` | Convert to `MutableMap<K,V>` |

---

## Complete Example

```kotlin
// Interfaces
interface Shape {
    fun area(): Double
    fun describe(): String
}

// Data classes
data class Circle(r: Double)
data class Rect(w: Double, h: Double)

// Implement the interface for each type
implement Shape for Circle {
    fun area(): Double { return 3.14159 * this.r * this.r }
    fun describe(): String { return "circle r=${this.r}" }
}

implement Shape for Rect {
    fun area(): Double { return this.w * this.h }
    fun describe(): String { return "rect ${this.w}x${this.h}" }
}

// Extension function
fun Circle.diameter(): Double { return this.r * 2.0 }

// when expression
fun classify(s: Shape): String {
    return when {
        s.area() > 100.0 -> "large"
        s.area() > 10.0  -> "medium"
        else             -> "small"
    }
}

fun main() {
    val shapes: List<Shape> = [Circle(5.0), Rect(3.0, 4.0), Circle(1.0)]

    for (s in shapes) {
        val size = classify(s)
        println("${s.describe()} — area=${s.area()}, size=${size}")
    }

    val c = Circle(7.0)
    println("diameter = ${c.diameter()}")
}
```

---

## Project Workflow

### Create a project

```bash
dotnet new ksr-console -n MyApp
cd MyApp
dotnet run
```

All `*.ksr` files in the project directory are compiled automatically by `dotnet build`.

### Add a NuGet package

```bash
dotnet add package Raylib-cs
```

Then use it in your `.ksr` files:

```kotlin
use Raylib_cs

fun main() {
    Raylib.initWindow(800, 600, "My Game")
    while (!Raylib.windowShouldClose()) {
        Raylib.beginDrawing()
        Raylib.clearBackground(Color.black)
        Raylib.drawText("Hello, KSR!", 200, 250, 40, Color.white)
        Raylib.endDrawing()
    }
    Raylib.closeWindow()
}
```

### Build and publish

```bash
dotnet build
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true
```

### Project file

A KSR project is a standard `.csproj` using the KSR SDK:

```xml
<Project Sdk="KSR.Sdk/0.1.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
</Project>
```

That's it. No boilerplate, no extra build steps.

---

## Single-File Mode

No project needed — run a `.ksr` file directly like a script.

```bash
ksr hello.ksr
ksr hello.ksr --debug                  # also prints the generated C# source
ksr hello.ksr --async-return=valuetask # use ValueTask for all async functions
```

---

## Examples

The `examples/` directory contains runnable `.ksr` files:

| File | Description |
|---|---|
| `examples/hello.ksr` | Data classes, extension functions, control flow |
| `examples/stdlib_demo.ksr` | `ksr.io` and `ksr.text` standard library demo |
| `examples/async_demo.ksr` | async/await with Task and ValueTask |
| `examples/collections_demo.ksr` | `ksr.collections`, fluent and static API, immutable/mutable List and Map |
| `examples/generic_funs.ksr` | Generic type parameters on functions and extension functions |
| `examples/text_processing.ksr` | Text parsing + collection pipelines using `ksr.text` and `ksr.collections` together |
| `examples/raylib_demo.ksr` | Raylib primitives demo (circles, rectangles, lines) |
| `examples/game_of_life.ksr` | Conway's Game of Life at 1920×1080 using Raylib |

Run any example:

```bash
ksr examples/hello.ksr
```

For the Raylib examples, install the package first:

```bash
dotnet add package Raylib-cs
ksr examples/game_of_life.ksr
```

---

## VS Code Extension

The `ksr-lang` extension provides:

- **Syntax highlighting** — keywords, types, strings, operators, lambdas
- **Real-time diagnostics** — parse errors shown inline as you type (powered by the LSP server)
- **Completions** — keywords (`val`, `var`, `fun`, `async`, `await`, …), built-in types (`Int`, `String`, `List`, `MutableList`, …), stdlib symbols (`IO`, `File`, `Text`, `Lst`, `Mp`, …), stdlib module names (`ksr.io`, `ksr.text`, `ksr.collections`), data class names, interface names, and top-level function names from the current file
- **Hover documentation** — describes keywords, built-in types, stdlib symbols (`Lst`, `Mp`, `IO`, …), and identifiers on hover

The extension connects to the KSR Language Server (`ksr lsp`) via JSON-RPC over stdio using the standard Language Server Protocol. It works with VS Code and any other LSP-compatible editor.

Installed automatically by the installer scripts. After installation, reload VS Code (`Ctrl+Shift+P` → **Reload Window**) to activate the Language Server.

To install manually:

```bash
code --install-extension vscode-extension/ksr-lang-0.1.0.vsix
```

If the Language Server fails to start, verify `ksr` is on your PATH:

```bash
ksr lsp   # should hang waiting for input, not print an error
```

---

## How It Works

KSR is a **source-to-source compiler**: `.ksr` → C# → .NET assembly.

```
.ksr source
    └── Lexer         → tokens
    └── Parser        → AST  (with source positions)
    └── CodeGenerator → C# source  (with #line directives)
    └── Roslyn        → .NET assembly
```

The compiler hooks into `dotnet build` via a custom MSBuild task (`KSR.Build`), so `.ksr` files compile transparently alongside any other `.cs` files in your project.

### Source mapping

The compiler embeds `#line` directives in the generated C# so that every error — whether a compile-time type error or a runtime exception — is reported against your `.ksr` file and line number, not the generated C#:

```
error KSR001: hello.ksr(42,5): undefined variable 'naem'
```

This works in both single-file mode (`ksr file.ksr`) and full project mode (`dotnet build`).

---

## NuGet Packages

| Package | Purpose |
|---|---|
| `KSR` | Global CLI — `ksr <file.ksr>` single-file runner |
| `KSR.Core` | Compiler library — Lexer, Parser, AST, CodeGen |
| `KSR.Build` | MSBuild task — hooks KSR into `dotnet build` |
| `KSR.Sdk` | MSBuild SDK — `Sdk="KSR.Sdk/0.1.0"` |
| `KSR.StdLib` | Standard library — `ksr.io`, `ksr.text`, and `ksr.collections` modules |
| `KSR.Templates` | `dotnet new` templates |

---

## Type Reference

| KSR | C# | Notes |
|---|---|---|
| `Int` | `int` | 32-bit integer |
| `Long` | `long` | 64-bit integer |
| `Double` | `double` | 64-bit float |
| `Float` | `float` | 32-bit float |
| `Bool` | `bool` | |
| `String` | `string` | |
| `Unit` | `void` | return type only |
| `T?` | `T?` | nullable |
| `T[]` | `T[]` | array |
| `List<T>` | `IReadOnlyList<T>` | immutable list |
| `MutableList<T>` | `List<T>` | mutable list |
| `Map<K,V>` | `IReadOnlyDictionary<K,V>` | immutable map |
| `MutableMap<K,V>` | `Dictionary<K,V>` | mutable map |

---

## Roadmap

- [x] Source mapping — errors point to `.ksr` line numbers (`#line` directives)
- [x] `List<T>` and `Map<K, V>` collection literals
- [x] Interfaces / trait-style polymorphism (`interface` + `implement … for …`)
- [x] Pattern matching — `when` expression (switch expr / ternary / if-else)
- [x] Language server (LSP) — real-time diagnostics, completion, hover (`ksr lsp`)
- [x] Standard library — `ksr.io` (file/console I/O) and `ksr.text` (string utilities)
- [x] Async/await — `async fun`, `await`, `@ValueTask`, `--async-return=valuetask`
- [x] Standard library — `ksr.collections` (`Lst` and `Mp` higher-order operations)
- [x] Immutable collections by default — `List<T>` / `Map<K,V>` are read-only; `MutableList<T>` / `MutableMap<K,V>` are writable
- [x] Generic type parameters on functions — `fun <T> identity(x: T): T` and `fun <T> List<T>.second(): T`

---

## Building from Source

```bash
git clone https://github.com/your-org/ksr_lang
cd ksr_lang
dotnet build KSR.sln
```

Pack all NuGet packages to `artifacts/`:

```bash
dotnet pack KSR.Core.csproj                              -o artifacts/
dotnet pack sdk/KSR.Build/KSR.Build.csproj               -o artifacts/
dotnet pack sdk/KSR.Sdk/KSR.Sdk.csproj                   -o artifacts/
dotnet pack sdk/KSR.StdLib/KSR.StdLib.csproj             -o artifacts/
dotnet pack sdk/KSR.Templates/KSR.Templates.csproj       -o artifacts/
dotnet pack KSR.csproj                                   -o artifacts/
```

Or just run the installer script which does all of this automatically.

The compiler and toolchain is ~3 500 lines of C# across six layers:

```
Lexer/             tokeniser
Parser/            recursive-descent parser + semantic validation
AST/               node definitions (C# records)
CodeGen/           C# emitter (async/await, interfaces, when) + Roslyn in-memory runner
LspServer.cs       Language Server Protocol (JSON-RPC over stdio)
sdk/KSR.StdLib/    standard library (ksr.io, ksr.text)
```

---

## License

MIT — see [LICENSE.txt](LICENSE.txt).
