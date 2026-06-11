# Test project notes

Match root [AGENTS.md](../../AGENTS.md) for C# style. One `{TypeOrFeature}Tests.cs` per class; subfolders only when an area has multiple files (`Input/`, `Osc/`).

## Test method names

**`{methodUnderTest}_{when}_{expected}`** — spell `methodUnderTest` as in production (usually camelCase; PascalCase for type-level tests or static helpers like `RoundToBindingDecimals`). No class/type prefix; no `…ForTests` in the name.

Examples: `tryParse_roundTripsCompoundHotkeys`, `resolveKeyUpTargets_strictCandidateWins`, `loadAppConfigFromKeyValueText_parsesHotkeyGlobalsAndLongPress`.
