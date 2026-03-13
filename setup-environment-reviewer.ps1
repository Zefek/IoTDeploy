# One-time setup script: adds IoTDeploy GitHub App as required reviewer for an environment
#
# Prerequisites:
#   - GitHub Personal Access Token with 'repo' scope
#   - The environment must already exist in the repository
#
# Usage:
#   .\setup-environment-reviewer.ps1 -Repository "Garage" -Environment "Home" -Pat "ghp_..."

param(
    [Parameter(Mandatory)] [string] $Repository,
    [Parameter(Mandatory)] [string] $Environment,
    [Parameter(Mandatory)] [string] $Pat
)

$Owner      = "Zefek"
$AppId      = 2427873
$ApiBase    = "https://api.github.com"

$headers = @{
    "Authorization"        = "Bearer $Pat"
    "Accept"               = "application/vnd.github+json"
    "X-GitHub-Api-Version" = "2022-11-28"
}

# 1. Verify the environment exists
Write-Host "Checking environment '$Environment' in $Owner/$Repository..."
try {
    $envUrl = "$ApiBase/repos/$Owner/$Repository/environments/$Environment"
    $envInfo = Invoke-RestMethod -Uri $envUrl -Headers $headers -Method Get
    Write-Host "  Found: $($envInfo.name) (id: $($envInfo.id))"
} catch {
    Write-Error "Environment '$Environment' not found in $Owner/$Repository. $_"
    exit 1
}

# 2. Fetch current environment config to preserve existing settings
$currentReviewers = @()
if ($envInfo.protection_rules) {
    $reviewerRule = $envInfo.protection_rules | Where-Object { $_.type -eq "required_reviewers" }
    if ($reviewerRule) {
        $currentReviewers = $reviewerRule.reviewers | ForEach-Object {
            @{ type = $_.type; id = $_.reviewer.id }
        }
    }
}

# 3. Add IoTDeploy App if not already present
$alreadyAdded = $currentReviewers | Where-Object { $_.type -eq "App" -and $_.id -eq $AppId }
if ($alreadyAdded) {
    Write-Host "IoTDeploy App (id: $AppId) is already a required reviewer. Nothing to do."
    exit 0
}

$newReviewers = $currentReviewers + @(@{ type = "App"; id = $AppId })

# 4. Update the environment
Write-Host "Adding IoTDeploy App (id: $AppId) as required reviewer..."
$body = @{
    reviewers = $newReviewers
} | ConvertTo-Json -Depth 5

try {
    $result = Invoke-RestMethod -Uri $envUrl -Headers $headers -Method Put -Body $body -ContentType "application/json"
    Write-Host "Done. Environment '$($result.name)' updated successfully."
} catch {
    Write-Error "Failed to update environment. $_"
    exit 1
}
