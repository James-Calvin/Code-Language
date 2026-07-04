# Code Editor Support

This directory contains editor-facing assets for the Code language.

## TextMate Grammar

`textmate/code.tmLanguage.json` is the reusable lexical grammar for `.code`
files. VS Code consumes this grammar directly, and other editors can reuse or
convert it.

The grammar is intentionally lexical. It highlights source structure, but it
does not type-check, resolve imports, report diagnostics, format code, or
provide completions.

## VS Code

The VS Code wrapper lives in `vscode/`. Release builds produce:

```text
artifacts/release/code-language-vscode-<version>.vsix
```

Manual install:

```powershell
code --install-extension artifacts/release/code-language-vscode-<version>.vsix
```

Local maintainer install:

```powershell
./scripts/install-local.ps1 -SkipTests -InstallVsCodeExtension
```

Public Windows installer opt-in:

```powershell
iex "& { $(irm https://raw.githubusercontent.com/James-Calvin/Code-Language/master/install.ps1) } -InstallVsCodeExtension"
```

If the `code` command is not available, the installer prints the manual command
instead of failing the compiler install.

## Tests

Run the dependency-free editor syntax checks with:

```powershell
node scripts/test-editor-syntax.mjs
```

The check validates the grammar, VS Code extension metadata, the syntax fixture,
and keyword drift against `ConsoleApp1/Compiler/Lexer.cs`.
