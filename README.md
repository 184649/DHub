# DHub

DHub full application with compressed ZIP update.

## Build

```bat
Build.bat
```

## Files to place on GitHub main branch root

Build output is under `release/`.

```text
DHub.zip
DHubUpdater.zip
version.json
release-notes.json
```

`DHub.zip` contains `DHub.exe`.
`DHubUpdater.zip` contains `DHubUpdater.exe`.

## version.json

```json
{
  "version": "1.0.1",
  "downloadUrl": "https://raw.githubusercontent.com/Chairman-bits/DHub/main/DHub.zip",
  "updaterUrl": "https://raw.githubusercontent.com/Chairman-bits/DHub/main/DHubUpdater.zip",
  "releaseNotes": "https://raw.githubusercontent.com/Chairman-bits/DHub/main/release-notes.json"
}
```
