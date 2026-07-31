# GostEditor

GostEditor is a desktop editor for preparing academic and technical documents
according to GOST-oriented formatting rules. It is built with .NET 8 and
Avalonia UI and uses its own document model, text engine and paged layout.

## Requirements

- .NET 8 SDK

## Build and run

```bash
dotnet restore GostEditor.sln
dotnet build GostEditor.sln
dotnet run --project GostEditor.UI/GostEditor.UI.csproj
```

## Tests

Run the complete test suite from the repository root:

```bash
dotnet test
```

The test project covers critical text editing operations, `.gost` archive
round-tripping and DOCX package generation.

## Branch workflow

- `develop` contains integrated and verified changes.
- New work is developed in `feature/**` branches.
- Every push to `develop` or `feature/**`, and every pull request targeting
  `develop`, runs restore, build and tests in GitHub Actions.
