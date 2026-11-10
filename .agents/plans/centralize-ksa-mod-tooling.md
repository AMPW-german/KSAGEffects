# Centralize Shared KSA Mod Tooling into KSAModTools

Consolidate duplicated MSBuild configuration, KSA/StarMap/NuGet assembly resolution, packaging targets, Git hooks, and CI/release pipelines into a public, author-neutral `AMPW-german/KSAModTools` repository consumed as a Git submodule across KSA mod repositories.

## Summary

Across `KSAGEffects`, `ShaderExtensions`, `KittenExtensions`, `KSARS`, and `MPFX`, build and release infrastructure is heavily duplicated:

- Every mod project maintains its own copy of `KSADLLs.props`, `CopyOutput.targets`, `SetVersionMetadata` in its `.csproj`, and ~55 lines of identical SDK/compiler `<PropertyGroup>` boilerplate.
- `KSADLLs.props` has drifted (`KSARS` includes `Bepu*.dll` via `$(KSADir)`, `KSAGEffects` hardcodes `..\..\..\..\..\..\Program Files\Kitten Space Agency\Bepu*.dll`, and `ShaderExtensions`/`MPFX` carry unused `<ModFile>` items).
- Local `$(KSADir)` and `$(KSAModsDir)` paths are only defined in untracked `.csproj.user` files, so fresh clones or CLI builds fail unless parameters or `.user` files are manually recreated.
- Each repository duplicates a ~350–460 line `.github/workflows/build.yml` with inline Python release-asset downloaders, GitHub App token minting for the private `KSADLLs` repo, `VersionPrefix` detection, GitHub release creation/upload, and SpaceDock publishing.
- `KSADLLs` currently commits ~30 `Brutal.*.dll` files alongside `KSA.dll` and `Planet*.dll`, even though `Ahwoo.BRUTAL.*` (as well as `BepuPhysics`/`BepuUtilities` and `Samboy063.Tomlet`) are now published on NuGet.org.

The target architecture introduces `AMPW-german/KSAModTools`, mounted as a submodule at `tooling/` in each consumer repository, designed to be reusable by any KSA mod author:

1. **Author-neutral shared MSBuild configuration (`msbuild/KsaMod.props` and `msbuild/KsaMod.targets`)**:
   - Provides SDK/compiler defaults (`Debug` and `Release` only; project-specific configurations like `PerfDebug` and identity fields like `Company` and `Authors` stay in each consumer `.csproj`).
   - **Local builds default to the local KSA installation (`$(KSADir)`)** (`UseKsaNuGetPackages=false` by default locally), linking `Brutal*.dll`, `Bepu*.dll`, `KSA.dll`, `Planet*.dll`, and `Tomlet.dll` directly from `$(KSADir)` (with optional opt-in `UseKsaNuGetPackages=true` resolved against a local clone of the KSA DLL repo).
   - **CI builds (`UseKsaNuGetPackages=true`)** consume the single unified version manifest (`ksa-versions.csv`) and generated `KsaNuGet.props` stored inside the **KSA DLL repository** (`KSADLLs`) alongside `KSA.dll` and `Planet*.dll` — so KSA version updates only ever touch `KSADLLs`, never forcing a commit in `KSAModTools`.
   - `Lib.Harmony` version is synced from `StarMapLoader/StarMap`'s `main` branch (`Directory.Packages.props`).
   - Provides local `KSADir`/`KSAModsDir` fallbacks, `StarMap.API` reference, `SetVersionMetadata`, and `mod.toml` + `Release/$(ModName)/` packaging.
2. **Composite GitHub Actions (`ci/build-package/action.yml` and `ci/release/action.yml`)** inside the `tooling/` submodule:
   - Single source of version truth (submodule commit pin governs both MSBuild and CI).
   - Resolves `<ModName>`, `<AssemblyName>`, and `<VersionPrefix>` directly from `csproj_path` via `dotnet msbuild -getProperty:...` (no redundant `mod_name` workflow input).
   - Accepts the KSA DLL provider repository per project.
3. **Public GitHub App + automatic installation discovery**:
   - A dedicated public GitHub App (`KSAModTools` updater app) can be installed on any mod repository.
   - On push to `KSAModTools` `main`, the workflow in `KSAModTools` queries `GET /app/installations` and `GET /installation/repositories` across all installations of the public GitHub App and directly creates/updates the `chore/bump-tooling` branch and opens + queues squash auto-merge on a PR targeting each repository's default branch.
   - Locally, `tooling/githooks/post-merge` (and `post-checkout`) syncs `tooling/` on `git pull` / branch switch, with support for a local override (via MSBuild property `<ToolingRef>` in `.csproj` / `.csproj.user` or git config `submodule.tooling.override`) to pin or track a custom branch/commit when testing tooling changes.

## Decisions

- **Tooling repository**: `AMPW-german/KSAModTools` on GitHub, cloned locally at `C:\Users\AMPW\source\repos\KSAModTools` and mounted at `tooling/` in each consumer repository.
- **Project-specific vs. shared MSBuild properties**:
  - `Company`, `Authors`, `Copyright`, `Description`, `ModName`, `VersionPrefix`, `AssemblyVersion`, and custom configurations such as `PerfDebug` remain strictly in each project's `.csproj`.
  - Shared props only define universal KSA mod defaults (`TargetFramework`, `LangVersion`, `ImplicitUsings`, `Nullable`, `AllowUnsafeBlocks`, `Debug`/`Release` property groups, etc.).
- **KSA version snapshot and NuGet manifest live in the KSA DLL repo (`KSADLLs`), not in `KSAModTools`**:
  - NuGet package versions (`Ahwoo.BRUTAL.*`, `BepuPhysics`, `BepuUtilities`, `Samboy063.Tomlet`, and `Lib.Harmony`) are tied to a specific KSA + StarMap snapshot, which is already the exact role of the KSA DLL repo. Keeping them in `KSADLLs` avoids forcing commits/PRs in `KSAModTools` every time KSA updates.
  - Replace `KSA_VERSION.txt` with a single unified `ksa-versions.csv` in `KSADLLs` recording every assembly/package (`name,version,is_nuget,package_id`), including `KSA.dll`, `Planet.Core.dll`, `Planet.Render.Core.dll`, all `Brutal.*.dll` -> `Ahwoo.BRUTAL.*`, `Bepu*.dll`, `Tomlet.dll` -> `Samboy063.Tomlet`, and `Lib.Harmony` (fetched from `https://raw.githubusercontent.com/StarMapLoader/StarMap/main/Directory.Packages.props`).
  - Alongside `ksa-versions.csv`, `update_ksa_dlls.py` writes `KsaNuGet.props` into `KSADLLs` so MSBuild can `<Import>` the `<PackageReference>` items directly without needing a pre-restore CSV parser task.
  - `KSADLLs` stores only `KSA.dll`, `Planet.Core.dll`, `Planet.Render.Core.dll`, `ksa-versions.csv`, and `KsaNuGet.props` (dropping all 29 `Brutal.*.dll`, `Bepu*.dll`, and `Tomlet.dll` binaries).
- **Local vs. CI assembly resolution**:
  - **Local default (`UseKsaNuGetPackages=false`)**: References `Brutal*.dll`, `Bepu*.dll`, `KSA.dll`, `Planet*.dll`, and `Tomlet.dll` directly from the local KSA installation (`$(KSADir)`), plus `Lib.Harmony` from NuGet (using `KsaNuGet.props` from `$(KSADllRepoDir)` when available, or a fallback version property).
  - **Optional local NuGet mode (`UseKsaNuGetPackages=true`) & CI (`CI=true`)**: Imports `KsaNuGet.props` from `$(KSADllRepoDir)` (in CI, `deps/ksa/KsaNuGet.props`; locally, resolved from `KSA_DLL_REPO_DIR` env var, gitignored `tooling/local.config.props`, or default sibling `$(MSBuildThisFileDirectory)..\..\..\KSADLLs\`) and only references `KSA.dll` and `Planet*.dll` from `$(KSADir)`.
- **Configuring the local KSA DLL repo path for `update_ksa_dlls.py` and local NuGet builds**:
  - Configured via environment variable `KSA_DLL_REPO_DIR` (and optional `KSA_DIR`) or a gitignored config file (`tooling/.ksa-tools.env` / `tooling/local.config.props`), never hardcoded to a single user's machine.
- **Local submodule sync and branch/commit override**:
  - Handled by automated bump PRs on each repo's remote default branch + `tooling/githooks/post-merge` (and `post-checkout`) locally. No MSBuild fetch target is needed.
  - The hook supports an optional override to a specific branch, tag, or commit hash (or `off` to skip touching `tooling/`), read in precedence order from:
    1. `git config --local submodule.tooling.override` (local, untracked per-clone override),
    2. `<ToolingRef>` property in `.csproj.user` or `.csproj`, or
    3. Default behavior: if no override is set, run `git submodule update --init --recursive` to match the parent commit's recorded gitlink.
  - The hook always checks `git -C tooling status --porcelain` first and refuses to overwrite a dirty `tooling/` working tree.
- **Resolving project metadata in CI**:
  - `ci/build-package` requires only `csproj_path`; it resolves `ModName`, `AssemblyName`, and `VersionPrefix` directly from MSBuild via `dotnet msbuild <csproj_path> -getProperty:ModName,AssemblyName,VersionPrefix`.
- **Two-App security separation**:
  - **Public `KSAModTools` GitHub App**: Installed by any user on their KSA mod repos (`Contents: write`, `Pull requests: write`, `Metadata: read`). Used by `KSAModTools` to discover all installed repos via the GitHub App Installations API and directly push the `chore/bump-tooling` branch + open and auto-merge the bump PR against each repo's default branch.
  - **Private KSA DLL Access**: Remains project-configurable (`ksa_dll_repo` input + project's own DLL-access credentials), so `KSAModTools` never hardcodes a single user's private DLL repository.

---

## 1. `KSAModTools` and `KSADLLs` Contents and Structure

```text
KSAModTools/
├── .github/
│   └── workflows/
│       └── bump-consumers.yml
├── ci/
│   ├── build-package/
│   │   └── action.yml
│   ├── release/
│   │   └── action.yml
│   └── scripts/
│       ├── download_deps.py
│       ├── rewrite_project_refs.py
│       ├── publish_github_release.sh
│       └── publish_spacedock.sh
├── githooks/
│   ├── post-checkout
│   └── post-merge
├── msbuild/
│   ├── KsaMod.props
│   └── KsaMod.targets
├── scripts/
│   └── update_ksa_dlls.py
├── .gitignore               # ignores .ksa-tools.env and local.config.props
└── README.md
```

```text
KSADLLs/ (Private KSA DLL snapshot repository)
├── KSA.dll
├── Planet.Core.dll
├── Planet.Render.Core.dll
├── ksa-versions.csv         # Single version manifest (KSA, Planet.*, Brutal.*, Bepu*, Tomlet, Lib.Harmony)
└── KsaNuGet.props           # Generated MSBuild PackageReference list for this KSA snapshot
```

### 1.1 `scripts/update_ksa_dlls.py` and `KSADLLs` Snapshot Files

Replaces `C:\Users\AMPW\source\repos\KSADLLs\updateRepo.py`:

- **Configuration resolution** (in precedence order):
  1. CLI arguments (`--dll-repo-dir`, `--ksa-dir`)
  2. Environment variables (`KSA_DLL_REPO_DIR`, `KSA_DIR`)
  3. Gitignored config file `KSAModTools/.ksa-tools.env` (e.g. `KSA_DLL_REPO_DIR=C:\Users\AMPW\source\repos\KSADLLs`)
- **Version extraction and resolution**:
  - Reads `FileVersion` / `ProductVersion` from `KSA.dll`, `Planet.Core.dll`, and `Planet.Render.Core.dll` in `KSA_DIR`.
  - Compares `KSA.dll` version against the `KSA` row in `<dll-repo-dir>/ksa-versions.csv` (aborting unless `--force` or newer).
  - Scans `Brutal.*.dll` (`Ahwoo.BRUTAL.<Suffix>`), `BepuPhysics.dll` (`BepuPhysics`), `BepuUtilities.dll` (`BepuUtilities`), and `Tomlet.dll` (`Samboy063.Tomlet`) in `KSA_DIR` and resolves each against NuGet's `https://api.nuget.org/v3-flatcontainer/<id>/index.json` (picking the exact version or lowest published version `>=` the DLL version when a build was skipped on NuGet, such as `Ahwoo.BRUTAL.Core` `2026.10.4` and `Ahwoo.BRUTAL.Maths` `2026.10.3`).
  - Fetches `https://raw.githubusercontent.com/StarMapLoader/StarMap/main/Directory.Packages.props` and extracts the `<PackageVersion Include="Lib.Harmony" Version="..." />` version (currently `2.4.3`).
- **Outputs written to `KSADLLs` (`<dll-repo-dir>`)**:
  1. **`ksa-versions.csv`** — single version file replacing `KSA_VERSION.txt`, with columns `name,version,is_nuget,package_id`:
     ```csv
     name,version,is_nuget,package_id
     KSA,2026.10.25.5707,false,
     Planet.Core,1.0.0.0,false,
     Planet.Render.Core,1.0.0.0,false,
     Lib.Harmony,2.4.3,true,Lib.Harmony
     Brutal.Core,2026.10.4,true,Ahwoo.BRUTAL.Core
     ...
     BepuPhysics,2.5.0-beta.29,true,BepuPhysics
     BepuUtilities,2.5.0-beta.29,true,BepuUtilities
     Tomlet,6.2.0,true,Samboy063.Tomlet
     ```
  2. **`KsaNuGet.props`** — generated MSBuild file in `KSADLLs` imported during NuGet/CI builds (and for `Lib.Harmony` Version), defining `<HarmonyVersion>` and `<PackageReference Include="..." Version="[...]" PrivateAssets="all" ExcludeAssets="runtime" />` items.
  3. **DLL cleanup and copy**: deletes all `*.dll` files in `KSADLLs` and copies only `KSA.dll`, `Planet.Core.dll`, and `Planet.Render.Core.dll`.
  4. Commits and pushes `KSADLLs` with message `Update to KSA v<version>`.

### 1.2 `msbuild/KsaMod.props`

Imported at the top of each mod `.csproj`:

- **Optional gitignored local config import**:
  - `<Import Project="$(MSBuildThisFileDirectory)..\local.config.props" Condition="Exists('$(MSBuildThisFileDirectory)..\local.config.props')" />`
- **Default local paths (overridable by `.csproj.user`, `local.config.props`, env vars, or CLI `/p:`)**:
  - `KSADir` defaults to `C:/Program Files/Kitten Space Agency/` when unset.
  - `KSAModsDir` defaults to `$([System.Environment]::GetFolderPath(SpecialFolder.MyDocuments))/My Games/Kitten Space Agency/mods/` when unset.
  - `KSADllRepoDir` defaults to `$(KSA_DLL_REPO_DIR)` when set, otherwise `$(MSBuildThisFileDirectory)..\..\..\KSADLLs\` if that sibling folder exists.
  - Normalizes trailing slashes on `KSADir`, `KSAModsDir`, and `KSADllRepoDir`.
- **NuGet vs. Local DLL mode switch**:
  - `UseKsaNuGetPackages` defaults to `true` when `'$(CI)' == 'true'`, and `false` for local builds (can be overridden with `<UseKsaNuGetPackages>true</UseKsaNuGetPackages>` in `.csproj.user` or `/p:UseKsaNuGetPackages=true`).
- **SDK and compiler defaults** (all guarded with `Condition="'$(Prop)' == ''"`):
  - `Configuration` (`Debug`), `Platform` (`AnyCPU`), `TargetFramework` (`net10.0`), `TargetFrameworkVersion` (`net10.0`).
  - `AppendTargetFrameworkToOutputPath` (`false`), `LangVersion` (`latest`), `ImplicitUsings` (`enable`), `Nullable` (`enable`), `AllowUnsafeBlocks` (`True`).
  - `Configurations` (`Debug;Release`). Projects needing extra configurations (such as `KSAGEffects` with `PerfDebug`) override `<Configurations>Debug;Release;PerfDebug</Configurations>` in their `.csproj`.
  - `OutputType` (`Library`).
  - `GitShortHash` (`nogit`), `InformationalVersionPostfix` (empty), `IncludeSourceRevisionInInformationalVersion` (`false`).
  - `DebugType` (`portable`), `DebugSymbols` (`true`), `ErrorReport` (`prompt`), `WarningLevel` (`4`), `Prefer32Bit` (`false`).
  - Standard `Debug` (`DefineConstants=TRACE;DEBUG`, `Optimize=false`, `InformationalVersionPostfix=-debug`) and `Release` (`DefineConstants=TRACE`, `Optimize=true`) property groups.
  - **Does not** set `Company` or `Authors` — those remain in each consumer `.csproj`.
- **Harmony & KSA NuGet package imports**:
  - Imports `$(KSADllRepoDir)KsaNuGet.props` when `Exists('$(KSADllRepoDir)KsaNuGet.props')` (or `$(KSADir)KsaNuGet.props` in CI where `KSADLLs` is cloned into `deps/ksa/`).
  - Sets fallback `<HarmonyVersion Condition="'$(HarmonyVersion)' == ''">2.4.3</HarmonyVersion>` and includes `<PackageReference Include="Lib.Harmony" Version="$(HarmonyVersion)" Condition="'$(IncludeHarmony)' != 'false'" />`.

### 1.3 `msbuild/KsaMod.targets`

Imported at the bottom of each mod `.csproj`:

- **Derived identity properties**:
  - `AssemblyName` defaults to `$(ModName)` when unset (preserving `KittenExtensions` where `ModName=KittenExtensionsContinued` and `AssemblyName=KittenExtensions`).
  - `OutputPath` defaults to `bin\$(Configuration)`.
  - `Version` defaults to `$(VersionPrefix)`, `FileVersion` defaults to `$(VersionPrefix).0`.
- **KSA and StarMap DLL references**:
  - Always includes `$(KSADir)KSA.dll` and `$(KSADir)Planet*.dll` with `Private="False"`.
  - When `'$(UseKsaNuGetPackages)' != 'true'` (the local build default), also includes `$(KSADir)Brutal*.dll`, `$(KSADir)Bepu*.dll`, and `$(KSADir)Tomlet.dll` with `Private="False"` directly from the local KSA installation.
  - Includes `StarMap.API` reference at `$(KSAModsDir)StarMap/StarMap.API.dll` with `Private="False"` (conditioned on `'$(IncludeStarMapApi)' != 'false'`).
- **`SetVersionMetadata` target** (`BeforeTargets="GetAssemblyVersion"`):
  - Runs `git rev-parse --short HEAD`, sets `GitShortHash` (fallback `nogit`), and sets `InformationalVersion` and `ProductVersion` to `v$(VersionPrefix).$(GitShortHash)$(InformationalVersionPostfix)`.
- **`CopyBuildOutput` and `mod.toml` packaging targets** (`AfterTargets="Build"`, conditioned on `'$(SkipModPackaging)' != 'true'`):
  - Stages output to `$(MSBuildProjectDirectory)/../Release/$(ModName)/` and, on local builds when `Exists('$(KSAModsDir)')` and `'$(CopyToLocalMods)' != 'false'`, also copies to `$(KSAModsDir)$(ModName)/`.
  - Generates `mod.toml` from `ModName`, `Version`, `Authors`, `Description`, `@(None)` items with `ModManifestArray`, `EntryAssembly`, and `@(StarMapModDependency)`.

### 1.4 `githooks/post-merge` and `githooks/post-checkout`

- Installed once per repo via `git config core.hooksPath tooling/githooks`.
- First checks `git -C tooling status --porcelain`; if `tooling/` has uncommitted changes, prints a warning and exits without modifying `tooling/`.
- Resolves an optional target override in order:
  1. `git config --local submodule.tooling.override`
  2. `<ToolingRef>...</ToolingRef>` in `*.csproj.user` or `*.csproj` (parsed with a lightweight regex lookup)
- Behavior based on resolved value:
  - `off` / `none`: skips submodule sync completely (useful when testing local uncommitted edits or a manually checked-out `tooling/` state).
  - `<branch-or-commit>` (e.g. `feat/new-ci` or `a1b2c3d`): runs `git -C tooling fetch origin` and checks out `origin/<branch>` (if a remote branch) or `<commit>` directly.
  - Unset (default): runs `git submodule update --init --recursive` to match the parent commit's recorded gitlink.

### 1.5 Composite Actions and CI Scripts (`ci/`)

- **`ci/build-package/action.yml`**:
  - Inputs:
    - `csproj_path` (required, e.g. `KSAGEffects/KSAGEffects.csproj`)
    - `artifact_prefix` (optional — defaults to the `.csproj` filename stem, e.g. `KittenExtensions` for `KittenExtensions/KittenExtensions.csproj`; `ModName` and `VersionPrefix` are queried directly from `csproj_path` via `dotnet msbuild -getProperty:ModName -getProperty:VersionPrefix`)
    - `mod_deps` (optional JSON array of GitHub release dependencies; `StarMap.API` downloaded by default unless `include_starmap: 'false'`)
    - `project_ref_replacements` (optional JSON array mapping local `<ProjectReference>` paths to CI `<Reference>/<HintPath>` entries, used by `KSAGEffects` for `GEffectsLogic`)
    - `test_command` (optional shell command executed after build with `KSADir` and `KSAModsDir` exported, used by `KSARS`)
    - `ksa_dll_repo` (required per project, e.g. `${{ secrets.KSADLLRepo }}`)
    - `ksa_dll_token` (token with read access to `ksa_dll_repo`, or minted via `ksa_dll_app_id` / `ksa_dll_app_private_key`)
    - `github_token` (defaults to `${{ github.token }}`)
  - Steps:
    1. Setup .NET 10.x (`actions/setup-dotnet@v6`).
    2. Shallow-clone `ksa_dll_repo` into `deps/ksa/` (bringing in `KSA.dll`, `Planet*.dll`, `ksa-versions.csv`, and `KsaNuGet.props`) and read the `KSA` version from `deps/ksa/ksa-versions.csv`.
    3. Resolve `ModName`, `AssemblyName`, and `VersionPrefix` from `csproj_path` via `dotnet msbuild -getProperty:... /p:KSADir="$(pwd)/deps/ksa/"`.
    4. Run `ci/scripts/download_deps.py` for `StarMap` and `mod_deps`.
    5. Apply `project_ref_replacements` if provided.
    6. Compare `v<VersionPrefix>` against the latest GitHub release tag on `push` to `main` to set `release_needed` and detect whether `VersionPrefix` changed in the push range.
    7. Run `dotnet restore <csproj_path> /p:KSADir="$(pwd)/deps/ksa/" /p:UseKsaNuGetPackages=true` and `dotnet build <csproj_path> -c "$CONFIGURATION" --no-restore /p:KSADir="$(pwd)/deps/ksa/" /p:KSAModsDir="$(pwd)/deps/" /p:UseKsaNuGetPackages=true`.
    8. Run optional `test_command`.
    9. Compute `artifact_name` and upload `Release/` via `actions/upload-artifact@v4`.
  - Outputs: `mod_name`, `version`, `ksa_version`, `artifact_name`, `release_needed`.

- **`ci/release/action.yml`**:
  - Inputs: `version`, `ksa_version`, `artifact_name`, `spacedock_id` (optional), `spacedock_username` (optional), `spacedock_password` (optional), `github_token`.
  - Zips the artifact, creates and publishes the GitHub Release, and (when `spacedock_id` is set) uploads the release and changelog to SpaceDock.

### 1.6 Public GitHub App Permissions, Auto-Merge Mechanics, and Approval Governance

- **Required GitHub App Permissions (`KSAModTools` Public App)**:
  - **Repository permissions**:
    - `Contents: Read & Write` — required to create/update the `chore/bump-tooling` branch, commit the updated submodule gitlink, and execute the merge into the default branch.
    - `Pull requests: Read & Write` — required to create the pull request and call the GraphQL `enablePullRequestAutoMerge` / REST merge endpoint (`gh pr merge --auto --squash`).
    - `Metadata: Read` — mandatory default for all GitHub Apps; used to list installed repositories.
  - **Repository settings requirement on consumer repos**:
    - **"Allow auto-merge"** must be checked in the repository's General Settings (`allow_auto_merge: true`), and **"Allow squash merging"** must be enabled.
- **How a repository is configured to allow vs. block automatic merging**:
  - **Allow automatic merge without human intervention (your current solo setup)**:
    - Branch ruleset on `main` has `pull_request` enabled with `required_approving_review_count: 0`.
    - If `required_status_checks` is enabled, the PR created by the App token triggers the `pull_request` CI workflow (`build-package`), waits for the build check to pass, and GitHub automatically squash-merges the PR as soon as checks go green. If no status checks are required, `gh pr merge --auto --squash` (or immediate fallback `gh pr merge --squash`) merges immediately.
  - **Require human approval and block automatic merge (for external users or stricter repos)**:
    - A repository owner can block unattended merging in any of three ways:
      1. **Ruleset / Branch Protection `Require approvals >= 1`**: Because a GitHub App cannot approve its own pull request (and lacks review-approval rights for a self-authored PR), `gh pr merge --auto --squash` simply **queues** the PR in "Auto-merge enabled" state. The PR cannot merge until a human with write/maintain access clicks **Approve** on the PR; the moment they approve (and checks pass), GitHub completes the merge automatically.
      2. **Ruleset `Require approval of the most recent reviewable push` or `Require review from Code Owners`**: Same effect — auto-merge arms the PR, but the merge is held until a human/CODEOWNER approves.
      3. **Disable "Allow auto-merge" in Repository Settings**: The workflow's `gh pr merge --auto` step is made non-fatal (`|| true`), leaving the open bump PR for a maintainer to review and click Merge manually.

---

## 2. Per-Repository Impact and Migration Details

### 2.1 `KSADLLs` (Private Game Assembly Repo)

- **Repository**: `C:\Users\AMPW\source\repos\KSADLLs`
- **Changes**:
  - Remove `updateRepo.py` and `KSA_VERSION.txt`.
  - Run `KSAModTools/scripts/update_ksa_dlls.py` to remove all 29 `Brutal.*.dll` files, `BepuPhysics.dll`, `BepuUtilities.dll`, and `Tomlet.dll`, leaving only `KSA.dll`, `Planet.Core.dll`, `Planet.Render.Core.dll`, `ksa-versions.csv`, and `KsaNuGet.props`.

### 2.2 `KSAGEffects` (KSA G-Effects)

- **Repository**: `AMPW-german/KSAGEffects` (`C:\Users\AMPW\source\repos\KSAGEffects`)
- **SpaceDock ID**: `4363`
- **Submodule**: Add `tooling` -> `https://github.com/AMPW-german/KSAModTools.git` (`branch = main`).
- **Files removed**: `KSAGEffects/KSADLLs.props`, `KSAGEffects/CopyOutput.targets`.
- **`KSAGEffects/KSAGEffects.csproj` changes**:
  - Import `..\tooling\msbuild\KsaMod.props` at top and `..\tooling\msbuild\KsaMod.targets` at bottom.
  - Retain project-specific properties: `ModName` (`KSAGEffects`), `VersionPrefix` (`0.3.3`), `AssemblyVersion` (`0.3.0.0`), `Company` (`AMPW`), `Authors` (`AMPW`), `Copyright` (`GNU GPL v3.0`), and the `PerfDebug` configuration (`<Configurations>Debug;Release;PerfDebug</Configurations>` + its `Condition="'$(Configuration)'=='PerfDebug'"` `<PropertyGroup>`).
  - Remove hardcoded `BepuPhysics` and `BepuUtilities` hint paths (now resolved via `$(KSADir)Bepu*.dll` locally and `KsaNuGet.props` in CI), `Lib.Harmony`, `StarMap.API`, and `SetVersionMetadata`.
  - Retain `GEffectsLogic` `<ProjectReference>`, `KittenExtensions` + `ShaderExtensions` `<Reference>` and `<StarMapModDependency>`, and asset/shader/patch items.
- **`.github/workflows/build.yml` changes**:
  - Checkout with `submodules: recursive` and `fetch-depth: 0`.
  - Mint token for `secrets.KSADLLRepo` and run `./tooling/ci/build-package` (`csproj_path: KSAGEffects/KSAGEffects.csproj`, `mod_deps`: `KittenExtensions`, `ShaderExtensions`, `GEffectsLogic`, `project_ref_replacements` for `GEffectsLogic`).
  - Run `./tooling/ci/release` (`spacedock_id: 4363`).

### 2.3 `ShaderExtensions`

- **Repository**: `AMPW-german/ShaderExtensions` (`C:\Users\AMPW\source\repos\ShaderExtensions`)
- **SpaceDock ID**: `4164`
- **Submodule**: Add `tooling` -> `https://github.com/AMPW-german/KSAModTools.git` (`branch = main`).
- **Files removed**: `ShaderExtensions/KSADLLs.props`, `ShaderExtensions/ShaderExtensions.props`, `ShaderExtensions/CopyOutput.targets`.
- **`ShaderExtensions/ShaderExtensions.csproj` changes**:
  - Import `..\tooling\msbuild\KsaMod.props` and `..\tooling\msbuild\KsaMod.targets`.
  - Retain: `ModName` (`ShaderExtensions`), `VersionPrefix` (`1.1.0`), `AssemblyVersion` (`1.0.0.0`), `Company` (`AMPW`), `Authors` (`AMPW`), `Copyright` (`MIT`), `KittenExtensions` `<Reference>` and `<StarMapModDependency>`.
- **`.github/workflows/build.yml` changes**:
  - Checkout with `submodules: recursive` and `fetch-depth: 0`.
  - Run `./tooling/ci/build-package` (`csproj_path: ShaderExtensions/ShaderExtensions.csproj`, `mod_deps`: `KittenExtensions`) and `./tooling/ci/release` (`spacedock_id: 4164`).

### 2.4 `KittenExtensions` (KittenExtensionsContinued)

- **Repository**: `AMPW-german/KittenExtensions` (`C:\Users\AMPW\source\repos\KittenExtensions`)
- **SpaceDock ID**: `4588`
- **Submodules**: Keep `XPathPatch`; add `tooling` -> `https://github.com/AMPW-german/KSAModTools.git` (`branch = main`).
- **Files removed**: `KittenExtensions/KSADLLs.props`, `KittenExtensions/CopyOutput.targets`.
- **`KittenExtensions/KittenExtensions.csproj` changes**:
  - Import `..\tooling\msbuild\KsaMod.props` and `..\tooling\msbuild\KsaMod.targets`.
  - Retain: `ModName` (`KittenExtensionsContinued`), `AssemblyName` (`KittenExtensions`), `VersionPrefix` (`0.5.3`), `AssemblyVersion` (`0.5.0.0`), `Authors` (`tom_is_unlucky, AMPW`), `Description`, `Copyright` (`MIT`), and `<ProjectReference Include="../XPathPatch/XPathPatch.csproj" />`.
- **`.github/workflows/build.yml` changes**:
  - Checkout with `submodules: recursive` and `fetch-depth: 0`.
  - Run `./tooling/ci/build-package` (`csproj_path: KittenExtensions/KittenExtensions.csproj`) and `./tooling/ci/release` (`spacedock_id: 4588`).

### 2.5 `KSARS`

- **Repository**: `AMPW-german/KSARS` (`C:\Users\AMPW\source\repos\KSARS`)
- **SpaceDock ID**: `4480`
- **Submodule**: Add `tooling` -> `https://github.com/AMPW-german/KSAModTools.git` (`branch = main`).
- **Files removed**: `KSARS/KSADLLs.props`, `KSARS/CopyOutput.targets`.
- **`KSARS/KSARS.csproj` changes**:
  - Import `..\tooling\msbuild\KsaMod.props` and `..\tooling\msbuild\KsaMod.targets`.
  - Retain: `ModName` (`KSARS`), `VersionPrefix` (`1.1.0`), `AssemblyVersion` (`1.0.0.0`), `Company` (`AMPW`), `Authors` (`AMPW`), `Copyright` (`GNU GPL v3.0`), `InternalsVisibleTo` (`KSARS.Visuals.Tests`), `HydroloxEfficiency.xml` asset, and `SpectralTables` `<EmbeddedResource>` items.
  - `tests/KSARS.Visuals.Tests/KSARS.Visuals.Tests.csproj` and `RuntimeStage.targets` remain untouched (`PrivateAssets="all" ExcludeAssets="runtime"` on NuGet packages and `Private="False"` on local DLLs guarantee `Brutal*`, `Bepu*`, and `Tomlet` are never copied to `bin/`, satisfying `StageRuntimeProbe`'s foreign-DLL guard).
- **`.github/workflows/build.yml` changes**:
  - Checkout with `submodules: recursive` and `fetch-depth: 0`.
  - Run `./tooling/ci/build-package` (`csproj_path: KSARS/KSARS.csproj`, `test_command` running `KSARS.Visuals.Tests` with `/p:SkipModPackaging=true`) and `./tooling/ci/release` (`spacedock_id: 4480`).

### 2.6 `MPFX`

- **Repository**: `AMPW-german/MPFX` (`C:\Users\AMPW\source\repos\MPFX`)
- **SpaceDock ID**: `4165`
- **Submodule**: Add `tooling` -> `https://github.com/AMPW-german/KSAModTools.git` (`branch = main`).
- **Files removed**: `MPFX/KSADLLs.props`, `MPFX/CopyOutput.targets`.
- **`MPFX/MPFX.csproj` changes**:
  - Import `..\tooling\msbuild\KsaMod.props` and `..\tooling\msbuild\KsaMod.targets`.
  - Retain: `ModName` (`MPFX`), `VersionPrefix` (`0.3.7`), `AssemblyVersion` (`0.3.1.0`), `Company` (`AMPW`), `Authors` (`AMPW`), `Copyright` (`MIT`), `ModMenu.Attributes` `<PackageReference>` and copy item, `KittenExtensions` `<Reference>`, `KittenExtensionsContinued` + `ShaderExtensions` `<StarMapModDependency>` items, and asset/shader items.
- **`.github/workflows/build.yml` changes**:
  - Checkout with `submodules: recursive` and `fetch-depth: 0`.
  - Run `./tooling/ci/build-package` (`csproj_path: MPFX/MPFX.csproj`, `mod_deps`: `KittenExtensions`) and `./tooling/ci/release` (`spacedock_id: 4165`).

---

## Implementation Steps

### Phase 1: Create `KSAModTools` and Migrate `KSADLLs` to Unified Manifest + Minimal DLLs

- [x] Create local repository `C:\Users\AMPW\source\repos\KSAModTools` and public GitHub repository `AMPW-german/KSAModTools` (with `.gitignore` excluding `.ksa-tools.env` and `local.config.props`).
- [x] Write `scripts/update_ksa_dlls.py` (migrated from `KSADLLs/updateRepo.py`), reading the DLL repo location from `KSA_DLL_REPO_DIR` or `.ksa-tools.env`, resolving `Ahwoo.BRUTAL.*`, `BepuPhysics`, `BepuUtilities`, and `Samboy063.Tomlet` against NuGet.org, fetching `Lib.Harmony` version from `StarMapLoader/StarMap` `main` (`Directory.Packages.props`), writing unified `ksa-versions.csv` (including `KSA` and `Planet.*` versions + `is_nuget` flag) and `KsaNuGet.props` into `KSADLLs`, and stripping `Brutal*`/`Bepu*`/`Tomlet` DLLs from `KSADLLs`.
- [x] Run `scripts/update_ksa_dlls.py` against `C:\Program Files\Kitten Space Agency` and `C:\Users\AMPW\source\repos\KSADLLs`, verifying `ksa-versions.csv`, `KsaNuGet.props`, and the reduced `KSADLLs` contents (`KSA.dll`, `Planet.Core.dll`, `Planet.Render.Core.dll`).
- [x] Author `msbuild/KsaMod.props` (author-neutral SDK/compiler defaults, `Debug`/`Release` only, local-install default `UseKsaNuGetPackages=false` vs. CI `UseKsaNuGetPackages=true`, `KsaNuGet.props` import from `KSADLLs`, and `Lib.Harmony`).
- [x] Author `msbuild/KsaMod.targets` (`AssemblyName`/`OutputPath` defaults, local `$(KSADir)` DLL references vs. NuGet mode, `StarMap.API.dll` reference, `SetVersionMetadata`, `CopyBuildOutput` / `mod.toml` generation).
- [x] Author `githooks/post-merge` and `githooks/post-checkout` (supporting `<ToolingRef>` in `.csproj`/`.csproj.user` and `git config submodule.tooling.override`, while refusing dirty `tooling/` trees).
- [x] Author `ci/scripts/download_deps.py`, `ci/scripts/rewrite_project_refs.py`, `ci/scripts/publish_github_release.sh`, `ci/scripts/publish_spacedock.sh`, and composite actions `ci/build-package/action.yml` (resolving `ModName`/`VersionPrefix` from `csproj_path` and reading `KSA` version from `deps/ksa/ksa-versions.csv`) and `ci/release/action.yml`.
- [x] Author `.github/workflows/bump-consumers.yml` to discover all repositories with the public `KSAModTools` App installed and open/auto-merge `chore/bump-tooling` PRs against their default branch.

### Phase 2: Pilot Migration in `KSAGEffects`

- [x] Add `KSAModTools` submodule at `tooling/` in `KSAGEffects` and configure `git config core.hooksPath tooling/githooks`.
- [x] Migrate `KSAGEffects/KSAGEffects.csproj` to import `KsaMod.props` and `KsaMod.targets` (keeping `Company`, `Authors`, and `PerfDebug` in `KSAGEffects.csproj`), and remove `KSAGEffects/KSADLLs.props` and `KSAGEffects/CopyOutput.targets`.
- [x] Update `.github/workflows/build.yml` to use `./tooling/ci/build-package` and `./tooling/ci/release`.
- [x] Verify local builds (`dotnet build KSAGEffects.slnx -c Debug`, `-c Release`, and `-c PerfDebug`) in both default local-DLL mode (`UseKsaNuGetPackages=false`) and NuGet mode (`/p:UseKsaNuGetPackages=true`), confirming no leaked DLLs in `Release/KSAGEffects/` and identical `mod.toml` output.

### Phase 3: Roll Out to `ShaderExtensions`, `KittenExtensions`, `KSARS`, and `MPFX`

- [x] Migrate `ShaderExtensions` (`ShaderExtensions.csproj`, delete `KSADLLs.props`, `ShaderExtensions.props`, `CopyOutput.targets`, update `build.yml`) and verify local Debug/Release builds.
- [x] Migrate `KittenExtensions` (`KittenExtensions.csproj`, delete `KSADLLs.props`, `CopyOutput.targets`, update `build.yml`) and verify local Debug/Release builds.
- [x] Migrate `KSARS` (`KSARS.csproj`, delete `KSADLLs.props`, `CopyOutput.targets`, update `build.yml`) and verify `dotnet build` and `dotnet test tests/KSARS.Visuals.Tests/KSARS.Visuals.Tests.csproj /p:SkipModPackaging=true`.
- [x] Migrate `MPFX` (`MPFX.csproj`, delete `KSADLLs.props`, `CopyOutput.targets`, update `build.yml`) and verify local Debug/Release builds.

### Phase 4: Public GitHub App and Auto-Bump Verification

- [ ] Create and install the public `KSAModTools` GitHub App on the mod repositories, configure its credentials in `KSAModTools`, and enable "Allow auto-merge" in each consumer repository's settings.
- [ ] Trigger `bump-consumers.yml` on `KSAModTools` and verify that submodule bump PRs are opened against each repo's default branch, trigger PR CI checks, and auto-merge cleanly.

## Verification Criteria

- [x] All five mod repositories build cleanly locally in `Debug` and `Release` (plus `PerfDebug` for `KSAGEffects`) both against the local KSA install (`UseKsaNuGetPackages=false`, the default) and against NuGet + `KSADLLs` (`/p:UseKsaNuGetPackages=true`).
- [x] `Release/<ModName>/` in every repository contains only the mod's own assembly, `.pdb`, assets/shaders/patches, and `mod.toml` — zero `Brutal*.dll`, `Bepu*.dll`, or `Tomlet.dll` files copied to output.
- [x] Generated `Release/<ModName>/mod.toml` matches the pre-migration manifest in all five repositories.
- [x] `KSARS.Visuals.Tests` and `StageRuntimeProbe` pass without any foreign-assembly staging errors.

## Risks and Considerations

- **Skipped NuGet versions on `Ahwoo.BRUTAL.*`**: Some `Brutal.*.dll` files shipped in KSA report a `FileVersion` that was never pushed to NuGet.org (for example, `Brutal.Core.dll` reports `2026.10.2` while NuGet has `2026.10.1` and `2026.10.4`; `Brutal.Maths.dll` reports `2026.10.2` while NuGet has `2026.10.1` and `2026.10.3`). `scripts/update_ksa_dlls.py` queries NuGet's version list and picks the matching version or nearest compatible published version rather than blindly writing `FileVersion`.
- **Preventing NuGet DLL copy to mod output**: Setting `PrivateAssets="all" ExcludeAssets="runtime"` (exposing only `compile` assets) on all `Ahwoo.BRUTAL.*`, `Bepu*`, `Samboy063.Tomlet`, and `Lib.Harmony` packages in `KsaNuGet.props` ensures they behave identically to `<Reference Private="False" />`.
