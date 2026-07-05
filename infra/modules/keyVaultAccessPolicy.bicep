@description('Name of the Key Vault to grant access on')
param keyVaultName string

@description('Principal ID of the identity to grant access to')
param principalId string

@description('Tenant ID the access policy entry is scoped to')
param tenantId string = tenant().tenantId

// Access-policy grant, not a role assignment — the deploy service principal only has
// Contributor on the resource group and cannot create Microsoft.Authorization/roleAssignments
// in this tenant. Writing an access policy is a property write on the vault resource itself,
// which Contributor permits. Grants only the secret read operations the web app needs.
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource accessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        tenantId: tenantId
        objectId: principalId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
        }
      }
    ]
  }
}
