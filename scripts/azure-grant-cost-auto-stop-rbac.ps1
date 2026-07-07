<#
.SYNOPSIS
  Grants the cost-auto-stop Logic App's managed identity permission to stop the
  BakedManila web app. Must run once after a deploy with budgetAmount > 0 has
  created the Logic App -the GitHub Actions deploy identity cannot create this
  role assignment itself (this tenant blocks roleAssignments/write for it; see
  README.md "cost auto-stop needs one manual role grant").

.DESCRIPTION
  Safe to re-run -skips if the role assignment already exists. Looks up the
  Logic App and web app by their deterministic Bicep-assigned names rather than
  deployment history, since CI-triggered deployments don't use the same
  deployment name as a manual `az deployment group create` run.

.PARAMETER Environment
  Which bkdmnl environment to grant the role in.

.PARAMETER SubscriptionId
  Azure subscription ID. Defaults to the BakedManila subscription.

.EXAMPLE
  ./scripts/azure-grant-cost-auto-stop-rbac.ps1 -Environment prod
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'prod', 'dr')]
    [string]$Environment,

    [string]$SubscriptionId = '4d6302be-05c4-4bb7-b94f-2805445f5d1c'
)

$ErrorActionPreference = 'Stop'
$resourceGroup = "rg-bkdmnl-$Environment-sea"
$logicAppName = "logic-bkdmnl-$Environment-sea"
$webAppName = "app-bkdmnl-$Environment-sea"

az account set --subscription $SubscriptionId
$activeSubId = az account show --query id -o tsv
if ($activeSubId -ne $SubscriptionId) {
    throw "Active subscription is '$activeSubId', expected '$SubscriptionId'. Aborting."
}

$principalId = az resource show --resource-group $resourceGroup --resource-type Microsoft.Logic/workflows --name $logicAppName --query identity.principalId -o tsv 2>$null
if ([string]::IsNullOrWhiteSpace($principalId)) {
    throw "Logic App '$logicAppName' not found in $resourceGroup. Has a deploy with budgetAmount > 0 run for $Environment yet?"
}

$webAppId = az webapp show --resource-group $resourceGroup --name $webAppName --query id -o tsv
if ([string]::IsNullOrWhiteSpace($webAppId)) {
    throw "Web app '$webAppName' not found in $resourceGroup."
}

$existing = az role assignment list --assignee $principalId --scope $webAppId --role "Website Contributor" --query "[0].id" -o tsv
if ([string]::IsNullOrWhiteSpace($existing)) {
    Write-Host "Granting Website Contributor on $webAppName to Logic App $logicAppName..."
    az role assignment create --assignee $principalId --role "Website Contributor" --scope $webAppId | Out-Null
    Write-Host "Done -cost auto-stop can now actually stop the app when the budget hits 100%." -ForegroundColor Green
} else {
    Write-Host "Role assignment already exists -skipping."
}
