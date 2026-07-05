targetScope = 'subscription'

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
var location = 'southeastasia'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: 'rg-${appName}-${env}-sea'
  location: location
}

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  scope: rg
  params: {
    appName: appName
    env: env
    location: location
  }
}

module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsights'
  scope: rg
  params: {
    appName: appName
    env: env
    location: location
    logAnalyticsWorkspaceId: logAnalytics.outputs.id
  }
}

module appServicePlan 'modules/appServicePlan.bicep' = {
  name: 'appServicePlan'
  scope: rg
  params: {
    appName: appName
    env: env
    location: location
    sku: sku
  }
}

module sqlServer 'modules/sqlServer.bicep' = {
  name: 'sqlServer'
  scope: rg
  params: {
    appName: appName
    env: env
    location: location
    sqlAdminPassword: sqlAdminPassword
  }
}

module storageAccount 'modules/storageAccount.bicep' = {
  name: 'storageAccount'
  scope: rg
  params: {
    appName: appName
    env: env
    location: location
  }
}

// SQL and storage land before Key Vault — the vault composes their connection strings
// into secrets. Web App consumes the vault's secret URIs; RBAC modules run last, once
// the web app's managed identity exists.
module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  scope: rg
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
  scope: rg
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
  }
}

module storageAccountRbac 'modules/storageAccount.rbac.bicep' = {
  name: 'storageAccountRbac'
  scope: rg
  params: {
    storageAccountName: storageAccount.outputs.name
    principalId: webApp.outputs.principalId
  }
}

module keyVaultRbac 'modules/keyVault.rbac.bicep' = {
  name: 'keyVaultRbac'
  scope: rg
  params: {
    keyVaultName: keyVault.outputs.name
    principalId: webApp.outputs.principalId
  }
}

output webAppName string = webApp.outputs.name
output defaultHostName string = webApp.outputs.defaultHostName
