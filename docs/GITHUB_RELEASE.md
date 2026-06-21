# DHub GitHub Release

## Repository

```text
https://github.com/Chairman-bits/DHub.git
```

## Build

```bat
Build.bat
```

Input only the version.

```text
Enter version (ex: 1.0.1): 1.0.1
```

## Generated files

```text
release/
├─ DHub.exe
└─ version.json

github-release/
├─ DHub.exe
├─ version.json
└─ RELEASE_NOTES.md
```

## Upload / commit

Upload to GitHub Releases:

```text
github-release/DHub.exe
```

Release tag:

```text
v1.0.1
```

Commit to repository:

```text
release/version.json
```

## Update URL

```text
https://raw.githubusercontent.com/Chairman-bits/DHub/main/release/version.json
```

## version.json

```json
{
  "version": "1.0.1",
  "downloadUrl": "https://github.com/Chairman-bits/DHub/releases/download/v1.0.1/DHub.exe",
  "releaseNotes": "DHub v1.0.1"
}
```
