# DHub storage regression harness

This portable .NET 8 console harness links the actual production `ShortcutStore`,
`StorageFile`, `StorageIntegrity`, `StorageSnapshotDiff`, `ShortcutDetector`,
`ShortcutRunner`, and model source files. Only the WPF image type/icon service
boundary is stubbed. It never invokes a shell launch. All storage uses unique
OS temporary directories, removed after each test; it never opens user DHub data.

Run on Windows, macOS, or Linux with the .NET 8 SDK:

```sh
dotnet run --project tests/StorageRegression/StorageRegression.csproj --configuration Release
```

For a restricted Linux environment where SDK first-run/restore cannot create a
named mutex, the no-network/no-restore Roslyn runner uses the installed SDK:

```sh
DOTNET=/path/to/dotnet tests/StorageRegression/run.sh
```

`latest-results.txt` records the latest local run. The harness exits nonzero when
an assertion fails. The direct runner treats compiler warnings as errors.

Covered: staged split parsing; disabled split mode; missing/corrupt main and
backup recovery without writes; incomplete/mismatched split preservation;
read-only mutation guards; explicit reconciliation with original file copies
verified by SHA-256; stale-file detection; valid backup retention; pre-parse/post-save fingerprint safeguards; atomic single
file replacement and failed replacement evidence; invalid IDs/references;
non-destructive full-recipe deduplication and workspace reference remapping;
bookmark-source preservation; Windows argument quoting without process start; and cached/async shortcut status
(initial unknown, actual existing/missing files/folders, invalidation, stale-result
rejection, read-only search getters, and exclusion from persisted JSON).

## Storage contract and remaining limits

- Loading never automatically writes or promotes an orphan temporary file.
- Each file is written to a unique sibling temporary file, flushed to disk,
  parsed/validated, and committed using `File.Replace` (existing destination) or
  a same-directory `File.Move` (first creation). There is no Delete-then-Move.
- Split mode remains supported. Multiple file replacements are not a single
  transaction. If a save is interrupted, the next load detects differing/partial
  split files and requires an explicit choice instead of silently rolling back.
- Resolution/restore archives root files outside automatic backup retention,
  validates copied hashes and checks originals again before replacing anything.
  The conflict preview includes capped concrete differences, not counts alone.
- Missing main + complete split-only recovery cannot recover combined-only
  bookmark folder sources. A valid combined backup is tried first.
- Snapshot equality and ID/reference validation are conservative. Invalid source
  files are retained; they are not repaired by deleting rows or guessing IDs.
- The tests exercise real filesystem APIs on the executing OS. They do not prove
  Windows power-loss durability, directory metadata durability, SMB semantics,
  antivirus/file-lock behavior, or multi-process transactional exclusion.
  Fingerprint checks detect observed external changes but are not filesystem CAS.
- `IsReadOnly` blocks persistence and public mutating store methods. Public
  observable collections/model properties remain editable by existing UI code;
  the UI must present them as unsaved/read-only rather than claiming immutability.
