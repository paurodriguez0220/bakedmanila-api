@description('Application code')
param appName string

@description('Environment name (dev, prod)')
param env string

@description('Monthly budget amount in USD. 80% triggers an email alert; 100% triggers the auto-stop action group.')
param budgetAmount int

@description('Email address for the 80% early-warning alert')
param alertEmail string

@description('Resource ID of the Action Group to invoke at 100% (triggers auto-stop)')
param actionGroupId string

@description('Start of the current billing month — computed at deploy time, not re-evaluated on redeploy unless this changes')
param budgetStartDate string = utcNow('yyyy-MM-01T00:00:00Z')

resource budget 'Microsoft.Consumption/budgets@2021-10-01' = {
  name: 'budget-${appName}-${env}'
  properties: {
    category: 'Cost'
    amount: budgetAmount
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: budgetStartDate
      endDate: dateTimeAdd(budgetStartDate, 'P10Y')
    }
    notifications: {
      Actual_GreaterThan_80_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 80
        thresholdType: 'Actual'
        contactEmails: [
          alertEmail
        ]
        contactGroups: []
        contactRoles: []
      }
      Actual_GreaterThan_100_Percent: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Actual'
        contactEmails: [
          alertEmail
        ]
        contactGroups: [
          actionGroupId
        ]
        contactRoles: []
      }
    }
  }
}

output name string = budget.name
