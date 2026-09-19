[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupFile,
    [string]$ComposeFile,
    [string]$Service = "postgres",
    [switch]$ConfirmRestore
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($ComposeFile)) {
    $ComposeFile = Join-Path $PSScriptRoot "docker-compose.yml"
}

if (-not $ConfirmRestore) {
    throw "Restore sẽ ghi đè dữ liệu hiện tại. Hãy chạy lại với -ConfirmRestore sau khi đã kiểm tra đúng file backup."
}
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Không tìm thấy Docker CLI. Hãy cài Docker Desktop và thử lại."
}

$composePath = [System.IO.Path]::GetFullPath($ComposeFile)
$backupPath = [System.IO.Path]::GetFullPath($BackupFile)
if (-not (Test-Path -LiteralPath $composePath -PathType Leaf)) {
    throw "Không tìm thấy file Docker Compose: $composePath"
}
if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) {
    throw "Không tìm thấy file backup: $backupPath"
}

$database = if ([string]::IsNullOrWhiteSpace($env:POSTGRES_DB)) { "moli_internal_management" } else { $env:POSTGRES_DB }
$username = if ([string]::IsNullOrWhiteSpace($env:POSTGRES_USER)) { "postgres" } else { $env:POSTGRES_USER }
$password = $env:POSTGRES_PASSWORD

$arguments = @("compose", "-f", $composePath, "exec", "-T")
if (-not [string]::IsNullOrWhiteSpace($password)) {
    $arguments += @("-e", "PGPASSWORD=$password")
}
$arguments += @(
    $Service,
    "pg_restore",
    "--clean",
    "--if-exists",
    "--exit-on-error",
    "--no-owner",
    "--no-privileges",
    "--username=$username",
    "--dbname=$database"
)

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = "docker"
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$quotedArguments = foreach ($argument in $arguments) {
    '"' + $argument.Replace('"', '\"') + '"'
}
$startInfo.Arguments = $quotedArguments -join ' '

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
[void]$process.Start()
$outputTask = $process.StandardOutput.ReadToEndAsync()
$errorTask = $process.StandardError.ReadToEndAsync()

try {
    $stream = [System.IO.File]::OpenRead($backupPath)
    try {
        $stream.CopyTo($process.StandardInput.BaseStream)
    }
    finally {
        $stream.Dispose()
        $process.StandardInput.Close()
    }
    $process.WaitForExit()
    $output = $outputTask.GetAwaiter().GetResult()
    $errorOutput = $errorTask.GetAwaiter().GetResult()

    if ($process.ExitCode -ne 0) {
        throw "pg_restore thất bại (exit $($process.ExitCode)): $errorOutput $output"
    }
}
finally {
    $process.Dispose()
}

[PSCustomObject]@{
    RestoredFile = $backupPath
    Database = $database
    RestoredAtUtc = [DateTime]::UtcNow
}
