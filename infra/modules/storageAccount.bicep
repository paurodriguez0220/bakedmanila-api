@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

// Storage account names: no hyphens, lowercase, <=24 chars.
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: 'st${toLower(appName)}${toLower(env)}sea'
  location: location
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: true
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
}

// Anonymous read access — product images are public storefront assets.
resource productImagesContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: 'product-images'
  properties: {
    publicAccess: 'Blob'
  }
}

output name string = storageAccount.name
output id string = storageAccount.id
