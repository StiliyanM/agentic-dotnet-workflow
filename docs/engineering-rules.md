# Engineering rules

These rules apply to all code in `src/`. The planner and the implementer follow them. The reviewer checks them. Each agent file keeps its own duties, inputs, outputs and restrictions.

Other shared rules: `docs/architecture.md` (layers, validation, contracts, tests) and `docs/csharp-style.md` (language and naming).

## Priority

- KISS and YAGNI are the most important rules. If another rule or a pattern conflicts with them, KISS and YAGNI win.
- The layer structure in `docs/architecture.md` is required. KISS and YAGNI apply inside each layer. They do not remove a layer.

## Principles

- Also use: DRY, SOLID, Law of Demeter, composition over inheritance, and basic OOP (encapsulation, abstraction, polymorphism).

## Design patterns

- Design patterns you can use: factory method, builder, singleton, decorator, facade, strategy, observer, state machine.
- Use a pattern only when the code has a real problem that the pattern solves. Do not add a pattern to show that you know it.
- Make a singleton with the DI container, not with a static instance.

## Dependencies between components

- Make a dependency on shared state explicit in the signature or in a comment at the call site. Examples: a save that depends on an entity that the same `DbContext` tracks, or two writes that must be in one transaction.
- One component owns each atomic operation (for example, "record the event and change the status"). Do not split one atomic operation across components that save separately.

Evidence: spec 002, reviewer optional item 2 (`WebhookEventRepository.TryRecordAsync` saved the payment only because the same `DbContext` tracked it).

## Constraints

- PostgreSQL through EF Core is the only storage.
- Keep the code small. Write and plan only what the spec and the tests need.
