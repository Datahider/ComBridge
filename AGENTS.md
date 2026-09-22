# AGENTS.md

ComBridge is a small Windows-only desktop automation bridge built with C# and .NET 10.

Development follows documentation-first TDD: update `README.md`, add failing tests, then implement.
The HTTP server must bind to loopback by default. Never add network exposure, compatibility fallbacks,
clipboard-based typing, or protected-desktop workarounds without explicit approval.

Use PascalCase for classes, camelCase for methods, and snake_case for properties and local variables.
Run `dotnet test` and `scripts/build-linux.sh` before declaring a release ready.

