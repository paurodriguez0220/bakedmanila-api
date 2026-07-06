@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Email address to notify on budget alerts')
param alertEmail string

@description('Name of the Logic App to invoke to stop the app on budget breach')
param logicAppName string

// Referenced as `existing` so the trigger's callback URL (a SAS-signed secret) is
// computed and consumed entirely within this module — never surfaced as a module
// output, which the Bicep linter flags as a secret-leak risk in deployment history.
resource logicApp 'Microsoft.Logic/workflows@2019-05-01' existing = {
  name: logicAppName
}

var logicAppTriggerResourceId = resourceId('Microsoft.Logic/workflows/triggers', logicApp.name, 'manual')

resource actionGroup 'Microsoft.Insights/actionGroups@2023-01-01' = {
  name: 'ag-${appName}-cost-${env}'
  location: 'global'
  properties: {
    groupShortName: take('ag${appName}cost', 12)
    enabled: true
    emailReceivers: [
      {
        name: 'CostAlertEmail'
        emailAddress: alertEmail
        useCommonAlertSchema: false
      }
    ]
    logicAppReceivers: [
      {
        name: 'AutoStopApp'
        resourceId: logicAppTriggerResourceId
        callbackUrl: listCallbackUrl(logicAppTriggerResourceId, '2019-05-01').value
        useCommonAlertSchema: false
      }
    ]
  }
}

output id string = actionGroup.id
