@description('Application code — the universal key across resource names, resource groups, and workflows')
param appName string = 'bkdmnl'

@description('Environment name (dev, prod)')
param env string

@description('App Service Plan SKU')
param sku string = 'B1'

@description('Seed admin email address')
param adminEmail string = 'admin@bakedmanila.com'

@description('ACS email "from" address')
param emailFrom string = ''

@description('ACS email "to" address (order notification recipient)')
param emailTo string = ''

@description('SQL admin login password. Empty default skips nothing here — required for the SQL server module.')
@secure()
param sqlAdminPassword string = ''

@description('JWT signing key. Empty default skips the Key Vault secret write.')
@secure()
param jwtSigningKey string = ''

@description('Seed admin password. Empty default skips the Key Vault secret write.')
@secure()
param adminPassword string = ''

@description('ACS email connection string. Empty default skips the Key Vault secret write — the app falls back to logging notifications.')
@secure()
param acsEmailConnectionString string = ''

// Region is hardcoded per app, not parameterized — see standards-docs/azure-infra.md.
// Resource-group-scoped deployment (default scope) — the resource group itself is
// created by the workflow before this deployment runs (see infra-bkdmnl.yml), so the
// OIDC service principal only needs Contributor on the bkdmnl resource groups, not the
// subscription.
var location = 'southeastasia'

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  params: {
    appName: appName
    env: env
    location: location
  }
}

module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsights'
  params: {
    appName: appName
    env: env
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

module appServicePlan 'modules/appServicePlan.bicep' = {
  name: 'appServicePlan'
  params: {
    appName: appName
    env: env
    location: location
    sku: sku
  }
}

module sqlServer 'modules/sqlServer.bicep' = {
  name: 'sqlServer'
  params: {
    appName: appName
    env: env
    location: location
    sqlAdminPassword: sqlAdminPassword
  }
}

module storageAccount 'modules/storageAccount.bicep' = {
  name: 'storageAccount'
  params: {
    appName: appName
    env: env
    location: location
  }
}

// SQL and storage land before Key Vault — the vault composes their connection strings
// into secrets. Web App consumes the vault's secret URIs; the Key Vault access policy
// module runs last, once the web app's managed identity exists.
//
// Note: the RBAC modules (keyVault.rbac.bicep, storageAccount.rbac.bicep) that used to
// live here have been removed. The deploy service principal only has Contributor on the
// resource group and this tenant's admin cannot grant it roleAssignments/write, so the
// template can no longer contain any Microsoft.Authorization/roleAssignments resources.
// Key Vault access is now granted via an access policy (see modules/keyVaultAccessPolicy.bicep),
// which is a property write on the vault and Contributor-compatible. Blob Storage access
// already goes through the KV-stored connection string (ConnectionStrings__BlobStorage in
// webApp.bicep), not the managed identity, so dropping the Storage Blob Data Contributor
// grant removes a forward-looking, never-consumed permission with no functional impact.
// Revisit RBAC for both if tenant permissions ever allow granting roleAssignments/write.
module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  params: {
    appName: appName
    env: env
    location: location
    sqlServerFqdn: sqlServer.outputs.fullyQualifiedDomainName
    sqlAdminLogin: sqlServer.outputs.adminLogin
    sqlDatabaseName: sqlServer.outputs.databaseName
    sqlAdminPassword: sqlAdminPassword
    storageAccountName: storageAccount.outputs.name
    jwtSigningKey: jwtSigningKey
    adminPassword: adminPassword
    acsEmailConnectionString: acsEmailConnectionString
  }
}

module webApp 'modules/webApp.bicep' = {
  name: 'webApp'
  params: {
    appName: appName
    env: env
    location: location
    appServicePlanId: appServicePlan.outputs.id
    sqlConnectionStringSecretUri: keyVault.outputs.sqlConnectionStringSecretUri
    jwtSigningKeySecretUri: keyVault.outputs.jwtSigningKeySecretUri
    adminPasswordSecretUri: keyVault.outputs.adminPasswordSecretUri
    acsEmailConnectionStringSecretUri: keyVault.outputs.acsEmailConnectionStringSecretUri
    blobConnectionStringSecretUri: keyVault.outputs.blobConnectionStringSecretUri
    hasAcsEmailConnectionString: !empty(acsEmailConnectionString)
    adminEmail: adminEmail
    emailFrom: emailFrom
    emailTo: emailTo
    appInsightsConnectionString: appInsights.outputs.connectionString
    storageBlobEndpoint: storageAccount.outputs.blobEndpoint
  }
}

module keyVaultAccessPolicy 'modules/keyVaultAccessPolicy.bicep' = {
  name: 'keyVaultAccessPolicy'
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: webApp.outputs.principalId
  }
}

output webAppName string = webApp.outputs.name
output defaultHostName string = webApp.outputs.defaultHostName
