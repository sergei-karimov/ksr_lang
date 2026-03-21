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
```

### Functions

```kotlin
fun add(a: Int, b: Int): Int {
    return a + b
}

fun greet(name: String) {
    println("Hello, ${name}!")
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

### Collections

```kotlin
val nums: List<Int> = [1, 2, 3]
val empty: List<String> = []

val scores: Map<String, Int> = ["alice": 10, "bob": 7]
```

### Float Literals

```kotlin
val pi = 3.14159
val e: Double = 2.71828
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

---

## Complete Example

```kotlin
data class Point(x: Int, y: Int)

fun Point.distanceSq(other: Point): Int {
    val dx = this.x - other.x
    val dy = this.y - other.y
    return dx * dx + dy * dy
}

fun sumRange(from: Int, to: Int): Int {
    var sum = 0
    for (i in from..to) {
        sum += i
    }
    return sum
}

fun main() {
    val p1 = Point(0, 0)
    val p2 = Point(3, 4)
    println("Squared distance: ${p1.distanceSq(p2)}")
    println("Sum 1..10 = ${sumRange(1, 10)}")
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
ksr hello.ksr --debug    # also prints the generated C# source
```

---

## Examples

The `examples/` directory contains runnable `.ksr` files:

| File | Description |
|---|---|
| `examples/hello.ksr` | Data classes, extension functions, control flow |
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
- **Live diagnostics** — parse errors shown inline as you type

Installed automatically by the installer. To install manually:

```bash
code --install-extension vscode-extension/ksr-lang-0.1.0.vsix
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
| `List<T>` | `List<T>` | mutable list |
| `Map<K,V>` | `Dictionary<K,V>` | mutable map |

---

## Roadmap

- [x] Source mapping — errors point to `.ksr` line numbers (`#line` directives)
- [x] `List<T>` and `Map<K, V>` collection literals
- [x] Interfaces / trait-style polymorphism (`interface` + `implement … for …`)
- [ ] Pattern matching (`when` expression)
- [ ] Coroutines / async-await
- [ ] Standard library (`ksr.io`, `ksr.collections`)
- [ ] Language server (LSP) for full IDE support

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
dotnet pack sdk/KSR.Templates/KSR.Templates.csproj       -o artifacts/
dotnet pack KSR.csproj                                   -o artifacts/
```

Or just run the installer script which does all of this automatically.

The compiler is ~1 700 lines of C# across four layers:

```
Lexer/      tokeniser
Parser/     recursive-descent parser
AST/        node definitions (C# records)
CodeGen/    C# emitter + Roslyn in-memory runner
```

---

## License

MIT — see [LICENSE.txt](LICENSE.txt).
