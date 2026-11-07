# Windows acceptance checklist

These checks are not claimed as executed in the Linux development environment.

- Build and publish Release on Windows x64 with .NET 10; approve UAC.
- Import official Windows 10/11 WIM, unencrypted ESD, BIOS/UEFI ISO and a complete SWM set.
- Verify original source hashes remain unchanged after all operations.
- Inspect all indexes, import one, mount, and compare inventory with stock DISM output.
- Enable an available feature; disable an optional feature; add compatible CAB/MSU/language package.
- Add a signed third-party INF with payload files; remove only an oem*.inf driver.
- Change a noncritical service; confirm boot/system/kernel service edits are refused.
- Write all six registry value types; verify SOFTWARE/SYSTEM changes offline, including CurrentControlSet resolution; verify host registry has no residual WIS_* hives.
- Stage an inert test installer and verify execution only inside the installed VM.
- Generate unattended XML, verify installed language and Windows time-zone ID, and test setup.
- Commit, rebuild ISO and boot separately under UEFI and legacy BIOS where supported.
- Kill the GUI during a test servicing step; reopen the project and discard/recover its mount only.
- Test cancellation before a step and during an active DISM step; the active step must finish before the next is skipped.
- Test locked files, write failure, disk-full, incompatible package, nonempty mount path, tampered project path, symlink/reparse point, malformed JSON and an existing foreign SetupComplete.cmd.
- Install resulting image in an isolated VM; validate boot, sign-in, networking, Windows Update and Windows Recovery.
