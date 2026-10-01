# 004-layered-structure

Refactor. Move the code from spec 001 into the layers in `docs/architecture.md`. The HTTP behavior of `POST /payments` must not change.

- Make the projects `ApmPlayground.Domain`, `ApmPlayground.Application` and `ApmPlayground.Infrastructure`, and add them to the solution. Set the project references as `docs/architecture.md` says.
- Move each type to its layer. `Payment` and its enums go to Domain. `AppDbContext` and the EF Core configuration go to Infrastructure. Create a use case for payment creation in Application.
- Replace the static validator with a FluentValidation validator in Application. The rules and the error messages do not change.
- Keep the request and response contracts as they are now. Spec 005 changes them.

Acceptance:
- The integration tests from spec 001 do not change, and they pass.
- The unit tests change only to follow the new projects, namespaces and the FluentValidation validator. They test the same rules and messages.
