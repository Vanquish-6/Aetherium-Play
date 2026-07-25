param(
    [string]$Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Path)) {
    throw "Provide the full path to setup.inx with -Path."
}

function Decrypt-SetupInx {
    param([byte[]]$Bytes)

    $buffer = [byte[]]::new($Bytes.Length)
    [Array]::Copy($Bytes, $buffer, $Bytes.Length)

    for ($i = 0; $i -lt $buffer.Length; $i++) {
        $v7 = 0xF1 -bxor $buffer[$i]
        $v8 = ($v7 -shr 1)
        if (($v7 -band 1) -ne 0) {
            $v8 = $v8 -bor 0x80
        }

        $v9 = ($v8 -shr 1)
        if (($v8 -band 1) -ne 0) {
            $v9 = $v9 -bor 0x80
        }

        $buffer[$i] = [byte](($v9 - ($i % 0x47)) -band 0xFF)
    }

    return $buffer
}

function Encrypt-SetupInx {
    param([byte[]]$Bytes)

    $buffer = [byte[]]::new($Bytes.Length)
    [Array]::Copy($Bytes, $buffer, $Bytes.Length)

    for ($i = 0; $i -lt $buffer.Length; $i++) {
        $value = ($buffer[$i] + ($i % 0x47)) -band 0xFF
        $rotated = ((($value -shl 2) -band 0xFF) -bor ($value -shr 6)) -band 0xFF
        $buffer[$i] = [byte](0xF1 -bxor $rotated)
    }

    return $buffer
}

function Find-PatternOffset {
    param(
        [byte[]]$Bytes,
        [byte[]]$Pattern
    )

    for ($i = 0; $i -le ($Bytes.Length - $Pattern.Length); $i++) {
        $matched = $true
        for ($j = 0; $j -lt $Pattern.Length; $j++) {
            if ($Bytes[$i + $j] -ne $Pattern[$j]) {
                $matched = $false
                break
            }
        }

        if ($matched) {
            return $i
        }
    }

    return -1
}

if (-not (Test-Path -LiteralPath $Path)) {
    throw "setup.inx not found: $Path"
}

$originalPattern = [byte[]]@(
    0x04,0x00,0x29,0x00,0x04,0x00,0x04,0x8E,0xFF,0x04,0x8F,0xFF,0x07,0x00,0x00,0x00,
    0x00,0x07,0x01,0x00,0x00,0x00,0x0D,0x00,0x03,0x00,0x05,0x8E,0xFF,0x04,0x8E,0xFF,
    0x06,0x01,0x00,0x36,0x04,0x00,0x02,0x00,0x07,0x01,0x00,0x00,0x00,0x05,0x8E,0xFF,
    0x05,0x00,0x01,0x00,0x07,0x03,0x00,0x00,0x00
)

$patchedPattern = [byte[]]@(
    0x04,0x00,0x29,0x00,0x04,0x00,0x04,0x8E,0xFF,0x04,0x8F,0xFF,0x07,0x00,0x00,0x00,
    0x00,0x07,0x01,0x00,0x00,0x00,0x0C,0x00,0x03,0x00,0x05,0x8E,0xFF,0x04,0x8E,0xFF,
    0x06,0x01,0x00,0x36,0x04,0x00,0x02,0x00,0x07,0x01,0x00,0x00,0x00,0x05,0x8E,0xFF,
    0x05,0x00,0x01,0x00,0x07,0x03,0x00,0x00,0x00
)

$encryptedBytes = [System.IO.File]::ReadAllBytes($Path)
$plainBytes = Decrypt-SetupInx -Bytes $encryptedBytes

$alreadyPatchedOffset = Find-PatternOffset -Bytes $plainBytes -Pattern $patchedPattern
if ($alreadyPatchedOffset -ge 0) {
    Write-Host ("IE version check already patched at decrypted offset 0x{0:X}." -f $alreadyPatchedOffset)
    exit 0
}

$offset = Find-PatternOffset -Bytes $plainBytes -Pattern $originalPattern
if ($offset -lt 0) {
    throw "Could not find the IE version check block in decrypted setup.inx."
}

$backupPath = "$Path.ie-version-check-backup"
if (-not (Test-Path -LiteralPath $backupPath)) {
    Copy-Item -LiteralPath $Path -Destination $backupPath
}

$plainBytes[$offset + 22] = 0x0C
$plainBytes[$offset + 23] = 0x00

$patchedBytes = Encrypt-SetupInx -Bytes $plainBytes
[System.IO.File]::WriteAllBytes($Path, $patchedBytes)

$verifyPlainBytes = Decrypt-SetupInx -Bytes ([System.IO.File]::ReadAllBytes($Path))
$verifyOffset = Find-PatternOffset -Bytes $verifyPlainBytes -Pattern $patchedPattern
if ($verifyOffset -lt 0) {
    throw "Patch verification failed for $Path"
}

Write-Host ("Patched IE version check at decrypted offset 0x{0:X}." -f $verifyOffset)
