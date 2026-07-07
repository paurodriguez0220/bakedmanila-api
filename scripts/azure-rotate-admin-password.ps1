<#
.SYNOPSIS
  Rotates the BakedManila admin login password in one step: generates a new
  alphanumeric-only password (no symbols - safe to paste anywhere, including
  bash, PowerShell, and URLs, with no history-expansion or escaping surprises),
  writes it to Key Vault, and restarts the web app so the seeder picks it up.

.DESCRIPTION
  DevSeeder.SeedAdminAsync compares the configured Admin:Password (sourced from
  this Key Vault secret via an App Service Key Vault reference) against the
  stored Identity user's password on every startup, and resets it on mismatch.
  Key Vault references are resolved once at app startup, so updating the secret
  alone does not change the running app's config - it must restart to notice.
  This script does all three steps (generate, write secret, restart) in one go.

  Safe to re-run - grants itself vault access if missing (Owner does not imply
  Key Vault secret access; see azure-get-secret.ps1 for why).

.PARAMETER Environment
  Which bkdmnl environment to rotate the admin password for.

.PARAMETER SubscriptionId
  Azure subscription ID. Defaults to the BakedManila subscription.

.PARAMETER Password
  Optional - supply your own password instead of generating one. Must satisfy
  the app's Identity policy: 10+ characters, at least one uppercase, one
  lowercase, one digit. Avoid punctuation if you want it safe to paste into a
  shell without escaping.

.EXAMPLE
  ./scripts/azure-rotate-admin-password.ps1 -Environment prod
#>

param(
    [ValidateSet('dev', 'prod', 'dr')]
    [string]$Environment = 'prod',

    [string]$SubscriptionId = '4d6302be-05c4-4bb7-b94f-2805445f5d1c',

    [string]$Password
)

$ErrorActionPreference = 'Stop'
$vaultName = "kv-bkdmnl-$Environment-sea"
$resourceGroup = "rg-bkdmnl-$Environment-sea"
$webAppName = "app-bkdmnl-$Environment-sea"

az account set --subscription $SubscriptionId
$activeSubId = az account show --query id -o tsv
if ($activeSubId -ne $SubscriptionId) {
    throw "Active subscription is '$activeSubId', expected '$SubscriptionId'. Aborting."
}

$userId = az ad signed-in-user show --query id -o tsv
$existingPolicy = az keyvault show --name $vaultName --query "properties.accessPolicies[?objectId=='$userId'] | [0].objectId" -o tsv
if ([string]::IsNullOrWhiteSpace($existingPolicy)) {
    Write-Host "Granting yourself secrets get/list/set on $vaultName..."
    az keyvault set-policy --name $vaultName --object-id $userId --secret-permissions get list set | Out-Null
} else {
    Write-Host "Already have vault access on $vaultName - skipping grant."
}

if ([string]::IsNullOrWhiteSpace($Password)) {
    $words = @('Cookie', 'Banana', 'Choco', 'Vanilla', 'Butter', 'Sugar', 'Crumb', 'Toffee', 'Caramel', 'Oatmeal')
    $word1 = Get-Random -InputObject $words
    $word2 = Get-Random -InputObject ($words | Where-Object { $_ -ne $word1 })
    $digits = Get-Random -Minimum 10 -Maximum 99
    $Password = "$word1$word2$digits"
}

Write-Host "Setting AdminPassword in $vaultName..."
az keyvault secret set --vault-name $vaultName --name AdminPassword --value $Password | Out-Null

Write-Host "Restarting $webAppName so the seeder picks up the new password..."
az webapp restart --resource-group $resourceGroup --name $webAppName | Out-Null

Write-Host ""
Write-Host "Done. New admin password:" -ForegroundColor Cyan
Write-Host $Password
Write-Host ""
Write-Host "Give the app a minute to finish restarting and re-seed before logging in."
