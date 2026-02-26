# EntropyX

**EntropyX** is a cross-platform CLI tool that measures how disordered your codebase is over time.  
It walks your git history, collects per-file quality signals (SLOC, cyclomatic complexity, code smells, coupling, and maintainability), and rolls them into a single **entropy score** per commit—higher means more complexity spread unevenly across more files.

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
  - [report](#report)
  - [check tools](#check-tools)
  - [heatmap](#heatmap)
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

### scan lang

Detect and list the programming language of every source file in a directory.

```sh
entropyx scan lang [path]
```

| Argument | Default | Description |
|---|---|---|
| `path` | `.` | Directory to scan |

```sh
entropyx scan lang ./src
```

---

### scan here

Scan the current directory without a git repository. Reports SLOC and basic metrics for every recognised source file.

```sh
entropyx scan here [path]
```

| Argument | Default | Description |
|---|---|---|
| `path` | `.` | Directory to scan |

```sh
entropyx scan here ./src
```

---

### scan head

Scan only the most recent commit (`HEAD`) of a git repository and store the results.

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

Scan all commits starting from a given commit hash (inclusive) up to `HEAD`.

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

Scan the entire git history of a repository (oldest commit first) and store every commit.  
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
```

---

### scan chk

Scan only **checkpoint commits** (tagged commits and merge commits). This is a faster alternative to `scan full` for large repositories where you only want to track significant milestones.

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

### report

Print a summary of all stored commit metrics. Optionally filter by a commit hash prefix or generate a rich HTML report with graphs, timeseries, and issue highlights.

```sh
entropyx report <repoPath> [--db <file>] [--commit <hash>] [--html <outputFile>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `repoPath` | _(required)_ | Path to the git repository |
| `--db` | `entropyx.db` | SQLite database file |
| `--commit` | _(all)_ | Show only the commit matching this hash prefix |
| `--html` | _(none)_ | Write a rich HTML report to this file |

```sh
# Show all commits
entropyx report . --db metrics.db

# Show a specific commit
entropyx report . --db metrics.db --commit abc1234

# Generate a rich HTML report
entropyx report . --db metrics.db --html report.html
```

The HTML report includes:
- **Entropy timeseries** – entropy score plotted over every scanned commit
- **Codebase growth charts** – SLOC and file count over time
- **Issues section** – top 10 files by size, cyclomatic complexity, and smell score, each with a colored severity badge
- **Troubled commits** – commits that caused a statistically significant entropy _increase_
- **Heroic commits** – commits that caused a statistically significant entropy _decrease_
- **Full commit table** – all commits with their entropy score and delta, color-coded green/red

---

### check tools

Verify that external tools (`git`, `cloc`) are available and print platform-specific install instructions for any that are missing.

```sh
entropyx check tools
```

---

### heatmap

Scan a directory and render a **complexity heatmap** showing per-file hotspots.

```sh
entropyx heatmap [path] [--output <file.png>] [--include <patterns>]
```

| Argument / Option | Default | Description |
|---|---|---|
| `path` | `.` | Directory to scan |
| `--output` | _(none)_ | Save the heatmap as a PNG image to this path |
| `--include` | _(all recognised source files)_ | Comma-separated file patterns to include (e.g. `*.cs,*.ts`) |

**Console output** – files are sorted hottest-first and each row shows a 10-block heat bar coloured on a traffic-light gradient (green → yellow → red) together with SLOC, cyclomatic complexity, coupling, and a raw badness score.

**PNG image** (`--output`) – generates a PNG using an IR camera colour palette (black → indigo → blue → cyan → green → yellow → orange → red → white) with one row per file and a colour-scale legend at the bottom.

```sh
# Show heatmap in the console for the current directory
entropyx heatmap .

# Show heatmap and save an IR-palette PNG
entropyx heatmap ./src --output hotspots.png

# Restrict to C# and TypeScript files only
entropyx heatmap . --include *.cs,*.ts --output hotspots.png
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
