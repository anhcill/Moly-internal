$publicDatabaseUrl = (& railway run --service Postgres --environment production -- powershell -NoProfile -Command '$env:DATABASE_PUBLIC_URL').Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($publicDatabaseUrl)) {
    throw "Không lấy được DATABASE_PUBLIC_URL từ Railway Postgres. Kiểm tra railway login/link."
}

$env:RAILWAY_DATABASE_PUBLIC_URL = $publicDatabaseUrl
$childScript = @'
$databaseUri = [Uri]$env:RAILWAY_DATABASE_PUBLIC_URL
$credentials = $databaseUri.UserInfo.Split(':', 2)
if ($credentials.Count -ne 2 -or [string]::IsNullOrWhiteSpace($databaseUri.Host)) {
    throw "DATABASE_PUBLIC_URL của Railway không đúng định dạng PostgreSQL."
}

$username = [Uri]::UnescapeDataString($credentials[0])
$password = [Uri]::UnescapeDataString($credentials[1])
$database = $databaseUri.AbsolutePath.TrimStart('/')
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Host=$($databaseUri.Host);Port=$($databaseUri.Port);Database=$database;Username=$username;Password=$password;SSL Mode=Require;Trust Server Certificate=true"
$env:DatabaseStartup__ApplyMigrationsOnStartup = "false"
$env:DatabaseStartup__RequireConnectionOnStartup = "true"

dotnet watch --project "src\InternalManagement.Api" --launch-profile InternalManagement.Api
'@
$encodedScript = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($childScript))
railway run --service internal-management-api --environment production -- powershell -NoProfile -EncodedCommand $encodedScript
