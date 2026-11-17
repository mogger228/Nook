param([Parameter(ValueFromRemainingArguments=$true)][string[]]$ToolArguments)
$ErrorActionPreference='Stop'
$projectRoot=Split-Path -Parent $PSScriptRoot
$binary=Join-Path $projectRoot '.tools\codebase-memory-mcp\codebase-memory-mcp.exe'
if(!(Test-Path -LiteralPath $binary)) { throw 'Не найден codebase-memory-mcp. См. .codex/README.md.' }
$variables=@{ CBM_ALLOWED_ROOT=$projectRoot; CBM_CACHE_DIR=(Join-Path $projectRoot '.codex\cache\codebase-memory'); CBM_WORKERS='2'; CBM_MEM_BUDGET_MB='256' }
$previous=@{}
Push-Location -LiteralPath $projectRoot
try {
 foreach($name in $variables.Keys) { $previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process'); [Environment]::SetEnvironmentVariable($name,$variables[$name],'Process') }
 & $binary cli @ToolArguments
 if($LASTEXITCODE -ne 0) { throw "codebase-memory-mcp завершился с кодом $LASTEXITCODE" }
} finally {
 foreach($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name,$previous[$name],'Process') }
 Pop-Location
}
