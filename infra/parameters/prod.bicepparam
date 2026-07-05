using '../main.bicep'

param appName = 'bkdmnl'
param env = 'prod'
param sku = 'B1'
param adminEmail = 'admin@bakedmanila.com'
param emailFrom = ''
param emailTo = ''

// Empty default is load-bearing: an env var left unset means the corresponding
// Key Vault secret write is skipped rather than writing a blank value.
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD', '')
param jwtSigningKey = readEnvironmentVariable('JWT_SIGNING_KEY', '')
param adminPassword = readEnvironmentVariable('ADMIN_PASSWORD', '')
param acsEmailConnectionString = readEnvironmentVariable('ACS_EMAIL_CONNECTION_STRING', '')
