@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: 'log-${appName}-${env}-sea'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

output id string = workspace.id
