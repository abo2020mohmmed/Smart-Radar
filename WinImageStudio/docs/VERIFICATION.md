# Verification for 0.3.0

Local Linux verification: .NET SDK 10.0.403, release solution build with zero warnings/errors, 102 .NET tests, 6 media-copy tests and 11 package-metadata tests passed. Windows x64 self-contained publish succeeded.

Windows CI run [42418242523](https://github.com/abo2020mohmmed/Smart-Radar/actions/runs/42418242523) passed:

- 103 .NET tests, including actual readonly-file deletion, leaf/parent symbolic-link rejection, WOF compression verified through WofIsExternalFile, and DISM capture/mount/delete/commit/remount of a small fixture WIM.
- 8 PowerShell media-copy regression tests and 13 package-metadata tests, including isolated offline registry hives.
- Windows PowerShell 5.1 script parsing and self-contained Windows x64 publication.
- Actual WPF executable startup, five numbered controls, no false initial completion markers, manual/strong path switching, and preparation disabled without a project.

The tiny fixture WIM is not a complete Windows installation. These tests do not verify the user's specific ISO, complete aggressive servicing, final ISO creation, Windows installation/boot, Store/printing runtime behavior or Windows Update after customization. These scenarios require a compatible real ISO and virtual-machine acceptance testing. No claim of zero defects, production certification or full NTLite parity is made.

Further commits rerun the same Windows workflow. Use the latest green run for the corresponding package.
