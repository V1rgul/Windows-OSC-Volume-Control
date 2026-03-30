# Agent / contributor notes

- **Cursor agents:** project conventions live in **`.cursor/rules/*.mdc`** (loaded automatically). Start with `csharp-project-style.mdc`.
- **IDE analyzers:** root **`.editorconfig`** aligns ReSharper/Rider and .NET naming diagnostics with the project’s intentional non-Microsoft C# style.
- **Vendor subtree:** treat **`src/SharpOSC/`** as read-only unless an integration fix requires a minimal change.
