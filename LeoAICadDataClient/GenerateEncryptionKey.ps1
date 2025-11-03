# PowerShell script to generate encryption key at compile time
# Generates a C# file with random AES key and IV

$outputFile = "$PSScriptRoot\Utilities\EncryptionKeyGenerated.cs"

# Generate random 256-bit (32 bytes) key
$key = New-Object byte[] 32
$rng = [System.Security.Cryptography.RNGCryptoServiceProvider]::new()
$rng.GetBytes($key)

# Generate random 128-bit (16 bytes) IV
$iv = New-Object byte[] 16
$rng.GetBytes($iv)
$rng.Dispose()

# Convert to C# byte array format
$keyString = ($key | ForEach-Object { "0x{0:X2}" -f $_ }) -join ", "
$ivString = ($iv | ForEach-Object { "0x{0:X2}" -f $_ }) -join ", "

# Generate C# source file
$csContent = @"
// This file is auto-generated at compile time - DO NOT EDIT
// Each build generates a unique encryption key

namespace LeoAICadDataClient.Utilities
{
    internal static class EncryptionKeyGenerated
    {
        internal static readonly byte[] Key = new byte[] { $keyString };
        internal static readonly byte[] IV = new byte[] { $ivString };
    }
}
"@

# Write to file
$csContent | Out-File -FilePath $outputFile -Encoding UTF8 -NoNewline

Write-Host "Generated new encryption key at: $outputFile"
