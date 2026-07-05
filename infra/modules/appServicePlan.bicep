@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

@description('App Service Plan SKU')
param sku string

resource plan 'Microsoft.Web/serverfarms@2022-09-01' = {
  name: 'asp-${appName}-${env}-sea'
  location: location
  kind: 'linux'
  sku: {
    name: sku
  }
  properties: {
    reserved: true
  }
}

output id string = plan.id
