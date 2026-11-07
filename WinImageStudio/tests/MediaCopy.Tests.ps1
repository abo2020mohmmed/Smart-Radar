$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot '../src/WinImageStudio.Desktop/Scripts/MediaCopy.ps1')
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('WisMediaCopy-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($testRoot)
$script:passed = 0
$script:failed = 0
function Test-Case([string]$Name, [scriptblock]$Body) {
    try { & $Body; $script:passed++; Write-Output ('PASS ' + $Name) }
    catch { $script:failed++; [Console]::Error.WriteLine(('FAIL ' + $Name + ': ' + $_.Exception.ToString())) }
}
function Assert-Equal($Expected, $Actual) {
    if ($Expected -cne $Actual) { throw "Expected '$Expected', got '$Actual'." }
}
function Assert-Throws([scriptblock]$Body) {
    $threw = $false
    try { & $Body | Out-Null } catch { $threw = $true }
    if (!$threw) { throw 'Expected an exception.' }
}
function New-TestSource([string]$Name) {
    $source = Join-Path $testRoot $Name
    [void][IO.Directory]::CreateDirectory($source)
    return $source
}
try {
    Test-Case 'Hidden/System/ReadOnly sources/ws.dat is copied and remains writable' {
        $source = New-TestSource 'hidden source'
        [void][IO.Directory]::CreateDirectory((Join-Path $source 'sources'))
        $file = Join-Path $source 'sources/ws.dat'
        [IO.File]::WriteAllBytes($file, [byte[]]@(0,1,2,3,255))
        if ($env:OS -eq 'Windows_NT') {
            [IO.File]::SetAttributes($file, ([IO.FileAttributes]::Hidden -bor [IO.FileAttributes]::System -bor [IO.FileAttributes]::ReadOnly))
            $legacyItems = @(Get-Item -LiteralPath $file -ErrorAction SilentlyContinue)
            Write-Output ('Legacy Get-Item without Force returned ' + $legacyItems.Count + ' item(s).')
        }
        $destination = Join-Path $testRoot 'hidden destination'
        $copy = Copy-WisMediaDirectory -SourceRoot $source -DestinationRoot $destination
        $copied = Join-Path $destination 'sources/ws.dat'
        Assert-Equal 1 $copy.FilesCopied
        Assert-Equal 5 $copy.BytesCopied
        Assert-Equal $true ([IO.File]::Exists($copied))
        Assert-Equal ([Convert]::ToBase64String([IO.File]::ReadAllBytes($file))) ([Convert]::ToBase64String([IO.File]::ReadAllBytes($copied)))
        $attributes = [IO.File]::GetAttributes($copied)
        Assert-Equal 0 ([int]($attributes -band [IO.FileAttributes]::ReadOnly))
        if ($env:OS -eq 'Windows_NT') {
            Assert-Equal ([int][IO.FileAttributes]::Hidden) ([int]($attributes -band [IO.FileAttributes]::Hidden))
            Assert-Equal ([int][IO.FileAttributes]::System) ([int]($attributes -band [IO.FileAttributes]::System))
        }
    }
    Test-Case 'Spaces, brackets and Unicode filenames are literal' {
        $source = New-TestSource 'source [1] with spaces'
        $name = ([string][char]0x0645) + ([string][char]0x0644) + ([string][char]0x0641) + ' [1].txt'
        [IO.File]::WriteAllText((Join-Path $source $name), 'literal names')
        $destination = Join-Path $testRoot 'destination [2] with spaces'
        $copy = Copy-WisMediaDirectory $source $destination
        Assert-Equal 1 $copy.FilesCopied
        Assert-Equal 'literal names' ([IO.File]::ReadAllText((Join-Path $destination $name)))
    }
    Test-Case 'Empty files and nested directories are retained' {
        $source = New-TestSource 'nested'
        [void][IO.Directory]::CreateDirectory((Join-Path $source 'a/b/empty'))
        [IO.File]::WriteAllBytes((Join-Path $source 'a/zero.bin'), [byte[]]@())
        [IO.File]::WriteAllBytes((Join-Path $source 'a/b/data.bin'), [byte[]]@(17,18))
        $destination = Join-Path $testRoot 'nested-copy'
        $copy = Copy-WisMediaDirectory $source $destination
        Assert-Equal 2 $copy.FilesCopied
        Assert-Equal 2 $copy.BytesCopied
        Assert-Equal $true ([IO.Directory]::Exists((Join-Path $destination 'a/b/empty')))
    }
    Test-Case 'Existing destination is rejected and its contents are preserved' {
        $source = New-TestSource 'overwrite-source'
        $destination = New-TestSource 'overwrite-destination'
        [IO.File]::WriteAllText((Join-Path $destination 'keep.txt'), 'keep')
        Assert-Throws { Copy-WisMediaDirectory $source $destination }
        Assert-Equal 'keep' ([IO.File]::ReadAllText((Join-Path $destination 'keep.txt')))
    }
    Test-Case 'Nested destination is rejected before it is created' {
        $source = New-TestSource 'recursive-source'
        $destination = Join-Path $source 'new-copy'
        Assert-Throws { Copy-WisMediaDirectory $source $destination }
        Assert-Equal $false ([IO.Directory]::Exists($destination))
    }
    Test-Case 'Missing source fails instead of producing a successful empty copy' {
        Assert-Throws { Copy-WisMediaDirectory (Join-Path $testRoot 'missing') (Join-Path $testRoot 'missing-copy') }
    }
    if ($env:OS -eq 'Windows_NT') {
        Test-Case 'A locked source file is reported as a failure, never skipped' {
            $source = New-TestSource 'locked-source'
            $file = Join-Path $source 'locked.dat'
            [IO.File]::WriteAllText($file, 'locked')
            $lock = [IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            try { Assert-Throws { Copy-WisMediaDirectory $source (Join-Path $testRoot 'locked-copy') } }
            finally { $lock.Dispose() }
        }
        Test-Case 'Directory junctions are rejected without following them' {
            $source = New-TestSource 'junction-source'
            $outside = New-TestSource 'outside'
            [IO.File]::WriteAllText((Join-Path $outside 'keep.txt'), 'untouched')
            $link = Join-Path $source 'redirect'
            New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
            try {
                Assert-Throws { Copy-WisMediaDirectory $source (Join-Path $testRoot 'junction-copy') }
                Assert-Equal 'untouched' ([IO.File]::ReadAllText((Join-Path $outside 'keep.txt')))
            } finally { [IO.Directory]::Delete($link) }
        }
    }
} finally {
    foreach ($file in [IO.Directory]::EnumerateFiles($testRoot, '*', [IO.SearchOption]::AllDirectories)) { [IO.File]::SetAttributes($file, [IO.FileAttributes]::Normal) }
    [IO.Directory]::Delete($testRoot, $true)
}
Write-Output ("Media copy: {0} passed; {1} failed." -f $script:passed,$script:failed)
if ($script:failed -gt 0) { exit 1 }
exit 0
