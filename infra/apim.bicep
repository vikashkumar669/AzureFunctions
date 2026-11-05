// Harbourline Partner API in API Management (Chapter 14).
// Deploy after main.bicep. The OpenAPI file is the contract; the
// Function App is only the backend.

param location string = resourceGroup().location
param apimName string
param publisherEmail string
param functionAppHostName string
param tenantId string = subscription().tenantId
@description('Application ID URI of the Partner API app registration.')
param partnerApiAudience string
@description('Application ID URI that protects the Function App (Easy Auth).')
param functionAppAudience string

resource apim 'Microsoft.ApiManagement/service@2022-08-01' = {
  name: apimName
  location: location
  sku: { name: 'Consumption', capacity: 0 }
  identity: { type: 'SystemAssigned' }
  properties: {
    publisherEmail: publisherEmail
    publisherName: 'Harbourline Group (fictional)'
  }
}

resource namedValues 'Microsoft.ApiManagement/service/namedValues@2022-08-01' = [
  for nv in [
    { name: 'tenant-id', value: tenantId }
    { name: 'partner-api-audience', value: partnerApiAudience }
    { name: 'function-app-audience', value: functionAppAudience }
  ]: {
    parent: apim
    name: nv.name
    properties: { displayName: nv.name, value: nv.value }
  }
]

resource backend 'Microsoft.ApiManagement/service/backends@2022-08-01' = {
  parent: apim
  name: 'harbourline-functions'
  properties: {
    protocol: 'http'
    url: 'https://${functionAppHostName}/api'
  }
}

// <listing:bicep-apim-api>
resource api 'Microsoft.ApiManagement/service/apis@2022-08-01' = {
  parent: apim
  name: 'partner-api'
  properties: {
    displayName: 'Harbourline Partner API'
    path: 'partner'
    protocols: ['https']
    subscriptionRequired: true             // key per partner + token
    format: 'openapi'
    value: loadTextContent('apim/partner-api.openapi.yaml') // <1>
  }
}

resource policy 'Microsoft.ApiManagement/service/apis/policies@2022-08-01' = {
  parent: api
  name: 'policy'
  properties: {
    format: 'rawxml'
    value: loadTextContent('apim/partner-api-policy.xml')  // <2>
  }
  dependsOn: [namedValues, backend]
}
// </listing:bicep-apim-api>
