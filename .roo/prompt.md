# Roo Development Rules for Illustra Project

## 1. Adhere to Project Coding Standards
- **Always** consult `docs/Rule.md` for project-specific coding standards, patterns, and implementation details before making changes.
- Pay close attention to UI implementation rules (multi-language support, dialog design), data handling (settings, database), asynchronous processing, and system integration guidelines outlined in `docs/Rule.md`.

## 2. Ensure Build Success After Edits
- **After every file modification** (using `apply_diff`, `write_to_file`, `insert_content`), immediately run `dotnet build Illustra.sln` to check for errors.
- **Do not proceed** with further steps or attempt completion if the build fails.
- Fix any reported build errors promptly. Refer to the "Troubleshooting" section in `docs/Rule.md` for common issues and solutions.

## 3. Verify Tool Output for Special Characters
- When using `write_to_file` or `insert_content` with content containing XML/XAML tags or C# code:
    - **Carefully review** the content you are providing to the tool.
    - Ensure that special characters like `<`, `>`, and `&amp;` are **not** unnecessarily escaped (e.g., `&amp;lt;`, `&amp;gt;`, `&amp;amp;`).
    - Pay special attention to C# logical operators like `&&` which might be incorrectly escaped to `&amp;&amp;`.
- If a build fails due to parsing errors (like `XamlParseException`) or C# syntax errors after using these tools, immediately suspect incorrect character escaping and correct the relevant file.

## 4. Follow General Best Practices
- Use DI (Dependency Injection) where appropriate, following the project's established patterns (e.g., using `ContainerLocator` or constructor injection as seen in existing code).
- Ensure UI updates are performed on the UI thread using `Dispatcher.InvokeAsync`.
- Handle potential `null` values appropriately.
