# Architecture rules

These rules apply to all specs. The planner and the implementer follow them. The reviewer checks them.

## Layers are required

The layer structure below is a required rule. It is not a KISS or YAGNI violation, and the reviewer must not flag it as one. KISS and YAGNI apply inside each layer: do not add a type, an interface or a library that the current spec does not need.

## Projects and dependencies

| Project | Contains | Can reference |
|---|---|---|
| `src/AgenticPayments.Domain` | Entities, value objects, domain enums, domain rules (invariants). | Nothing. No NuGet packages. |
| `src/AgenticPayments.Application` | Use cases (services), request and response contracts, FluentValidation validators, interfaces for persistence and other external services (ports). DI extension `AddApplication()`. | Domain. FluentValidation packages. |
| `src/AgenticPayments.Infrastructure` | `AppDbContext`, EF Core entity configurations (`IEntityTypeConfiguration<T>`), implementations of the Application interfaces. DI extension `AddInfrastructure(IConfiguration)`. | Application, Domain. EF Core and Npgsql packages. |
| `src/AgenticPayments.Api` | `Program.cs` (composition root), minimal API endpoints, HTTP mapping (status codes, problem details, JSON options). | Application, Infrastructure. |

- Domain does not know about EF Core, HTTP or JSON. Configure persistence in Infrastructure, not with attributes on entities.
- Application does not reference EF Core. It uses the interfaces that it defines. Infrastructure implements them.
- Endpoints stay thin. They bind the request, call a use case, and map the result to HTTP. Business rules go in Domain or Application.

## Validation

- Use FluentValidation (`AbstractValidator<T>`) in Application for request validation. Register the validators with `AddValidatorsFromAssembly`.
- Validation errors give HTTP 400 as an RFC 7807 validation problem. The `errors` keys are the camelCase field names (for example `amount`).
- Domain invariants (for example "a new payment is Pending") stay in the Domain entities.

## Contracts

- Request and response contracts are records in Application.
- Do not use nullable types in contracts unless a field is optional in the spec. Mark required fields with `required`.
- Use enums, not strings, for closed sets of values (for example currency, payment method, status).
- JSON: enums are strings. Reading is case-insensitive. Numeric enum values are rejected.
- A missing field, or a value that the JSON reader cannot convert, gives the same 400 validation problem as a validator error, with the error under that field's key.

## Not used unless a spec needs it

MediatR, CQRS, AutoMapper, generic repositories, a Unit of Work abstraction on top of EF Core, domain events.

## Tests

- Test each rule at the lowest level that can prove it. Add integration tests for public contracts, component interactions, and complete flows. Do not repeat every unit-test case through HTTP unless the HTTP path adds a distinct risk.
- Unit tests reference Domain and Application only. They test domain rules, use cases and validators. Fakes replace the Application interfaces (ports).
- Integration tests reference Api and use `ApiFactory`. They can use `AppDbContext` from Infrastructure to set up and check data.
