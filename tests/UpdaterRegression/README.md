# Updater regression

Package-free .NET 8 console tests, linking the production update helper and service with minimal application-boundary stubs. Runs in temporary directories and uses synthetic packages, injected HTTP/process seams, and replacement fault injection. It does not download or launch a released updater.

Windows / ordinary SDK:

```sh
dotnet run --project tests/UpdaterRegression/UpdaterRegression.csproj -c Release
```

Restricted Linux environment with an existing official SDK:

```sh
bash tests/UpdaterRegression/run-portable-tests.sh /path/to/dotnet-sdk-directory
```

50 checks cover approved HTTPS URLs and redirect hosts, required SHA-256, manifest BOM, bounds and archive entry safety, durable replacement/rollback including partial replacement, preview and single-file package guards, exact-PID wait, atomic ready/proceed handshake, cancellation/timeouts, quoted restart arguments, and preserved --data-dir. Actual Windows GUI, process launch, and install replacement remain manual checks.
