@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

@description('Resource ID of the linked Log Analytics workspace')
param logAnalyticsWorkspaceId string

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-${appName}-${env}-sea'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    IngestionMode: 'LogAnalytics'
    WorkspaceResourceId: logAnalyticsWorkspaceId
  }
}

// Not a secret — instrumentation is scoped to this resource and the connection
// string alone cannot read data back out.
output connectionString string = appInsights.properties.ConnectionString
