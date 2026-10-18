# Unified launcher regression

A dependency-free executable that links the production transient-row model,
search engine, and linked-folder service. It never launches anything and uses only
throwaway fixtures under the operating system's temporary directory.

## Run

Windows CI, with .NET 10 installed:

```powershell
dotnet run --project tests/UnifiedLauncherRegression/UnifiedLauncherRegression.csproj -c Release
```

Portable service/search checks with an already-installed .NET 8 SDK (no restore
or package download):

```sh
tests/UnifiedLauncherRegression/run-portable-tests.sh /path/to/dotnet-sdk /tmp/dhub-unified-tests
```

The portable runner compiles with the SDK's C# compiler and .NET 8 reference
assemblies. It does not prove that WPF or the Windows shell UI builds or works.

## Coverage

- Exact-name, prefix, name-word, and metadata ranking. Relevance precedes favorites.
- Deterministic favorites/manual/name/key order, all-word search, whitespace,
  Latin accents, Japanese width and voiced marks, emoji, malformed UTF-16, and
  culture independence.
- Rendering limits and total count, duplicate registration identity, payload and
  recipe preservation, disabled rows, and the distinct saved-set action label.
- Shared four-scan concurrency bound, same-request coalescing, queued cancellation,
  path/filter updates, removal/reconnection, and disposal.
- Snapshot identity, watcher change/overflow invalidation, retired watcher events,
  original `.lnk`/`.url` targets, and non-destructive hidden entries.
- Missing/inaccessible folders, deferred enumeration errors, skipped symbolic
  links, depth 16, 10,000-entry cap, and 20,000-visit cap.

Queue race tests hold the real shared scan semaphore, so they do not depend on
winning a scheduler race. Real watcher tests wait for an observable condition with
a timeout; they do not treat a fixed sleep as evidence. Overflow and retired-event
checks directly exercise the registered handlers. The inaccessible-directory test
separately isolates deferred enumeration with an inert, explicitly unhealthy
watcher; those rows must remain disabled.

Symlink fixtures report a skip when the host denies link creation. Unix permission
fixtures run only where the process cannot bypass permissions; Windows native ACL
and live shell behavior remain separate QA requirements. Skips are printed in the
result rather than counted as passes.
