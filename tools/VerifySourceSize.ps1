param(
    [ValidateRange(1, 100000)]
    [int]$MaxLines = 1000
)

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceDirectories = @(
    'InternalManagement.Desktop',
    'src',
    'tests'
)
$oversizedFiles = @(
    foreach ($directory in $sourceDirectories) {
        $sourcePath = Join-Path $repositoryRoot $directory
        Get-ChildItem -LiteralPath $sourcePath -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '[\\/](obj|bin|Migrations)[\\/]' } |
            ForEach-Object {
                $lineCount = [System.IO.File]::ReadAllLines($_.FullName).Length
                if ($lineCount -gt $MaxLines) {
                    [pscustomobject]@{
                        File = [System.IO.Path]::GetRelativePath($repositoryRoot, $_.FullName)
                        Lines = $lineCount
                    }
                }
            }
    }
)

if ($oversizedFiles.Count -gt 0) {
    $oversizedFiles | Sort-Object Lines -Descending | Format-Table -AutoSize | Out-String | Write-Error
    throw "Found $($oversizedFiles.Count) handwritten C# file(s) above $MaxLines lines."
}

Write-Output "Source-size check passed: no handwritten C# file exceeds $MaxLines lines."
