@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Azure region')
param location string

@description('Name of the App Service (web app) this Logic App stops when triggered')
param webAppName string

// HTTP-triggered Consumption Logic App: an Action Group invokes the trigger URL,
// and the single action stops the web app via the ARM control-plane API using the
// Logic App's own managed identity — no payload parsing, this workflow only ever
// does one thing.
resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'logic-${appName}-${env}-sea'
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    state: 'Enabled'
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      triggers: {
        manual: {
          type: 'Request'
          kind: 'Http'
          inputs: {
            schema: {}
          }
        }
      }
      actions: {
        Stop_web_app: {
          type: 'Http'
          inputs: {
            method: 'POST'
            uri: '${environment().resourceManager}/subscriptions/${subscription().subscriptionId}/resourceGroups/${resourceGroup().name}/providers/Microsoft.Web/sites/${webAppName}/stop?api-version=2022-09-01'
            authentication: {
              type: 'ManagedServiceIdentity'
            }
          }
          runAfter: {}
        }
      }
      outputs: {}
    }
    parameters: {}
  }
}

output name string = logicApp.name
output principalId string = logicApp.identity.principalId
