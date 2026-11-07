# Changes

## 0.1.2

- ISO extraction now uses literal .NET directory/file APIs instead of a PowerShell provider lookup that could fail for a copied Hidden/System file, such as sources/ws.dat.
- Source ReadOnly files are copied and made writable without removing their Hidden/System attributes. File lengths are checked and copy failures identify the relative source/destination; no file is silently skipped.
- Added native PowerShell regression tests for Hidden/System/ReadOnly files, literal bracket/Unicode filenames, empty/nested files, overwrite protection, recursive destinations, missing sources, locked files and junction rejection. Windows-specific cases run on Windows PowerShell 5.1 in CI.
- Set window background/foreground explicitly and retained a dark disabled-button template for readable status/error screens.
- Both ImageTools.ps1 and its MediaCopy.ps1 helper are shipped in the application Scripts directory.

To update, close the previous application, extract the complete new package into a new folder, and start a new project for an ISO extraction that failed before completion. The original ISO is unchanged. Do not delete a project with an active WIM mount.
