<#
.SYNOPSIS
    Starts the DotFlow API and Blazor UI together for local development.

.DESCRIPTION
    Launches both projects in the current console and streams their output. Press Ctrl-C once
    to stop both (each child process is terminated, tree and all), just like a single `dotnet run`.

    Both projects are started on the same scheme so the browser does not block cross-origin
    (or mixed-content) API calls.

.PARAMETER Profile
    The launch profile to use for both projects: 'http' (default) or 'https'.

.EXAMPLE
    .\run-dev.ps1
    Starts the API on http://localhost:5213 and the UI on http://localhost:5277.

.EXAMPLE
    .\run-dev.ps1 -Profile https
    Starts both over https (API https://localhost:7018, UI https://localhost:7188).
    Note: for https you must also point the UI at the https API via
    Workflow.UI/Workflow.UI.Client/wwwroot/appsettings.json (Api:BaseUrl).
#>
[CmdletBinding()]
param(
    [ValidateSet('http', 'https')]
    [string]$Profile = 'http'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Strip inherited hot-reload / browser-refresh env vars so this behaves like a plain `dotnet run`.
# If launched from an IDE terminal (Rider/VS) or a `dotnet watch` shell, these would otherwise
# make the app inject a reference to /_framework/blazor-hotreload.js that a plain run doesn't serve.
$env:DOTNET_MODIFIABLE_ASSEMBLIES = $null
$env:__ASPNETCORE_BROWSER_TOOLS = $null
$env:ASPNETCORE_AUTO_RELOAD_WS_ENDPOINT = $null
$env:ASPNETCORE_AUTO_RELOAD_WS_KEY = $null
$env:DOTNET_HOTRELOAD_NAMEDPIPE_NAME = $null
$env:DOTNET_WATCH = $null

$procs = New-Object System.Collections.Generic.List[System.Diagnostics.Process]

function Stop-All {
    foreach ($p in $procs) {
        try {
            if ($p -and -not $p.HasExited) {
                # Terminate the whole process tree; `dotnet run` spawns the actual app as a child.
                & taskkill.exe /T /F /PID $p.Id 2>$null | Out-Null
            }
        } catch {
            # Best-effort cleanup — ignore processes that already exited.
        }
    }
}

function Start-Component {
    param([string]$Name, [string[]]$ProjectArgs)

    Write-Host "→ starting $Name ($Profile)..." -ForegroundColor Cyan
    $args = @('run', '--project') + $ProjectArgs + @('--launch-profile', $Profile)
    $p = Start-Process -FilePath 'dotnet' -ArgumentList $args -WorkingDirectory $root -NoNewWindow -PassThru
    $procs.Add($p)
    return $p
}

try {
    $api = Start-Component -Name 'Workflow.Api' -ProjectArgs @('Workflow.Api')
    $ui  = Start-Component -Name 'Workflow.UI'  -ProjectArgs @('Workflow.UI/Workflow.UI')

    if ($Profile -eq 'http') {
        Write-Host ''
        Write-Host 'DotFlow is starting up:' -ForegroundColor Green
        Write-Host '  API : http://localhost:5213  (Swagger at /swagger)'
        Write-Host '  UI  : http://localhost:5277'
        Write-Host ''
        Write-Host 'Press Ctrl-C to stop both.' -ForegroundColor Yellow
    } else {
        Write-Host ''
        Write-Host 'DotFlow is starting up (https):' -ForegroundColor Green
        Write-Host '  API : https://localhost:7018  (Swagger at /swagger)'
        Write-Host '  UI  : https://localhost:7188'
        Write-Host ''
        Write-Host 'Press Ctrl-C to stop both.' -ForegroundColor Yellow
    }

    while ($true) {
        if ($api.HasExited -or $ui.HasExited) {
            Write-Host 'A component exited; shutting the other down...' -ForegroundColor Yellow
            break
        }
        Start-Sleep -Milliseconds 400
    }
} finally {
    Stop-All
}
