# EntropyX

**EntropyX** is a cross-platform CLI tool that measures how disordered your codebase is over time.  
It walks your git history, collects per-file quality signals (SLOC, cyclomatic complexity, code smells, coupling, and maintainability), and rolls them into a single **entropy score** per commit—higher means more complexity spread unevenly across more files.

> **EntropyX is a temperature gauge, not a GPA.**  
> The entropy score measures *structural drift over time* — how complexity is spreading across a codebase as it grows and evolves. A rising score in a growing repository is expected and does not automatically mean the code needs refactoring. Use the *trend* and *relative-to-history* context to understand whether drift is accelerating, stabilising, or improving.

---

## Table of Contents

- [How It Works](#how-it-works)
- [Prerequisites](#prerequisites)
- [Build](#build)
- [Publish (self-contained executables)](#publish-self-contained-executables)
- [Commands](#commands)
  - [scan lang](#scan-lang)
  - [scan here](#scan-here)
  - [scan head](#scan-head)
  - [scan from](#scan-from)
  - [scan full](#scan-full)
  - [scan chk](#scan-chk)
  - [scan details](#scan-details)
  - [report](#report)
  - [heatmap](#heatmap)
  - [refactor](#refactor)
  - [compare](#compare)
  - [db list](#db-list)
  - [clear](#clear)
  - [check tools](#check-tools)
- [Metrics Reference](#metrics-reference)
- [Entropy Formula](#entropy-formula)
- [Citation](#citation)
- [Supported Languages](#supported-languages)

---

## How It Works

1. **Scan** – EntropyX walks a git repository commit-by-commit, reads each changed source file directly from the git object store (no working-tree checkout required), and computes per-file quality metrics.
2. **Store** – Results are persisted in a local SQLite database (`entropyx.db` by default).
3. **Report** – The `report` command reads the database and prints per-commit entropy scores so you can track how code health evolves across your history.

---

## Prerequisites

| Requirement | Notes |
|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Required to build from source |
| [git](https://git-scm.com/) | Must be in `PATH` for git-backed scan commands |
| [cloc](https://github.com/AlDanial/cloc) | Optional – used by external tool checks; not required for basic scans |

Run `entropyx check tools` after installation to verify your environment.

---

## Build

Clone the repository and build with the .NET SDK. A regular `dotnet build` produces a framework-dependent binary that runs on the machine where the .NET runtime is installed:

```sh
git clone https://github.com/drcircuit/entropyx.git
cd entropyx

# Build the whole solution
dotnet build EntropyX.sln

# Run directly without publishing
dotnet run --project src/CodeEvo.Cli -- --help
```

---

## Publish (self-contained executables)

The CLI project ships with publish profiles for the four most common targets.  
Each profile produces a **self-contained, single-file** native executable — no .NET runtime required on the target machine.

| Platform | Command |
|---|---|
| Windows x64 | `dotnet publish src/CodeEvo.Cli /p:PublishProfile=win-x64 -o out/win-x64` |
| Linux x64 | `dotnet publish src/CodeEvo.Cli /p:PublishProfile=linux-x64 -o out/linux-x64` |
| macOS Intel | `dotnet publish src/CodeEvo.Cli /p:PublishProfile=osx-x64 -o out/osx-x64` |
| macOS Apple Silicon | `dotnet publish src/CodeEvo.Cli /p:PublishProfile=osx-arm64 -o out/osx-arm64` |

The output binary is named `entropyx` (or `entropyx.exe` on Windows).  
You can copy it to any directory on your `PATH` to use it globally.

---

## Commands

All commands support `-h` / `--help` for inline documentation.

---

### scan lang

Detect and list the programming language of every source file in a directory.

```sh
entropyx scan lang [path] [--include <patterns>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `path` | `.` | Directory to scan |
| `--include` | _(all recognised source files)_ | Comma-separated file patterns (e.g. `*.cs,*.ts`) |

```sh
entropyx scan lang ./src
entropyx scan lang . --include *.cs,*.ts
```

---

### scan here

Scan a directory **without a git repository**. Reports SLOC and basic metrics for every recognised source file. Optionally saves a snapshot `data.json` for later comparison with `compare`.

```sh
entropyx scan here [path] [--include <patterns>] [--save <file.json>] [--kind all|production|utility]
```

| Argument / Option | Default | Description |
|---|---|---|
| `path` | `.` | Directory to scan |
| `--include` | _(all recognised source files)_ | Comma-separated file patterns (e.g. `*.cs,*.ts`) |
| `--save` | _(none)_ | Save a `data.json` snapshot to this path |
| `--kind` | `all` | Filter by code kind: `all`, `production`, or `utility` |

```sh
entropyx scan here ./src
entropyx scan here . --save baseline.json
entropyx scan here . --kind production
```

---

### scan head

Scan only the most recent commit (`HEAD`) of a git repository and store the results in the database.

```sh
entropyx scan head [repoPath] [--db <file>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `repoPath` | `.` | Path to the git repository |
| `--db` | `entropyx.db` | SQLite database file |

```sh
entropyx scan head . --db metrics.db
```

---

### scan from

Scan all commits starting from a given commit hash (inclusive) up to `HEAD` and store the results.

```sh
entropyx scan from <commit> [repoPath] [--db <file>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `commit` | _(required)_ | Starting commit SHA |
| `repoPath` | `.` | Path to the git repository |
| `--db` | `entropyx.db` | SQLite database file |

```sh
entropyx scan from abc1234 . --db metrics.db
```

---

### scan full

Scan the **entire git history** of a repository (oldest commit first) and store every commit.  
Already-scanned commits are skipped automatically, so re-running is safe.

```sh
entropyx scan full [repoPath] [--db <file>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `repoPath` | `.` | Path to the git repository |
| `--db` | `entropyx.db` | SQLite database file |

```sh
entropyx scan full /path/to/repo
entropyx scan full . --db metrics.db
```

---

### scan chk

Scan only **checkpoint commits** (tagged commits and merge commits). A faster alternative to `scan full` for large repositories where you only want to track significant milestones.

```sh
entropyx scan chk [repoPath] [--db <file>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `repoPath` | `.` | Path to the git repository |
| `--db` | `entropyx.db` | SQLite database file |

```sh
entropyx scan chk . --db metrics.db
```

---

### scan details

Scan the current `HEAD` commit and display a detailed per-language SLOC breakdown, per-file metrics table, notable events (troubled/heroic commits), and a health assessment. Optionally writes a rich HTML drilldown report. Uses the database for historical context when available.

```sh
entropyx scan details [repoPath] [--db <file>] [--html <outputFile>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `repoPath` | `.` | Path to the git repository |
| `--db` | `entropyx.db` | SQLite database file (for historical context) |
| `--html` | _(none)_ | Write a rich HTML drilldown report to this file |

```sh
entropyx scan details .
entropyx scan details . --db metrics.db --html drilldown.html
```

---

### report

Print a summary of all stored commit metrics. Optionally filter by a commit hash prefix, generate a rich HTML report with graphs and issue highlights, or export standalone vector SVG charts for use in papers and whitepapers.

```sh
entropyx report <repoPath> [--db <file>] [--commit <hash>] [--html <outputFile>] [--export-figures <dir>] [--kind all|production|utility]
```

| Argument / Option | Default | Description |
|---|---|---|
| `repoPath` | _(required)_ | Path to the git repository |
| `--db` | `entropyx.db` | SQLite database file |
| `--commit` | _(all)_ | Show only the commit matching this hash prefix |
| `--html` | _(none)_ | Write a rich HTML report to this file |
| `--export-figures` | _(none)_ | Export vector SVG charts to this directory |
| `--kind` | `all` | Filter metrics by code kind: `all`, `production`, or `utility` |

```sh
# Show all commits
entropyx report . --db metrics.db

# Show a specific commit
entropyx report . --db metrics.db --commit abc1234

# Generate a rich HTML report
entropyx report . --db metrics.db --html report.html

# Export SVG figures only (e.g. for whitepapers)
entropyx report . --db metrics.db --export-figures ./figures

# Generate HTML report and export SVG figures together
entropyx report . --db metrics.db --html report.html --export-figures ./figures
```

The HTML report includes:
- **Health gauges** – semi-circular gauges for entropy, complexity, and smell scores. The entropy gauge shows historical min/avg/max across all scanned commits; the CC and smell gauges show low/high threshold reference markers.
- **Entropy timeseries** – entropy score plotted over every scanned commit
- **Codebase growth charts** – SLOC, file count, and SLOC-per-file over time
- **CC and Smell timeseries** – avg cyclomatic complexity and avg smell score over time
- **Complexity heatmap** – per-file badness scores for the latest commit, sorted hottest-first
- **Issues section** – top 10 files by size, cyclomatic complexity, smell score, and coupling, each with a colored severity badge
- **Entropy Contribution Analysis** – three ranked lists that decompose where entropy is coming from:
  - *Top diffusion contributors* — files with the highest `−p_i·log₂(p_i)` term; the entropy *spreaders* pulling complexity toward a uniform distribution
  - *Top badness contributors* — files with the highest raw combined badness `b_i`
  - *Top delta contributors* — files with the largest increase in badness since the previous commit (requires ≥ 2 scanned commits)
- **Troubled commits** – commits that caused a statistically significant entropy _increase_
- **Heroic commits** – commits that caused a statistically significant entropy _decrease_
- **Full commit table** – all commits with entropy score and delta, color-coded green/red
- **Relative assessment** – the current score shown at its historical percentile so drift is judged against the repo's own evolution

The `--export-figures` option writes the following standalone vector SVG files (ideal for academic papers):

| File | Content |
|---|---|
| `entropy-over-time.svg` | Entropy score over git history |
| `sloc-over-time.svg` | Total SLOC over git history |
| `sloc-per-file-over-time.svg` | Average SLOC per file over git history |
| `cc-over-time.svg` | Average cyclomatic complexity over git history |
| `smell-over-time.svg` | Average weighted smell score over git history |

---

### heatmap

Scan a directory and render a **complexity heatmap** showing per-file hotspots. Optionally saves the heatmap as a PNG image.

```sh
entropyx heatmap [path] [--html <file.png>] [--include <patterns>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `path` | `.` | Directory to scan |
| `--html` | _(none)_ | Save the heatmap as a PNG image to this path |
| `--include` | _(all recognised source files)_ | Comma-separated file patterns (e.g. `*.cs,*.ts`) |

**Console output** – files are sorted hottest-first and each row shows a 10-block heat bar coloured on a traffic-light gradient (green → yellow → red) together with SLOC, cyclomatic complexity, coupling, and a raw badness score.

**PNG image** (`--html`) – generates a PNG using an IR camera colour palette (black → indigo → blue → cyan → green → yellow → orange → red → white) with one row per file and a colour-scale legend at the bottom.

```sh
# Show heatmap in the console for the current directory
entropyx heatmap .

# Show heatmap and save an IR-palette PNG
entropyx heatmap ./src --html hotspots.png

# Restrict to C# and TypeScript files only
entropyx heatmap . --include *.cs,*.ts --html hotspots.png
```

---

### refactor

Rank source files by their refactoring priority and print the top candidates. Optionally writes an HTML report. Useful for identifying where to focus technical-debt reduction efforts.

```sh
entropyx refactor [path] [--focus <metric>] [--top <n>] [--html <outputFile>] [--include <patterns>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `path` | `.` | Directory to scan |
| `--focus` | `overall` | Metric(s) to rank by: `overall`, `sloc`, `cc`, `mi`, `smells`, `coupling`, or a comma-separated combination |
| `--top` | `10` | Number of files to list |
| `--html` | _(none)_ | Write a rich HTML refactor report to this file |
| `--include` | _(all recognised source files)_ | Comma-separated file patterns (e.g. `*.cs,*.ts`) |

```sh
# Show top 10 files by overall refactor priority
entropyx refactor .

# Focus on cyclomatic complexity, show top 20
entropyx refactor . --focus cc --top 20

# Generate an HTML refactor report
entropyx refactor . --html refactor.html

# Focus on multiple metrics
entropyx refactor ./src --focus cc,smells --html refactor.html
```

---

### compare

Compare two `data.json` snapshots (produced by `scan here --save` or `report --html`) and print an evolutionary assessment showing which files improved, regressed, appeared, or disappeared. Optionally writes an HTML comparison report.

```sh
entropyx compare <baseline> <current> [--html <outputFile>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `baseline` | _(required)_ | Path to the baseline `data.json` file |
| `current` | _(required)_ | Path to the current `data.json` file |
| `--html` | _(none)_ | Write a rich HTML comparison report to this file |

```sh
# Compare two snapshots on the console
entropyx compare baseline.json current.json

# Generate an HTML comparison report
entropyx compare baseline.json current.json --html comparison.html
```

> **Tip:** use `scan here --save` to capture a snapshot before and after a refactoring session, then run `compare` to see the impact.

---

### db list

List all repositories stored in the database and their total commit counts.

```sh
entropyx db list [--db <file>]
```

| Option | Default | Description |
|---|---|---|
| `--db` | `entropyx.db` | SQLite database file |

```sh
entropyx db list
entropyx db list --db metrics.db
```

---

### clear

Delete all scanned data from the database for the given repository. Prompts for confirmation before erasing.

```sh
entropyx clear [repoPath] [--db <file>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `repoPath` | `.` | Path to the git repository whose data to clear |
| `--db` | `entropyx.db` | SQLite database file |

```sh
entropyx clear . --db metrics.db
```

---

### check tools

Verify that external tools (`git`, `cloc`) are available and print platform-specific install instructions for any that are missing.

```sh
entropyx check tools [path]
```

```sh
entropyx check tools
entropyx check tools ./src
```

---

## Metrics Reference

| Metric | Description |
|---|---|
| **SLOC** | Source lines of code — blank lines and comment-only lines are excluded |
| **Cyclomatic Complexity (CC)** | Number of independent paths through a file; higher means harder to test |
| **Maintainability Index (MI)** | Composite score (0–100) combining volume, complexity, and line count; lower means harder to maintain |
| **Code Smells** | Counted at three severity levels: High, Medium, and Low |
| **Coupling Proxy** | Proxy for afferent/efferent coupling; higher means more inter-module dependencies |
| **Entropy Score** | Commit-level score — see formula below |

---

## Entropy Formula

The entropy score for a commit is computed as follows:

1. **Transform** each file's SLOC to `L' = ln(1 + SLOC)`.
2. **Min-max normalise** the five feature vectors `{L', CC, S, U, M}` across all files in the commit.
3. **Compute per-file badness**:  
   `b_i = L̂'_i + Ĉ_i + Ŝ_i + Û_i + (1 − M̂_i)`  
   where smells are weighted `3×` (High), `2×` (Medium), `1×` (Low) before normalisation.
4. **Discard** files with `b_i ≤ ε`.  
   If fewer than 2 active files remain the score is **0**.
5. **Shannon entropy** over the badness distribution:  
   `H = −Σ p_i · log₂(p_i)` where `p_i = b_i / Σb`
6. **Normalise** by `log₂(N)` (N = number of active files) to obtain `H_norm ∈ [0, 1]`.
7. **Scale** by mean badness: `entropy = H_norm × (Σb / N)`.
8. **Clamp** at 0.

A **high score** means complexity and problems are both large in magnitude *and* spread evenly across many files — the worst possible state for maintainability.

> **Interpreting the score:** EntropyX is designed to be read as a *longitudinal signal*, not an absolute grade.  
> A score of 1.2 in a repository that previously peaked at 1.8 and is now stable represents controlled growth, not crisis.  
> Reports show your current score at its **historical percentile** (relative to all stored snapshots) so you can judge drift in context.

---

## Citation

If you use EntropyX in academic work or want to reference its design, please cite the foundational whitepaper:

> Sande-Larsen, E. (2026). *EntropyX: A Longitudinal Entropy-Based Framework for Measuring Code Drift and Technical Debt in Modern Software Systems*. Zenodo. https://doi.org/10.5281/zenodo.18786769

```bibtex
@misc{sandelarsen2026entropyx,
  author    = {Sande-Larsen, Espen},
  title     = {{EntropyX}: A Longitudinal Entropy-Based Framework for Measuring Code Drift and Technical Debt in Modern Software Systems},
  year      = {2026},
  doi       = {10.5281/zenodo.18786769},
  url       = {https://zenodo.org/records/18786770},
  publisher = {Zenodo}
}
```

---

## Supported Languages

| Extension(s) | Language |
|---|---|
| `.cs` | C# |
| `.java` | Java |
| `.c`, `.h` | C |
| `.cpp`, `.cc`, `.cxx`, `.hpp` | C++ |
| `.ts`, `.tsx` | TypeScript |
| `.js`, `.jsx`, `.mjs`, `.cjs` | JavaScript |
| `.rs` | Rust |
| `.py` | Python |
