---
description: 'C# code style and patterns for this workshop repo'
applyTo: '**/*.cs'
---

# C# Style — PizzaBot Workshop

## Language Version
Target C# 13+ features. Use modern syntax throughout — records, pattern matching, switch expressions, primary constructors, collection expressions.

## File Structure
- Top-level statements in `Program.cs` — no `Main` method
- File-scoped namespace declarations
- Single class or type per file
- Remove unused `using` directives

## Nullability
- Nullable reference types are enabled across all projects
- Declare variables non-nullable unless null is a meaningful value
- Use `is null` / `is not null` — never `== null` / `!= null`
- Add null-coalescing throws at config boundaries (see existing `?? throw new InvalidOperationException(...)` pattern)

## Naming
- PascalCase: classes, methods, properties, public fields
- camelCase: local variables, parameters, private fields
- Prefix interfaces with `I` (e.g., `IOrderService`)
- `nameof()` over string literals when referencing member names

## Async
- `async`/`await` throughout — never `.Result` or `.Wait()`
- Name async methods with `Async` suffix
- `ConfigureAwait(false)` is not needed in console app / top-level contexts

## Comments
- Comment the *why*, not the *what*
- Teaching comments are valuable in workshop code — use them to explain SDK concepts, tool types, and conversation flow
- XML doc comments on any public API in `Shared/`
- Don't comment obvious code

## Workshop-Specific Rules
- Keep `Program.cs` linear and readable — the flow from top to bottom should tell the story of the agent being built
- Prefer inline lambdas and local functions over separate helper classes when it keeps the story in one file
- When the SDK requires verbosity (e.g., `BinaryData.FromObjectAsJson`), don't hide it — surface it with a comment explaining what's happening
- `#pragma warning disable OPENAI001` is expected at the top of files using preview OpenAI APIs — leave it, don't suppress differently
