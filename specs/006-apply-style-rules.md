# 006-apply-style-rules

Refactor. Make the existing code in `src/` and `tests/` follow `docs/csharp-style.md`. The HTTP behavior and the stored data must not change.

- The build enforces the analyzer rules from `.editorconfig` on this branch. The build must pass with no warnings.
- Enum members are PascalCase. `Currency` becomes `Eur`, `Gbp`, `Usd`.
- The wire values do not change. `"EUR"`, `"eur"` and `"Eur"` are still accepted (case-insensitive). The response still contains `"status": "Pending"`.
- The stored values do not change. The `Currency` column still contains the ISO code (`"EUR"`), not the member name.
- Apply the rules that only the reviewer checks: sealed classes (including entities), primary constructors, raw string literals for JSON in tests, records and `required` members where the rules say.

Acceptance:
- All existing tests pass. Tests change only to follow the new names and the style rules. They test the same behavior.
- An integration test shows that a payment with currency `"EUR"` is stored with the column value `"EUR"`.
