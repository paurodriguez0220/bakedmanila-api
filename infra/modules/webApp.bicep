@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

@description('Resource ID of the App Service Plan to host this app on')
param appServicePlanId string

@description('Key Vault secret URI for the SQL connection string')
param sqlConnectionStringSecretUri string

@description('Key Vault secret URI for the JWT signing key')
param jwtSigningKeySecretUri string

@description('Key Vault secret URI for the admin password')
param adminPasswordSecretUri string

@description('Key Vault secret URI for the ACS email connection string')
param acsEmailConnectionStringSecretUri string

@description('Key Vault secret URI for the blob storage connection string')
param blobConnectionStringSecretUri string

@description('Whether an ACS email connection string secret was provisioned')
param hasAcsEmailConnectionString bool

@description('Seed admin email address')
param adminEmail string

@description('ACS email "from" address')
param emailFrom string

@description('ACS email "to" address (order notification recipient)')
param emailTo string

@description('Application Insights connection string')
param appInsightsConnectionString string

var storagePublicBaseUrl = 'https://st${toLower(appName)}${toLower(env)}sea.blob.${environment().suffixes.storage}/product-images'

var baseAppSettings = [
  {
    name: 'ConnectionStrings__BakedManila'
    value: '@Microsoft.KeyVault(SecretUri=${sqlConnectionStringSecretUri})'
  }
  {
    name: 'Jwt__SigningKey'
    value: '@Microsoft.KeyVault(SecretUri=${jwtSigningKeySecretUri})'
  }
  {
    name: 'Jwt__Issuer'
    value: 'BakedManila'
  }
  {
    name: 'Jwt__Audience'
    value: 'BakedManila'
  }
  {
    name: 'Admin__Email'
    value: adminEmail
  }
  {
    name: 'Admin__Password'
    value: '@Microsoft.KeyVault(SecretUri=${adminPasswordSecretUri})'
  }
  {
    name: 'Email__From'
    value: emailFrom
  }
  {
    name: 'Email__To'
    value: emailTo
  }
  {
    name: 'Images__Provider'
    value: 'AzureBlob'
  }
  {
    name: 'ConnectionStrings__BlobStorage'
    value: '@Microsoft.KeyVault(SecretUri=${blobConnectionStringSecretUri})'
  }
  {
    name: 'Storage__PublicBaseUrl'
    value: storagePublicBaseUrl
  }
  {
    name: 'Migrations__ApplyAtStartup'
    value: 'true'
  }
  {
    name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
    value: appInsightsConnectionString
  }
]

// Email:ConnectionString is legitimately optional (LoggingNotificationSender fallback) —
// only wire the Key Vault reference when the secret was actually provisioned.
var emailAppSetting = [
  {
    name: 'Email__ConnectionString'
    value: '@Microsoft.KeyVault(SecretUri=${acsEmailConnectionStringSecretUri})'
  }
]

var appSettings = hasAcsEmailConnectionString ? concat(baseAppSettings, emailAppSetting) : baseAppSettings

resource webApp 'Microsoft.Web/sites@2022-09-01' = {
  name: 'app-${appName}-${env}-sea'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      minTlsVersion: '1.2'
      alwaysOn: true
      appSettings: appSettings
    }
  }
}

output name string = webApp.name
output defaultHostName string = webApp.properties.defaultHostName
output principalId string = webApp.identity.principalId
