# ADR-0001: Initial language, runtime, and testing stack

Status: Accepted
Created: 2026-09-23
Accepted: 2026-09-25 (NUnit initially confirmed 2026-09-23)

## Context

HomeVault is a fresh C#/.NET restart. The local machine has SDK 10.0.300. The user requested discussion of the stack before implementation.

## Decision

Use C# with .NET 10 LTS, nullable reference types, and the stable language version supplied by the target framework. Pin the SDK in global.json. Use NUnit and its own assertions with the specific versions recorded below. Use a console Playground before selecting a UI. Avoid additional packages until justified.

## Alternatives

.NET 8/9 have shorter remaining support; preview runtimes add unnecessary change. xUnit and MSTest are alternatives to NUnit. NUnit replaces the earlier xUnit proposal because the user explicitly selected it. A separate assertion library is unnecessary initially.

## Consequences

The installed SDK supports starting immediately. An LTS baseline reduces upgrade pressure. NUnit adds a test-only dependency. Database, ORM, UI, and hosting remain open decisions.

## Discussion and confirmation

On 2026-09-23, the user explicitly confirmed replacing the proposed xUnit choice with NUnit and requested that confirmation be recorded here. That initial approval covered only the framework. On 2026-09-25, the owner accepted the full concrete specification below and authorized HV-03 scaffolding.

## References

https://dotnet.microsoft.com/en-us/platform/support/policy

## HV-02 accepted specification (2026-09-25)

Refines [HV-02 / #6](https://github.com/mani8785/HomeVault/issues/6).
The owner accepted these choices on 2026-09-25.

| Setting | Accepted value | Reason |
| --- | --- | --- |
| Target framework | `net10.0` in all five projects | One runtime baseline |
| SDK | `10.0.300`, `rollForward: disable`, `allowPrerelease: false` in root `global.json` | Installed locally; identical SDK selection in CI |
| Language | Omit `LangVersion`; target framework default (C# 14) | Avoid machine-dependent latest or preview features |
| Compiler defaults | `Nullable=enable`, `ImplicitUsings=enable` | Consistent checks and SDK conventions |
| Framework | `NUnit` 4.6.1 | Confirmed framework with a concrete version |
| Adapter | `NUnit3TestAdapter` 6.3.0 | NUnit discovery through VSTest |
| Test host | `Microsoft.NET.Test.Sdk` 18.10.0 | Preserve existing dotnet test and TRX reporting |

Use explicit, non-floating package versions in the test project with
`PrivateAssets=all`; production projects have no test packages. Set
`IsTestProject=true` and `IsPackable=false`. Keep VSTest as the runner; do not
opt into NUnit executable/Microsoft.Testing.Platform mode. Use NUnit assertions
without another assertion or mocking package.

Production projects enable `GenerateDocumentationFile=true`. Treat compiler
warnings as errors, including CS1591 (missing public XML comments) and malformed
XML documentation warnings; do not suppress them globally. Tests do not generate
XML documentation. Review comment meaning manually under the existing workflow.

Exact SDK pinning requires reviewed servicing updates to global.json followed by
restore, format, Release build, and tests. Package sources confirm availability
and framework support. HV-03 successfully restored and built this combination and executed seven NUnit tests with a TRX report. Future version changes require the same validation.

### Sources checked for this decision

- [SDK selection and roll-forward](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json)
- [C# language defaults](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/configure-language-version)
- [NUnit 4.6.1](https://www.nuget.org/packages/NUnit/4.6.1)
- [NUnit3TestAdapter 6.3.0](https://www.nuget.org/packages/NUnit3TestAdapter/6.3.0)
- [Microsoft.NET.Test.Sdk 18.10.0](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.10.0)

## Full acceptance and implementation authorization

On 2026-09-25, the owner explicitly accepted the concrete HV-02 specification and authorized HV-03 scaffolding. This supersedes the earlier pending-confirmation statements above. Delivery must use a feature branch and pull request; merging remains separately gated. The package combination is validated during HV-03 by restore, build, and actual NUnit execution.
