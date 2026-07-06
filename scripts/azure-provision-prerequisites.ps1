<#
.SYNOPSIS
  One-time, per-environment Azure setup that the GitHub Actions deploy identity
  cannot perform itself -resource group creation, its own Contributor grant,
  and resource-provider registration (all subscription/tenant-scoped actions;
  the deploy identity only has Contributor on the resource group).

.DESCRIPTION
  Safe to re-run -every step checks current state first and skips if already
  done. Must be run by a human with Owner (or Contributor + User Access
  Administrator) rights on the target subscription. Run this BEFORE the first
  infra-bkdmnl.yml deploy to a new environment, and again any time a new
  resource provider namespace is added to the Bicep template.

.PARAMETER Environment
  Which bkdmnl environment to provision prerequisites for.

.PARAMETER SubscriptionId
  Azure subscription ID. Defaults to the BakedManila subscription.

.PARAMETER DeployAppClientId
  Client ID of the GitHub Actions OIDC deploy identity (Entra app).

.EXAMPLE
  ./scripts/azure-provision-prerequisites.ps1 -Environment prod -DeployAppClientId <client-id>
#>

param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('dev', 'prod', 'dr')]
    [string]$Environment,

    [string]$SubscriptionId = '4d6302be-05c4-4bb7-b94f-2805445f5d1c',

    [Parameter(Mandatory = $true)]
    [string]$DeployAppClientId
)

$ErrorActionPreference = 'Stop'
$resourceGroup = "rg-bkdmnl-$Environment-sea"

Write-Host "== BakedManila prerequisites: $Environment ($resourceGroup) ==" -ForegroundColor Cyan

# Safety: refuse to run against the wrong subscription (az can be logged into
# an unrelated tenant/subscription without any obvious warning).
az account set --subscription $SubscriptionId
$activeSubId = az account show --query id -o tsv
if ($activeSubId -ne $SubscriptionId) {
    throw "Active subscription is '$activeSubId', expected '$SubscriptionId'. Aborting."
}
Write-Host "Confirmed subscription: $SubscriptionId" -ForegroundColor Green

# Step 1: resource group (a resource-group-scoped deployment cannot create its own RG)
$rgExists = az group exists --name $resourceGroup
if ($rgExists -eq 'false') {
    Write-Host "Creating resource group $resourceGroup..."
    az group create --name $resourceGroup --location southeastasia | Out-Null
} else {
    Write-Host "Resource group $resourceGroup already exists -skipping."
}

# Step 2: Contributor grant for the deploy identity on this resource group only (least privilege)
$rgId = az group show --name $resourceGroup --query id -o tsv
$existingAssignment = az role assignment list --assignee $DeployAppClientId --scope $rgId --role Contributor --query "[0].id" -o tsv
if ([string]::IsNullOrWhiteSpace($existingAssignment)) {
    Write-Host "Granting deploy identity Contributor on $resourceGroup..."
    az role assignment create --assignee $DeployAppClientId --role Contributor --scope $rgId | Out-Null
} else {
    Write-Host "Deploy identity already has Contributor on $resourceGroup -skipping."
}

# Step 3: register every resource provider namespace this template needs. Registration
# is subscription-scoped, so the RG-scoped deploy identity cannot do this itself -a
# subscription that has never used one of these services fails the first deploy with
# MissingSubscriptionRegistration.
$providers = @('Microsoft.Web', 'Microsoft.Sql', 'Microsoft.Storage', 'Microsoft.KeyVault', 'Microsoft.Insights', 'Microsoft.OperationalInsights', 'Microsoft.Consumption', 'Microsoft.Logic')
foreach ($ns in $providers) {
    $state = az provider show --namespace $ns --query registrationState -o tsv
    if ($state -ne 'Registered') {
        Write-Host "Registering resource provider $ns (current state: $state)..."
        az provider register --namespace $ns | Out-Null
        do {
            Start-Sleep -Seconds 10
            $state = az provider show --namespace $ns --query registrationState -o tsv
            Write-Host "  ...$ns state: $state"
        } while ($state -eq 'Registering')
    } else {
        Write-Host "Resource provider $ns already registered -skipping."
    }
}

Write-Host ""
Write-Host "Prerequisites are in place for $Environment. Run the infra-bkdmnl.yml GitHub Actions workflow now." -ForegroundColor Cyan
Write-Host "If that deploy sets budgetAmount > 0, run scripts/azure-grant-cost-auto-stop-rbac.ps1 afterwards."
