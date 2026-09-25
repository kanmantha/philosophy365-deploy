param(
    [Parameter(Mandatory = $true)][string]$RenderApiKey,
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [Parameter(Mandatory = $true)][string]$NeonConnString
)

$ErrorActionPreference = "Stop"
$base = "https://api.render.com/v1"
$headers = @{ Authorization = "Bearer $RenderApiKey" }

function Invoke-Render($Method, $Path, $Body = $null) {
    $params = @{ Uri = "$base$Path"; Method = $Method; Headers = $headers; ContentType = "application/json" }
    if ($null -ne $Body) { $params.Body = ($Body | ConvertTo-Json -Depth 10) }
    Invoke-RestMethod @params
}

function Try-Render($ScriptBlock) {
    try { & $ScriptBlock }
    catch {
        $resp = $_.Exception.Response
        if ($null -ne $resp) {
            $rd = New-Object System.IO.StreamReader($resp.GetResponseStream())
            $body = $rd.ReadToEnd()
            Write-Host "ERROR ($([int]$resp.StatusCode)): $body" -ForegroundColor Red
        } else { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red }
        throw
    }
}

# 1. Resolve workspace/owner
$owners = Try-Render { Invoke-Render GET "/owners" }
$owner = $owners | Where-Object { $_.owner.type -eq "workspace" } | Select-Object -First 1
if (-not $owner) { $owner = $owners | Select-Object -First 1 }
$ownerId = $owner.owner.id
$ownerName = $owner.owner.name
Write-Host "Owner: $ownerName ($ownerId)"

# 2. Database: use the provided Neon connection string (no Render Postgres)
$pgConn = $NeonConnString
Write-Host "Using external Neon Postgres."

# 3. Create three Docker web services from the public repo
$api = Try-Render {
    Invoke-Render POST "/services" @{
        type = "web_service"
        name = "philosophy-api"
        ownerId = $ownerId
        repo = $RepoUrl
        branch = "main"
        autoDeploy = "no"
        serviceDetails = @{
            runtime = "docker"
            envSpecificDetails = @{
                dockerfilePath = "./Dockerfile.api"
                dockerContext = "./"
            }
            healthCheckPath = "/swagger/v1/swagger.json"
            plan = "free"
            region = "oregon"
            numInstances = 1
        }
        envVars = @(
            @{ key = "PORT"; value = "8080" },
            @{ key = "ASPNETCORE_ENVIRONMENT"; value = "Production" },
            @{ key = "ASPNETCORE_FORWARDEDHEADERS_ENABLED"; value = "true" },
            @{ key = "Database__Provider"; value = "Postgres" },
            @{ key = "ConnectionStrings__Default"; value = $pgConn },
            @{ key = "Jwt__Key"; generateValue = $true },
            @{ key = "Jwt__Issuer"; value = "AIPhilosophy365" },
            @{ key = "Jwt__Audience"; value = "AIPhilosophy365Clients" },
            @{ key = "Seed__AdminEmail"; value = "admin@philosophy365.app" },
            @{ key = "Seed__AdminPassword"; value = "Admin@12345!" },
            @{ key = "Mock__FfmpegPath"; value = "ffmpeg" }
        )
    }
}
$apiId = $api.service.id
$apiUrl = $api.service.serviceDetails.url
$apiDeploy = $api.deployId
Write-Host "API created: $apiUrl"

$web = Try-Render {
    Invoke-Render POST "/services" @{
        type = "web_service"
        name = "philosophy-web"
        ownerId = $ownerId
        repo = $RepoUrl
        branch = "main"
        autoDeploy = "no"
        serviceDetails = @{
            runtime = "docker"
            envSpecificDetails = @{
                dockerfilePath = "./Dockerfile.web"
                dockerContext = "./"
            }
            healthCheckPath = "/"
            plan = "free"
            region = "oregon"
            numInstances = 1
        }
        envVars = @(
            @{ key = "PORT"; value = "8080" },
            @{ key = "ASPNETCORE_ENVIRONMENT"; value = "Production" },
            @{ key = "ASPNETCORE_FORWARDEDHEADERS_ENABLED"; value = "true" },
            @{ key = "Api__BaseUrl"; value = $apiUrl }
        )
    }
}
$webId = $web.service.id
$webUrl = $web.service.serviceDetails.url
Write-Host "Web created: $webUrl"

$worker = Try-Render {
    Invoke-Render POST "/services" @{
        type = "web_service"
        name = "philosophy-worker"
        ownerId = $ownerId
        repo = $RepoUrl
        branch = "main"
        autoDeploy = "no"
        serviceDetails = @{
            runtime = "docker"
            envSpecificDetails = @{
                dockerfilePath = "./Dockerfile.worker"
                dockerContext = "./"
            }
            healthCheckPath = "/healthz"
            plan = "free"
            region = "oregon"
            numInstances = 1
        }
        envVars = @(
            @{ key = "PORT"; value = "8080" },
            @{ key = "ASPNETCORE_ENVIRONMENT"; value = "Production" },
            @{ key = "Database__Provider"; value = "Postgres" },
            @{ key = "ConnectionStrings__Default"; value = $pgConn },
            @{ key = "Mock__FfmpegPath"; value = "ffmpeg" },
            @{ key = "Mock__PublicBaseUrl"; value = $apiUrl }
        )
    }
}
$workerId = $worker.service.id
Write-Host "Worker created: $workerId"

Write-Host "`n=== Deployment started ==="
Write-Host "API:    $apiUrl"
Write-Host "Web:    $webUrl"
Write-Host "Worker: $workerId"
Write-Host "`nDeploys will be triggered; first builds can take several minutes." -ForegroundColor Cyan
Write-Host "API deploy: $apiDeploy"

# Save summary for later polling
@{ apiId = $apiId; webId = $webId; workerId = $workerId; apiUrl = $apiUrl; webUrl = $webUrl; ownerId = $ownerId } | ConvertTo-Json | Set-Content (Join-Path $PSScriptRoot "render-ids.json")