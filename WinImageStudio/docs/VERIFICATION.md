# Verification for 0.5.0

Windows CI run [42458796328](https://github.com/abo2020mohmmed/Smart-Radar/actions/runs/42458796328) verified:

- 126 .NET tests, 8 media-copy tests and 13 package-metadata tests.
- Actual DISM capture/mount/commit/remount of a tiny WIM, followed by recovery-compressed ESD export and DISM image-info validation. Working WIM bytes remain unchanged by export.
- Fresh final-export orchestration, install.wim/esd/swm alternative cleanup without deleting boot.wim, HTML data encoding, saved-image audit discard and project state, using injected external-tool runners where indicated.
- Native isolated offline registry hives including both x86/x64 Win32 uninstall registrations and hive unloading. The full audit was not performed against a complete real Windows ISO in CI.
- Unsigned installer rejected; actual Microsoft ADK bootstrapper downloaded from the pinned official URL, Authenticode verified, adksetup.exe executed to install Deployment Tools, and installed oscdimg discovered.
- Native Windows PowerShell uses a known built-in module path, avoiding inherited PowerShell 7 module conflicts. Signature verification remains mandatory.
- Self-contained Windows x64 build and actual WPF startup/primary action: Next commits the test-owned mounted WIM, persists Committed, opens output and finishes WIM-only output without claiming an ISO exists.

Local Linux verification: .NET SDK 10.0.403, release solution build with zero warnings/errors, 124 .NET tests, 6 media-copy tests and 11 package-metadata tests passed.

Fixtures are not complete Windows installations. These results do not verify the user's ISO, final size reduction, installation/boot, runtime printing/Store behavior or Windows Update after customization. Native ESD export and official ADK setup are tested; final ISO boot still requires virtual-machine acceptance testing. Program inventory does not prove malware absence or enumerate every portable/per-user app. No claim of zero defects or full NTLite parity is made.

Further commits rerun the Windows workflow. Use the latest successful artifact matching the desired commit.
