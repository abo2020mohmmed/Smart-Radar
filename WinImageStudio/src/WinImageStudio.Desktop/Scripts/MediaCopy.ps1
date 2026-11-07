function Copy-WisMediaDirectory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$SourceRoot,
        [Parameter(Mandatory=$true)][string]$DestinationRoot
    )
    $sourceFull = [IO.Path]::GetFullPath($SourceRoot)
    $destinationFull = [IO.Path]::GetFullPath($DestinationRoot)
    $trimChars = [char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $sourcePrefix = $sourceFull.TrimEnd($trimChars) + [IO.Path]::DirectorySeparatorChar
    $destinationPrefix = $destinationFull.TrimEnd($trimChars) + [IO.Path]::DirectorySeparatorChar
    $comparison = [StringComparison]::OrdinalIgnoreCase
    if (![IO.Directory]::Exists($sourceFull)) { throw "Media source directory does not exist: $sourceFull" }
    if (($sourceFull.TrimEnd($trimChars)).Equals($destinationFull.TrimEnd($trimChars), $comparison) -or $destinationFull.StartsWith($sourcePrefix, $comparison)) {
        throw 'The media destination must not be the source or a child of the source.'
    }
    if ([IO.Directory]::Exists($destinationFull) -or [IO.File]::Exists($destinationFull)) {
        throw "Media destination already exists; refusing to overwrite it: $destinationFull"
    }
    if (([IO.File]::GetAttributes($sourceFull) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw 'The media source must not be a reparse point.'
    }
    [void][IO.Directory]::CreateDirectory($destinationFull)
    $pending = New-Object 'System.Collections.Generic.Stack[string]'
    $pending.Push($sourceFull)
    [long]$filesCopied = 0
    [long]$bytesCopied = 0
    [long]$directoriesCopied = 0
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in [IO.Directory]::EnumerateFileSystemEntries($directory)) {
            $entryFull = [IO.Path]::GetFullPath($entry)
            if (!$entryFull.StartsWith($sourcePrefix, $comparison)) { throw 'Source enumeration escaped the media root.' }
            $attributes = [IO.File]::GetAttributes($entryFull)
            if (($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Media contains an unsupported reparse point: $entryFull"
            }
            $relative = $entryFull.Substring($sourcePrefix.Length)
            $target = [IO.Path]::GetFullPath([IO.Path]::Combine($destinationFull, $relative))
            if (!$target.StartsWith($destinationPrefix, $comparison)) { throw 'Copy destination escaped the project media directory.' }
            if (($attributes -band [IO.FileAttributes]::Directory) -ne 0) {
                [void][IO.Directory]::CreateDirectory($target)
                $pending.Push($entryFull)
                $directoriesCopied++
            } else {
                try {
                    $expectedLength = ([IO.FileInfo]::new($entryFull)).Length
                    # Native .NET file APIs include Hidden/System files and treat
                    # brackets and other legal filename characters literally.
                    [IO.File]::Copy($entryFull, $target, $false)
                    $copiedAttributes = [IO.File]::GetAttributes($target)
                    $writableAttributes = [IO.FileAttributes]([int]$copiedAttributes -band (-bnot [int][IO.FileAttributes]::ReadOnly))
                    [IO.File]::SetAttributes($target, $writableAttributes)
                    $actualLength = ([IO.FileInfo]::new($target)).Length
                    if ($actualLength -ne $expectedLength) { throw 'The copied file length does not match the source.' }
                    $filesCopied++
                    $bytesCopied += $actualLength
                } catch {
                    throw [IO.IOException]::new("Cannot copy or verify media file '$relative' from '$entryFull' to '$target'. No file was skipped. " + $_.Exception.Message, $_.Exception)
                }
            }
        }
    }
    [pscustomobject]@{ FilesCopied=$filesCopied; BytesCopied=$bytesCopied; DirectoriesCopied=$directoriesCopied; Destination=$destinationFull }
}
