#!/usr/bin/env pwsh
<#
.SYNOPSIS
    TokenRelay Token Encryption Utility
    
.DESCRIPTION
    This script encrypts and decrypts authentication tokens for the TokenRelay application.
    It uses AES encryption with a provided encryption key.

.PARAMETER Token
    The plain text token to encrypt

.PARAMETER EncryptionKey
    The encryption key to use (will be padded/truncated to 32 bytes). 
    For encryption: If not provided, a secure random key will be generated.
    For decryption: This parameter is mandatory.

.PARAMETER EncryptedToken
    The encrypted token to decrypt (must start with "ENC:")

.PARAMETER Decrypt
    Switch to decrypt instead of encrypt

.EXAMPLE
    .\Encrypt-Token.ps1 -Token "my-secret-token" -EncryptionKey "my-encryption-key-123"
    
.EXAMPLE
    .\Encrypt-Token.ps1 -Token "my-secret-token"
    Generates a random encryption key and encrypts the token
    
.EXAMPLE
    .\Encrypt-Token.ps1 -EncryptedToken "ENC:base64data..." -EncryptionKey "my-encryption-key-123" -Decrypt

.NOTES
    This script uses the same encryption logic as the TokenRelay application.
    Keep your encryption keys secure and use strong, unique keys for production.
#>

param(
    [Parameter(Mandatory=$false)]
    [string]$Token,
    
    [Parameter(Mandatory=$false)]
    [string]$EncryptionKey,
    
    [Parameter(Mandatory=$false)]
    [string]$EncryptedToken,
    
    [Parameter(Mandatory=$false)]
    [switch]$Decrypt
)

function New-SecureEncryptionKey {
    <#
    .SYNOPSIS
        Generates a secure random encryption key
    .DESCRIPTION
        Creates a cryptographically secure random 32-character key suitable for AES encryption
    #>
    try {
        # Generate 32 random bytes
        $randomBytes = New-Object byte[] 32
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        $rng.GetBytes($randomBytes)
        
        # Convert to base64 for easier handling (will be trimmed to 32 chars)
        $base64Key = [Convert]::ToBase64String($randomBytes)
        
        # Take first 32 characters to ensure consistent key length
        return $base64Key.Substring(0, 32)
    }
    catch {
        Write-Error "Failed to generate encryption key: $($_.Exception.Message)"
        return $null
    }
    finally {
        if ($rng) { $rng.Dispose() }
    }
}

function Encrypt-TokenAES {
    param(
        [string]$PlainToken,
        [string]$Key
    )
    
    try {
        # Ensure key is exactly 32 bytes
        $keyBytes = [System.Text.Encoding]::UTF8.GetBytes($Key.PadRight(32).Substring(0, 32))
        
        # Create AES instance
        $aes = [System.Security.Cryptography.Aes]::Create()
        $aes.Key = $keyBytes
        $aes.GenerateIV()
        
        # Encrypt the token
        $encryptor = $aes.CreateEncryptor()
        $plainBytes = [System.Text.Encoding]::UTF8.GetBytes($PlainToken)
        $encryptedBytes = $encryptor.TransformFinalBlock($plainBytes, 0, $plainBytes.Length)
        
        # Combine IV and encrypted data
        $result = $aes.IV + $encryptedBytes
        
        # Return with ENC: prefix
        $base64Result = [Convert]::ToBase64String($result)
        return "ENC:$base64Result"
    }
    catch {
        Write-Error "Encryption failed: $($_.Exception.Message)"
        return $null
    }
    finally {
        if ($aes) { $aes.Dispose() }
        if ($encryptor) { $encryptor.Dispose() }
    }
}

function Decrypt-TokenAES {
    param(
        [string]$EncryptedToken,
        [string]$Key
    )
    
    try {
        # Remove ENC: prefix
        if (-not $EncryptedToken.StartsWith("ENC:")) {
            throw "Encrypted token must start with 'ENC:'"
        }
        
        $actualEncryptedData = $EncryptedToken.Substring(4)
        $encryptedBytes = [Convert]::FromBase64String($actualEncryptedData)
        
        # Ensure key is exactly 32 bytes
        $keyBytes = [System.Text.Encoding]::UTF8.GetBytes($Key.PadRight(32).Substring(0, 32))
        
        # Extract IV (first 16 bytes) and ciphertext
        $iv = $encryptedBytes[0..15]
        $cipherText = $encryptedBytes[16..($encryptedBytes.Length - 1)]
        
        # Create AES instance
        $aes = [System.Security.Cryptography.Aes]::Create()
        $aes.Key = $keyBytes
        $aes.IV = $iv
        
        # Decrypt the token
        $decryptor = $aes.CreateDecryptor()
        $decryptedBytes = $decryptor.TransformFinalBlock($cipherText, 0, $cipherText.Length)
        
        return [System.Text.Encoding]::UTF8.GetString($decryptedBytes)
    }
    catch {
        Write-Error "Decryption failed: $($_.Exception.Message)"
        return $null
    }
    finally {
        if ($aes) { $aes.Dispose() }
        if ($decryptor) { $decryptor.Dispose() }
    }
}

# Main script logic
# Show help if no parameters are provided
if (-not $Token -and -not $EncryptedToken -and -not $Decrypt) {
    Write-Host ""
    Write-Host "=== TokenRelay Token Encryption Utility ===" -ForegroundColor Magenta
    Write-Host ""
    Write-Host "Usage Examples:" -ForegroundColor Yellow
    Write-Host "  Encrypt with custom key: " -ForegroundColor White -NoNewline
    Write-Host ".\Encrypt-Token.ps1 -Token 'my-secret-token' -EncryptionKey 'my-key-123'" -ForegroundColor Green
    Write-Host "  Encrypt with auto-generated key: " -ForegroundColor White -NoNewline
    Write-Host ".\Encrypt-Token.ps1 -Token 'my-secret-token'" -ForegroundColor Green
    Write-Host "  Decrypt: " -ForegroundColor White -NoNewline  
    Write-Host ".\Encrypt-Token.ps1 -EncryptedToken 'ENC:...' -EncryptionKey 'my-key-123' -Decrypt" -ForegroundColor Green
    Write-Host ""
    Write-Host "Notes:" -ForegroundColor Yellow
    Write-Host "  • If no encryption key is provided for encryption, a secure random key will be generated" -ForegroundColor Gray
    Write-Host "  • Encryption keys are required for decryption" -ForegroundColor Gray
    Write-Host "  • Keep your encryption keys secure and backed up" -ForegroundColor Gray
    Write-Host ""
    exit 0
}

if ($Decrypt) {
    # For decryption, encryption key is mandatory
    if (-not $EncryptionKey) {
        Write-Error "EncryptionKey parameter is required when using -Decrypt switch"
        exit 1
    }
    
    if (-not $EncryptedToken) {
        Write-Error "EncryptedToken parameter is required when using -Decrypt switch"
        exit 1
    }
    
    Write-Host "Decrypting token..." -ForegroundColor Yellow
    $result = Decrypt-TokenAES -EncryptedToken $EncryptedToken -Key $EncryptionKey
    
    if ($result) {
        Write-Host "Decrypted token: " -ForegroundColor Green -NoNewline
        Write-Host $result -ForegroundColor Cyan
    }
}
else {
    if (-not $Token) {
        Write-Error "Token parameter is required for encryption"
        exit 1
    }
    
    # Generate encryption key if not provided
    if (-not $EncryptionKey) {
        Write-Host "No encryption key provided. Generating secure random key..." -ForegroundColor Yellow
        $EncryptionKey = New-SecureEncryptionKey
        
        if (-not $EncryptionKey) {
            Write-Error "Failed to generate encryption key"
            exit 1
        }
        
        Write-Host "Generated encryption key: " -ForegroundColor Green -NoNewline
        Write-Host $EncryptionKey -ForegroundColor Cyan
        Write-Host "⚠️  Save this key securely - you'll need it for decryption!" -ForegroundColor Yellow
        Write-Host ""
    }
    
    Write-Host "Encrypting token..." -ForegroundColor Yellow
    $result = Encrypt-TokenAES -PlainToken $Token -Key $EncryptionKey
      if ($result) {
        Write-Host "Encrypted token: " -ForegroundColor Green -NoNewline
        Write-Host $result -ForegroundColor Cyan
        Write-Host ""
        
        Write-Host "Configuration for tokenrelay.json:" -ForegroundColor Magenta
        Write-Host '"token": "' -ForegroundColor White -NoNewline
        Write-Host $result -ForegroundColor Cyan -NoNewline
        Write-Host '"' -ForegroundColor White
        
        if ($EncryptionKey) {
            Write-Host ""
            Write-Host "Encryption key used: " -ForegroundColor Magenta -NoNewline
            Write-Host $EncryptionKey -ForegroundColor Cyan        }
    }
}
