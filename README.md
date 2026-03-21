# KSR

**KSR** is a statically-typed, Kotlin-inspired language that compiles to C# and runs on the .NET runtime.
It is designed to feel like Kotlin — concise, expressive, null-safe — while getting full access to the entire .NET ecosystem out of the box.

---

## Why KSR?

Kotlin is a great language, but targeting the JVM means carrying the JVM.
C# is powerful, but its syntax is verbose compared to modern languages.

KSR sits in the middle: **Kotlin-style syntax, .NET runtime, zero JVM overhead**.

- Write code that looks like Kotlin
- Use any NuGet package — Raylib, ASP.NET, Entity Framework, anything
- Compile to native via `dotnet publish`
- Zero runtime dependency beyond .NET itself

---

## Language Tour

### Variables

```kotlin
val name = "Sergei"        // immutable
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
val u = User("Sergei", 30)
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

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) or newer

### Install the KSR template pack

```bash
dotnet new install KSR.Templates
```

### Create a project

```bash
dotnet new ksr-console -n MyApp
cd MyApp
dotnet run
```

```
Hello from MyApp!
```

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

---

## Single-File Mode

No project needed — run a `.ksr` file directly like a script.

Install the CLI:

```bash
dotnet tool install -g KSR
```

Run a file:

```bash
ksr hello.ksr
ksr hello.ksr --debug    # also prints the generated C# source
```

---

## VS Code Extension

The `ksr-lang` extension provides:

- **Syntax highlighting** — keywords, types, strings, operators, lambdas
- **Live diagnostics** — parse errors shown inline as you type

Install from the `.vsix`:

```bash
code --install-extension ksr-lang-0.1.0.vsix
```

---

## How It Works

KSR is a **source-to-source compiler**: `.ksr` → C# → .NET assembly.

```
.ksr source
    └── Lexer         → tokens
    └── Parser        → AST
    └── CodeGenerator → C# source
    └── Roslyn        → .NET assembly
```

The compiler hooks into `dotnet build` via a custom MSBuild task, so `.ksr` files compile transparently — no extra tools, no extra steps.

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

That's it. All `*.ksr` files in the project are compiled automatically.

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

---

## Roadmap

- [ ] `List<T>` and `Map<K, V>` collection literals
- [ ] Pattern matching (`when` expression)
- [ ] Interfaces / trait-style polymorphism
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
```

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
