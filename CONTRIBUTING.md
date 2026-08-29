# Contributing to PlaceContext

Thanks for helping improve PlaceContext. Bug reports, documentation fixes, tests, and focused code
changes are all welcome.

## Before opening a change

1. Search existing issues and pull requests to avoid duplicate work.
2. Open an issue before a large architectural change so the approach can be discussed.
3. Keep changes focused; avoid mixing unrelated refactors into a bug fix.
4. Never commit credentials, private deployment addresses, customer data, or generated artifacts.

## Development setup

Install the .NET 10 SDK, Docker, and PostgreSQL, then run:

```bash
./setup.sh
./run.sh
```

The portal is served at `http://localhost:7700` and the MCP endpoint is `/mcp`.

## Checks

Run the checks relevant to your change before opening a pull request:

```bash
dotnet build PlaceContext.slnx
dotnet test PlaceContext.slnx
```

Add regression coverage for behavior changes. Update the embedded wiki under
`src/PlaceContext.Host/Wiki/` when user-facing behavior changes.

## Pull requests

Explain the problem and outcome, list the checks you ran, and call out migrations, configuration
changes, or compatibility concerns. By contributing, you agree that your contribution is licensed
under the repository's MIT License.

Pull requests require approval from a code owner before merge. Maintainers decide whether and when
a contribution is accepted; opening an issue or pull request does not guarantee inclusion. Direct
pushes, force pushes, and branch deletion are disabled on `main`.
