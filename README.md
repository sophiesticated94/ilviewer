# IL Viewer

VS Code extension for exploring Intermediate Language generated for .NET source code.

## Usage

1. Open a C#, F# or VB.NET project.
2. Run `IL Viewer: Otwórz podgląd`.
3. Use `Przebuduj` in the panel to run `dotnet build`.
4. Move the cursor, hover, or select source code to highlight matching IL.

The panel supports `Fragment`, `Funkcja`, `Klasa`, `Typ + zagnieżdżone`, `Projekt`, and `Aplikacja`. Application context includes the selected project and its project references. For C# and VB, the worker also detects nested source regions such as lambdas, object creations, object initializers, and member initializers, then colors matching IL by nesting depth.

Use `Wyjaśnij IL` to see opcode descriptions, operand kinds, stack behavior, flow control, and resolved signatures for instructions used in the active context.
