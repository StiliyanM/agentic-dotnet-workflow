# C# style rules

These rules apply to all code in `src/` and `tests/`. The test-writer and the implementer follow them. The reviewer checks them.

The language version is C# 14 (.NET 10). Use the modern form of a feature when one exists.

**Enforced** means that `.editorconfig` makes the build fail on a violation. The reviewer checks all other rules.

## Language features

| Rule | Enforced |
|---|---|
| File-scoped namespaces (`namespace X;`). | Yes (IDE0161) |
| Primary constructors for classes that only store their constructor parameters, for example DI services and entities. Use the parameter directly. Do not copy it to a field unless the field must be `readonly` for a reason. | Yes (IDE0290) |
| Collection expressions: `[]`, `[a, b]`, `[.. items]`. | Yes (IDE0300–IDE0305, IDE0028) |
| Target-typed `new()` when the type is clear from the left side. | Yes (IDE0090) |
| `var` for local variables. | Yes (IDE0007) |
| Pattern matching: `is null`, `is not null`, `is T t`, switch expressions, property patterns. | Yes (IDE0019, IDE0020, IDE0041, IDE0066, IDE0078, IDE0083) |
| `using var x = ...;` (simple `using` statement) instead of a `using` block when the scope is the rest of the method. | Yes (IDE0063) |
| Auto-properties. When a property needs logic, use the C# 14 `field` keyword, not a separate backing field. | Auto-properties: yes (IDE0032). `field`: reviewer |
| Records for contracts and other data-only types. `required` and `init` for properties that must be set. | Reviewer |
| Raw string literals (`"""`) for multi-line strings and for JSON in tests. Interpolated raw strings (`$"""`) when values are inserted. | Reviewer |
| Guard clauses with the built-in throw helpers: `ArgumentNullException.ThrowIfNull`, `ArgumentOutOfRangeException.ThrowIfNegativeOrZero`, `ArgumentException.ThrowIfNullOrEmpty`. | Reviewer (CA1062 enforces the null check) |
| Expression-bodied members for members that are one expression. | Reviewer |
| Braces on all `if`, `else`, `for`, `foreach`, `while` blocks. | Yes (IDE0011) |
| No unused `using` directives. | Yes (IDE0005) |

## Types

- Classes are `sealed` unless a spec needs inheritance. This includes EF Core entities. EF Core does not need proxies here.
- One public type per file. The file name is the type name.
- Nullable reference types are on. Do not use `!` (null-forgiving) unless a comment says why the value cannot be null.

## Naming

| Element | Style | Example | Enforced |
|---|---|---|---|
| Types, methods, properties, events, constants | PascalCase | `CreatePaymentUseCase`, `MaxAmount` | Yes (IDE1006) |
| Interfaces | `I` + PascalCase | `IPaymentRepository` | Yes (IDE1006) |
| Type parameters | `T` + PascalCase | `TEnum` | Yes (IDE1006) |
| Private instance fields | `_camelCase` | `_fixture` | Yes (IDE1006) |
| Parameters and locals | camelCase | `cancellationToken` | Yes (IDE1006) |
| Enum members | PascalCase. Acronyms with 3 or more letters are PascalCase too (`Eur`, `Usd`, `Html`). Only 2-letter acronyms stay upper case (`IO`). | `Currency.Eur` | Reviewer (the analyzer checks only the first letter) |
| Async methods | `Async` suffix | `ExecuteAsync` | Reviewer |

When a wire value or a stored value must differ from the member name (for example the ISO code `"EUR"` for `Currency.Eur`), map it explicitly in the layer that owns the format: JSON in Api, the database in Infrastructure. Do not rename members to match an external format.

## Async

- `CancellationToken` is the last parameter. Pass it to every call that accepts it.
- Do not use `.Result`, `.Wait()` or `async void`.

## Comments

- Comments explain why, not what. Do not use `#region`.
