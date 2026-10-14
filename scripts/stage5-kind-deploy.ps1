# TREBA Stage 5 - local kind deployment from previously loaded ECR images.
# Safe by default: without -Deploy, performs checks and prints the deployment plan.
# Run in Windows PowerShell 5.1 on the PC hosting Docker Desktop + kind.
# This file does not push to ECR or deploy to production EC2/RDS.

param(
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

$Context = "kind-treba-stage5"
$Namespace = "treba-stage5"
$KindNode = "treba-stage5-control-plane"
$AWSProfile = "treba-stage5"
$AWSRegion = "eu-west-1"
$AWSAccount = "019066823315"
$Registry = "$AWSAccount.dkr.ecr.$AWSRegion.amazonaws.com"
$BaseUrl = "http://localhost:8080"

$BackendDigest = "sha256:875c53782763138cef874db93fe09a64839d9cde9b41f78f6746d3761c2a9ec7"
$FrontendDigest = "sha256:77b03f6a906996fb5282dc837a70e04fb39eab4e86447766cb79b92d560a8b25"

# The frontend was imported into kind using this local registry-style tag.
# The ECR digest is independently checked below.
$BackendImage = "$Registry/treba-backend@$BackendDigest"
$FrontendImage = "$Registry/treba-frontend:stage5-ecr-77b03f6a"

function Get-DeploymentImage {
    param([string]$Deployment)

    $image = & kubectl --context $Context -n $Namespace get deployment $Deployment -o 'jsonpath={.spec.template.spec.containers[0].image}'
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($image)) {
        throw "Could not read image for deployment/$Deployment"
    }
    return [string]$image
}

function Wait-ForDeployment {
    param([string]$Deployment)

    & kubectl --context $Context -n $Namespace rollout status "deployment/$Deployment" --timeout=240s
    if ($LASTEXITCODE -ne 0) {
        throw "Rollout did not complete: $Deployment"
    }
}

function Set-DeploymentImage {
    param(
        [string]$Deployment,
        [string]$Container,
        [string]$Image
    )

    & kubectl --context $Context -n $Namespace set image "deployment/$Deployment" "${Container}=$Image"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not set image on deployment/$Deployment"
    }
    Wait-ForDeployment -Deployment $Deployment
}

function Test-HttpRoutes {
    param(
        [int]$Attempts = 15,
        [int]$PauseSeconds = 3
    )

    foreach ($path in @("/", "/ping", "/health", "/api/v1/categories")) {
        $healthy = $false

        for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
            $url = "$BaseUrl$path"
            $httpCode = [string](& curl.exe --silent --output NUL --write-out '%{http_code}' --connect-timeout 4 --max-time 10 $url)
            $curlExit = $LASTEXITCODE

            if ($curlExit -eq 0 -and $httpCode.Trim() -eq "200") {
                Write-Host "[OK] $path HTTP 200"
                $healthy = $true
                break
            }

            Write-Host "[WAIT] $path attempt $attempt/$Attempts - HTTP $httpCode, curl exit $curlExit"
            if ($attempt -lt $Attempts) {
                Start-Sleep -Seconds $PauseSeconds
            }
        }

        if (-not $healthy) {
            throw "HTTP check failed after $Attempts attempts: $path"
        }
    }

    # An HTTP 200 alone is not enough: the backend also needs a working database.
    $healthBody = [string](& curl.exe --fail --silent --show-error --max-time 10 "$BaseUrl/health")
    if ($LASTEXITCODE -ne 0) {
        throw "Cannot read backend /health JSON"
    }
    try {
        $health = $healthBody | ConvertFrom-Json
    }
    catch {
        throw "Backend /health returned invalid JSON"
    }
    if ($health.status -ne "ok" -or $health.database -ne "ok") {
        throw "Backend or database health check is not OK"
    }
    Write-Host "[OK] Backend and database health JSON"
}

Write-Host "=== TREBA Stage 5 local Kubernetes CD ==="
Write-Host "Mode: $(if ($Deploy) { 'DEPLOY' } else { 'DRY RUN' })"

# Strict target and identity checks. Do not accidentally act on production.
$currentContext = [string](& kubectl config current-context)
if ($LASTEXITCODE -ne 0 -or $currentContext.Trim() -ne $Context) {
    throw "Wrong current Kubernetes context: $currentContext"
}

$account = [string](& aws sts get-caller-identity --profile $AWSProfile --region $AWSRegion --query Account --output text)
if ($LASTEXITCODE -ne 0 -or $account.Trim() -ne $AWSAccount) {
    throw "AWS authentication failed or account mismatch"
}

$migrations = [string](& kubectl --context $Context -n $Namespace get configmap backend-config -o 'jsonpath={.data.ApplyMigrations}')
if ($LASTEXITCODE -ne 0 -or $migrations.Trim() -ne "false") {
    throw "ApplyMigrations must be false in ConfigMap backend-config"
}
Write-Host "[OK] Context, AWS account and migrations"

$remoteBackend = [string](& aws ecr describe-images --profile $AWSProfile --region $AWSRegion --repository-name treba-backend --image-ids imageTag=c9ba6ddda251376eacf2baf80cd42d14c912ecbe --query 'imageDetails[0].imageDigest' --output text)
if ($LASTEXITCODE -ne 0 -or $remoteBackend.Trim() -ne $BackendDigest) {
    throw "Backend ECR digest mismatch"
}

$remoteFrontend = [string](& aws ecr describe-images --profile $AWSProfile --region $AWSRegion --repository-name treba-frontend --image-ids imageTag=stage5-d57e2d0eabb9 --query 'imageDetails[0].imageDigest' --output text)
if ($LASTEXITCODE -ne 0 -or $remoteFrontend.Trim() -ne $FrontendDigest) {
    throw "Frontend ECR digest mismatch"
}
Write-Host "[OK] Both ECR digests confirmed"

# Images were previously loaded into the local kind node manually.
$nodeImages = @(& docker exec $KindNode ctr -n k8s.io images ls -q)
if ($LASTEXITCODE -ne 0) {
    throw "Cannot inspect local kind image store"
}
if ($nodeImages -notcontains $BackendImage) {
    throw "Backend ECR image is missing from kind: $BackendImage"
}
if ($nodeImages -notcontains $FrontendImage) {
    throw "Frontend Stage 5 image is missing from kind: $FrontendImage"
}
Write-Host "[OK] Both images are present in local kind"

$oldBackend = Get-DeploymentImage -Deployment "treba-backend"
$oldFrontend = Get-DeploymentImage -Deployment "treba-frontend"
Write-Host "Current Backend:  $oldBackend"
Write-Host "Target Backend:   $BackendImage"
Write-Host "Current Frontend: $oldFrontend"
Write-Host "Target Frontend:  $FrontendImage"

# Check the existing app *before* replacing any pod. A failing baseline is not
# evidence that a new deployment is bad, so we do not deploy onto a broken app.
Write-Host "=== Existing website health check ==="
Test-HttpRoutes -Attempts 5 -PauseSeconds 2

if (-not $Deploy) {
    Write-Host "=== DRY RUN PASSED - deployments unchanged ==="
    exit 0
}

$backendChanged = $false
$frontendChanged = $false

try {
    Write-Host "=== Deploying Backend ==="
    if ($oldBackend -ne $BackendImage) {
        $backendChanged = $true
        Set-DeploymentImage -Deployment "treba-backend" -Container "backend" -Image $BackendImage
    }
    else {
        Wait-ForDeployment -Deployment "treba-backend"
    }

    Write-Host "=== Deploying Frontend ==="
    if ($oldFrontend -ne $FrontendImage) {
        $frontendChanged = $true
        Set-DeploymentImage -Deployment "treba-frontend" -Container "frontend" -Image $FrontendImage
    }
    else {
        Wait-ForDeployment -Deployment "treba-frontend"
    }

    # Traefik can temporarily return 502 while the service endpoints change.
    # Retry before declaring failure; a persistent failure triggers rollback.
    Write-Host "=== Post-deployment HTTP checks (with retries) ==="
    Test-HttpRoutes -Attempts 15 -PauseSeconds 3

    Write-Host "=== STAGE 5 LOCAL CD: SUCCESS ==="
}
catch {
    $originalError = $_.Exception.Message
    Write-Warning "Deployment failed: $originalError"
    Write-Warning "Restoring the images used before this run..."
    $rollbackOk = $true

    if ($frontendChanged) {
        try {
            Set-DeploymentImage -Deployment "treba-frontend" -Container "frontend" -Image $oldFrontend
            Write-Host "[OK] Frontend restored"
        }
        catch {
            $rollbackOk = $false
            Write-Warning "Frontend rollback failed: $($_.Exception.Message)"
        }
    }

    if ($backendChanged) {
        try {
            Set-DeploymentImage -Deployment "treba-backend" -Container "backend" -Image $oldBackend
            Write-Host "[OK] Backend restored"
        }
        catch {
            $rollbackOk = $false
            Write-Warning "Backend rollback failed: $($_.Exception.Message)"
        }
    }

    try {
        Test-HttpRoutes -Attempts 10 -PauseSeconds 3
        Write-Host "[OK] Restored website responds correctly"
    }
    catch {
        $rollbackOk = $false
        Write-Warning "Website did not recover after rollback: $($_.Exception.Message)"
    }

    & kubectl --context $Context -n $Namespace get deployments,pods

    if ($rollbackOk) {
        throw "CD failed ($originalError). Previous deployment restored and checked."
    }
    throw "CD failed ($originalError). Rollback or post-rollback checks also failed; inspect the cluster."
}
