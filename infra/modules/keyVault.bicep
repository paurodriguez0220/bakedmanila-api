@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

@description('Tenant ID for the vault')
param tenantId string = subscription().tenantId

@description('FQDN of the SQL server, used to compose the ADO connection string secret')
param sqlServerFqdn string

@description('SQL admin login, used to compose the ADO connection string secret')
param sqlAdminLogin string

@description('SQL database name, used to compose the ADO connection string secret')
param sqlDatabaseName string

@description('SQL admin password. Never exposed via outputs — only used to compose the secret value below.')
@secure()
param sqlAdminPassword string

@description('Name of the storage account, used to compose the blob connection string secret via listKeys()')
param storageAccountName string

@secure()
param jwtSigningKey string = ''

@secure()
param adminPassword string = ''

@secure()
param acsEmailConnectionString string = ''

// Composed here, never output — downstream consumers get a Key Vault secret URI instead.
var sqlConnectionString = 'Server=tcp:${sqlServerFqdn},1433;Initial Catalog=${sqlDatabaseName};Persist Security Info=False;User ID=${sqlAdminLogin};Password=${sqlAdminPassword};MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;'

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

var blobConnectionString = 'DefaultEndpointsProtocol=https;AccountName=${storageAccountName};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}'

resource keyVault 'Microsoft.KeyVault/vaults@2023-02-01' = {
  name: 'kv-${appName}-${env}-sea'
  location: location
  properties: {
    sku: {
      family: 'A'
      name: 'standard'
    }
    tenantId: tenantId
    enableRbacAuthorization: true
  }
}

// Written whenever the SQL admin password is supplied — which is every real deployment,
// since sqlServer.bicep requires it unconditionally. The guard keeps a passwordless
// what-if/dry run from writing a secret with an incomplete connection string.
resource sqlConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (!empty(sqlAdminPassword)) {
  parent: keyVault
  name: 'SqlConnectionString'
  properties: {
    value: sqlConnectionString
  }
}

resource jwtSigningKeySecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (!empty(jwtSigningKey)) {
  parent: keyVault
  name: 'JwtSigningKey'
  properties: {
    value: jwtSigningKey
  }
}

resource adminPasswordSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (!empty(adminPassword)) {
  parent: keyVault
  name: 'AdminPassword'
  properties: {
    value: adminPassword
  }
}

// Legitimately optional — the app falls back to LoggingNotificationSender when unset.
resource acsEmailConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = if (!empty(acsEmailConnectionString)) {
  parent: keyVault
  name: 'AcsEmailConnectionString'
  properties: {
    value: acsEmailConnectionString
  }
}

// Derived from the storage account, which always exists — always written.
resource blobConnectionStringSecret 'Microsoft.KeyVault/vaults/secrets@2023-02-01' = {
  parent: keyVault
  name: 'BlobConnectionString'
  properties: {
    value: blobConnectionString
  }
}

output name string = keyVault.name
output vaultUri string = keyVault.properties.vaultUri

// Secret URIs are not secret; the vault RBAC grant controls read access. Composed as
// plain strings (rather than referencing the conditional secret resources above) so
// webApp can wire Key Vault references regardless of which secrets were actually
// written in this deployment.
output sqlConnectionStringSecretUri string = '${keyVault.properties.vaultUri}secrets/SqlConnectionString'
output jwtSigningKeySecretUri string = '${keyVault.properties.vaultUri}secrets/JwtSigningKey'
// Suppressed: linter name-matches "*Password*" and flags it as a secret, but this is a
// Key Vault secret URI (a locator, not a value) — the vault's RBAC grant is what protects it.
#disable-next-line outputs-should-not-contain-secrets
output adminPasswordSecretUri string = '${keyVault.properties.vaultUri}secrets/AdminPassword'
output acsEmailConnectionStringSecretUri string = '${keyVault.properties.vaultUri}secrets/AcsEmailConnectionString'
output blobConnectionStringSecretUri string = '${keyVault.properties.vaultUri}secrets/BlobConnectionString'
