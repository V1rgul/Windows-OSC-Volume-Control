## C# style of project

This codebase **intentionally does not follow** usual public-API PascalCase rules. Match existing files; do not “fix” names to Microsoft style.

### Naming

- **Types** (classes, structs, enums, delegates): **PascalCase** (`ConfigStore`, `AppConfigDiskOutcome`).
- **Enum members** and **`const`**: **UPPER_SNAKE_CASE** (`NONE`, `MIN_HEIGHT_DIP`).
- **Methods** (including `public`): **camelCase** when not forbidden below (`loadFromDisk`, `tryPersistToDisk`).
- **Properties, locals, parameters**: **camelCase** (`appConfig`, `endPoint`, `name`).
- **Instance fields** (non-const): **`_camelCase`** with a leading underscore (`_configStore`).

### Do not rename (language / framework)

Keep required shapes: **`Main`**, **`override`** members matching the base, **`Dispose` / `Dispose(bool)`** for `IDisposable`, WinForms **`Control.Name`** and designer event handler wiring, **`[GeneratedRegex]`** partials, P/Invoke declarations, and any name the runtime or BCL requires.

### Refactors

Avoid naive substring renames: e.g. `t.Name` → `t.name` can corrupt `Port.Name`; `.Osd` can corrupt `OsdMeasureSample`. Prefer **whole-symbol** or **compiler-driven** fixes.

### Tooling

Root **`.editorconfig`** turns off ReSharper/Rider **Inconsistent Naming** and relaxes **IDE1006**, **CA1707**, **CA1715** so the custom style does not spam warnings. Prefer extending `.editorconfig` if new analyzers conflict.
