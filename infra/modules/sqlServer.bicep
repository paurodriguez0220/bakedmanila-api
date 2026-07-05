@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

@description('SQL admin login name')
param adminLogin string = 'bkdmnladmin'

@description('SQL admin login password. Deviation from managed-identity auth — see README.')
@secure()
param sqlAdminPassword string

var databaseName = 'BakedManila'

resource sqlServer 'Microsoft.Sql/servers@2021-11-01' = {
  name: 'sql-${appName}-${env}-sea'
  location: location
  properties: {
    administratorLogin: adminLogin
    administratorLoginPassword: sqlAdminPassword
    version: '12.0'
    minimalTlsVersion: '1.2'
  }
}

// Serverless General Purpose Gen5, 1 vCore max, auto-pause after 60 minutes idle,
// scales down to 0.5 vCore, capped at 2 GB — cheapest tier for a pre-order storefront.
resource database 'Microsoft.Sql/servers/databases@2021-11-01' = {
  parent: sqlServer
  name: databaseName
  location: location
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 1
  }
  properties: {
    autoPauseDelay: 60
    minCapacity: json('0.5')
    maxSizeBytes: 2147483648
  }
}

resource allowAzureServices 'Microsoft.Sql/servers/firewallRules@2021-11-01' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output fullyQualifiedDomainName string = sqlServer.properties.fullyQualifiedDomainName
output adminLogin string = adminLogin
output databaseName string = databaseName
