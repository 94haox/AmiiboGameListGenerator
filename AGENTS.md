# Repository Guidelines

## Project Structure & Module Organization

- `AmiiboGameList/`: .NET 8 console app source (`.csproj`, `.sln`).
- `AmiiboGameList/ConsoleClasses/`: platform-specific scraping/parsing helpers (Switch, Switch2, WiiU, 3DS).
- `AmiiboGameList/resources/` + `AmiiboGameList/Properties/Resources.resx`: embedded assets (e.g., `WiiU.json`).
- `.github/`: CI workflows (build + deterministic output checks).
- Root docs: `README.md` (CLI usage), `LOGIC.md` (behavior notes), migration/planning docs.

## Build, Test, and Development Commands

Run from repo root:

- `dotnet restore AmiiboGameList/AmiiboGameList.csproj`: restore dependencies.
- `dotnet build AmiiboGameList/AmiiboGameList.csproj -c Release`: compile.
- `dotnet run --project AmiiboGameList/AmiiboGameList.csproj -- -h`: show CLI help.
- `dotnet run --project AmiiboGameList/AmiiboGameList.csproj -- -log 0 -p 4 -o ./games_info.json`: generate output.
  - Use `-i ./path/to/amiibo.json` to avoid downloading the Amiibo database.

## Coding Style & Naming Conventions

- C# (.NET 8), file-scoped namespaces, nullable-aware code where applicable.
- Indentation: 4 spaces in `.cs` files.
- Naming: `PascalCase` types/methods, `camelCase` locals/parameters, `Async` suffix for async APIs.
- Keep changes deterministic: stable ordering and culture-invariant comparisons where output is serialized.

## Testing Guidelines

- No unit test project is currently included. Prefer adding tests only when introducing non-trivial logic.
- Minimum validation for PRs: `dotnet build` + a local generation run.
- CI checks determinism by generating `games_info.json` multiple times; avoid non-deterministic iteration (e.g., unordered dictionaries) in output paths.

## Commit & Pull Request Guidelines

- Commits in history use short, imperative summaries; many use `feat:` prefixes. Follow that pattern when possible (`feat:`, `fix:`, `docs:`).
- PRs should include: what changed, how to run, and whether output changes are expected (attach before/after hashes or a small diff summary).
- Don’t commit generated artifacts like `games_info.json` unless the PR is explicitly updating canonical output in a downstream workflow.

## Security & Configuration Tips

- The generator fetches multiple upstream datasets over the network; prefer reproducible inputs when debugging (`-i` for Amiibo DB, fixed output path, and a chosen `-p`).
