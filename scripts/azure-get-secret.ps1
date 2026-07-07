<#
.SYNOPSIS
  Fetches a secret's value from the BakedManila Key Vault for a given environment.

.DESCRIPTION
  Prints the secret value to the console -treat the output as sensitive; don't
  paste it into logs, tickets, or chat.

  The vault uses access policies, not Azure RBAC (enableRbacAuthorization: false
  in keyVault.bicep), so being subscription/RG Owner does not by itself grant
  secret-read access -only identities explicitly listed in the vault's access
  policy can read secrets. If the signed-in user isn't listed yet, this script
  grants itself get/list (a vault-resource property write, which Owner/Contributor
  can perform -distinct from the Microsoft.Authorization/roleAssignments actions
  this tenant blocks; see README.md's Key Vault deviation). Safe to re-run -skips
  the grant if already present.

.PARAMETER SecretName
  Name of the Key Vault secret, e.g. AdminPassword, JwtSigningKey, SqlAdminPassword.

.PARAMETER Environment
  Which bkdmnl environment's vault to read from.

.PARAMETER SubscriptionId
  Azure subscription ID. Defaults to the BakedManila subscription.

.EXAMPLE
  ./scripts/azure-get-secret.ps1 -SecretName AdminPassword -Environment prod
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$SecretName,

    [ValidateSet('dev', 'prod', 'dr')]
    [string]$Environment = 'prod',

    [string]$SubscriptionId = '4d6302be-05c4-4bb7-b94f-2805445f5d1c'
)

$ErrorActionPreference = 'Stop'
$vaultName = "kv-bkdmnl-$Environment-sea"

az account set --subscription $SubscriptionId
$activeSubId = az account show --query id -o tsv
if ($activeSubId -ne $SubscriptionId) {
    throw "Active subscription is '$activeSubId', expected '$SubscriptionId'. Aborting."
}

$userId = az ad signed-in-user show --query id -o tsv
$existingPolicy = az keyvault show --name $vaultName --query "properties.accessPolicies[?objectId=='$userId'] | [0].objectId" -o tsv
if ([string]::IsNullOrWhiteSpace($existingPolicy)) {
    Write-Host "Granting yourself secrets get/list on $vaultName..."
    az keyvault set-policy --name $vaultName --object-id $userId --secret-permissions get list | Out-Null
} else {
    Write-Host "Already have secrets get/list on $vaultName -skipping grant."
}

$value = az keyvault secret show --vault-name $vaultName --name $SecretName --query value -o tsv
if ([string]::IsNullOrWhiteSpace($value)) {
    throw "Secret '$SecretName' not found (or empty) in vault '$vaultName'."
}

Write-Host "$SecretName ($vaultName):" -ForegroundColor Cyan
Write-Host $value
