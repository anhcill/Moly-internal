param(
    [string]$ApiBaseUrl = "https://internal-management-api-production-6f08.up.railway.app/"
)

$env:MOLY_API_BASE_URL = $ApiBaseUrl
dotnet watch --project (Join-Path $PSScriptRoot "..\InternalManagement.Desktop") --no-restore
