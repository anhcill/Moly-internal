[CmdletBinding()]
param(
    [string]$ComposeFile,
    [string]$OutputDirectory,
    [string]$Service = "postgres"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path $PSScriptRoot "docker-compose.yml"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "backups"
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Không tìm thấy Docker CLI. Hãy cài Docker Desktop và thử lại."
}

$composePath = [System.IO.Path]::GetFullPath($ComposeFile)
$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "Không tìm thấy file Docker Compose: $composePath"
}

[void](New-Item -ItemType Directory -Force -Path $outputPath)
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$backupPath = Join-Path $outputPath "moli-$timestamp.dump"
$database = if ([string]::IsNullOrWhiteSpace($env:POSTGRES_DB)) { "moli_internal_management" } else { $env:POSTGRES_DB }
$username = if ([string]::IsNullOrWhiteSpace($env:POSTGRES_USER)) { "postgres" } else { $env:POSTGRES_USER }
$password = $env:POSTGRES_PASSWORD

$arguments = @("compose", "-f", $composePath, "exec", "-T")
if (-not [string]::IsNullOrWhiteSpace($password)) {
    $arguments += @("-e", "PGPASSWORD=$password")
}
$arguments += @(
    $Service,
    "pg_dump",
    "--format=custom",
    "--no-owner",
    "--no-privileges",
    "--username=$username",
    "--dbname=$database"
)

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = "docker"
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$quotedArguments = foreach ($argument in $arguments) {
    '"' + $argument.Replace('"', '\"') + '"'
}
$startInfo.Arguments = $quotedArguments -join ' '

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
[void]$process.Start()
$errorTask = $process.StandardError.ReadToEndAsync()

try {
    $stream = [System.IO.File]::Create($backupPath)
    try {
        $process.StandardOutput.BaseStream.CopyTo($stream)
    }
    finally {
        $stream.Dispose()
    }
    $process.WaitForExit()
    $errorOutput = $errorTask.GetAwaiter().GetResult()

    if ($process.ExitCode -ne 0) {
        Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
        throw "pg_dump thất bại (exit $($process.ExitCode)): $errorOutput"
    }
}
catch {
    if (Test-Path -LiteralPath $backupPath) {
        Remove-Item -LiteralPath $backupPath -Force -ErrorAction SilentlyContinue
    }
    throw
}
finally {
    $process.Dispose()
}

$hash = Get-FileHash -LiteralPath $backupPath -Algorithm SHA256
[PSCustomObject]@{
    BackupFile = $backupPath
    Database = $database
    CreatedAtUtc = [DateTime]::UtcNow
    SizeBytes = (Get-Item -LiteralPath $backupPath).Length
    Sha256 = $hash.Hash
}
