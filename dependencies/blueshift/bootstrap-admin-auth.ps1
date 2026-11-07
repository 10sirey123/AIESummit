# Local operator bootstrap only. Tokens are written to files, never console output.
param(
    [Parameter(Mandatory)][string]$SettingsPath,
    [Parameter(Mandatory)][string]$TokenPath,
    [ValidateSet('Admin','Editor','Viewer')][string]$Role = 'Admin',
    [ValidatePattern('^[a-zA-Z0-9_.@-]{1,80}$')][string]$Subject = 'local-operator',
    [ValidateRange(1,240)][int]$Minutes = 60,
    [string[]]$EditorEntityTypes = @(),
    [switch]$EnableDevelopmentSignIn,
    [string]$DevelopmentPassphrasePath
)
$ErrorActionPreference = 'Stop'
$settingsFile = [IO.Path]::GetFullPath($SettingsPath)
$tokenFile = [IO.Path]::GetFullPath($TokenPath)
if ($settingsFile -eq $tokenFile) { throw 'Settings and token files must differ.' }
if ($EnableDevelopmentSignIn -and (-not $DevelopmentPassphrasePath -or
    [IO.Path]::GetFullPath($DevelopmentPassphrasePath) -in @($settingsFile, $tokenFile))) {
    throw 'Development sign-in requires a separate private passphrase file.'
}
foreach ($path in @($settingsFile, $tokenFile)) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
}
if (Test-Path -LiteralPath $settingsFile) {
    $settings = Get-Content -LiteralPath $settingsFile -Raw | ConvertFrom-Json -AsHashtable
    if (-not $settings.AdminAuth.SigningKey -or $settings.AdminAuth.Authority) {
        throw 'Local bootstrap requires an existing local SigningKey or a new settings file.'
    }
} else {
    $settings = @{ AdminAuth = @{
        Issuer = 'blueshift-local'; Audience = 'blueshift-admin'
        SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        EditorEntityTypes = $EditorEntityTypes
    } }
}
$key = [Convert]::FromBase64String($settings.AdminAuth.SigningKey)
if ($key.Length -lt 32) { throw 'Signing key must contain at least 32 random bytes.' }
function ConvertTo-Base64Url([byte[]]$Value) {
    [Convert]::ToBase64String($Value).TrimEnd('=').Replace('+','-').Replace('/','_')
}
function Write-PrivateText([string]$Path, [string]$Value) {
    if (-not (Test-Path -LiteralPath $Path)) { [IO.File]::WriteAllText($Path, '') }
    if (-not $IsWindows) { [IO.File]::SetUnixFileMode($Path, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite) }
    [IO.File]::WriteAllText($Path, $Value)
}
$now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
if ($EnableDevelopmentSignIn) {
    $phraseFile = [IO.Path]::GetFullPath($DevelopmentPassphrasePath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($phraseFile)) | Out-Null
    # Generate 96 random bits; never install a shared, hard-coded development password.
    $phrase = 'local-' + [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(12)).ToLowerInvariant()
    $salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
    $hash = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2($phrase, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA256, 32)
    $settings.AdminAuth.DevelopmentPassphraseHash = [Convert]::ToBase64String($salt) + ':' + [Convert]::ToBase64String($hash)
    Write-PrivateText $phraseFile $phrase
    Write-Host "Development passphrase written to $phraseFile. Restart the Development API to enable it; other environments ignore it."
}
$header = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes('{"alg":"HS256","typ":"JWT"}'))
$payload = @{ iss=$settings.AdminAuth.Issuer; aud=$settings.AdminAuth.Audience; sub=$Subject; role=$Role; iat=$now; nbf=$now; exp=$now+($Minutes*60) } | ConvertTo-Json -Compress
$unsigned = $header + '.' + (ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload)))
$signature = ConvertTo-Base64Url ([Security.Cryptography.HMACSHA256]::HashData($key, [Text.Encoding]::UTF8.GetBytes($unsigned)))
Write-PrivateText $settingsFile ($settings | ConvertTo-Json -Depth 10)
Write-PrivateText $tokenFile ($unsigned + '.' + $signature)
Write-Host "Local $Role token written to $tokenFile; expires in $Minutes minutes. Settings: $settingsFile"
