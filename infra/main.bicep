// Harbourline Connect – one environment (dev, test or prod). Chapter 20.
// Deploy: az deployment group create -g <rg> -f infra/main.bicep \
//           -p environmentName=dev dataverseUrl=https://<org>.crm.dynamics.com

targetScope = 'resourceGroup'

@allowed(['dev', 'test', 'prod'])
param environmentName string
param location string = resourceGroup().location
param dataverseUrl string
@description('Object id: az role definition list --name "Durable Task Data Contributor" --query [0].name')
param durableTaskRoleId string = ''
@minValue(1)
@maxValue(1000)
param maximumInstanceCount int = 40

// An app setting value that the platform resolves from Key Vault.
func keyVaultRef(vaultName string, secretName string) string =>
  '@Microsoft.KeyVault(VaultName=${vaultName};SecretName=${secretName})'

var suffix = uniqueString(resourceGroup().id, environmentName)
var tags = {
  application: 'harbourline-connect'
  environment: environmentName
  owner: 'integration-team'
}

// <listing:bicep-storage>
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'sthlconnect${suffix}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: environmentName == 'prod' ? 'Standard_ZRS' : 'Standard_LRS' }
  properties: {
    allowSharedKeyAccess: false          // identity only, no keys
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

var containerNames = [
  'deployments'
  'property-feeds'
  'feed-reports'
  'feed-quarantine'
  'meter-readings'
  'lease-packs'
]

// </listing:bicep-storage>

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = [
  for name in containerNames: {
    parent: blobService
    name: name
  }
]

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: 'log-hlconnect-${environmentName}-${suffix}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource insights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-hlconnect-${environmentName}-${suffix}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logs.id
    DisableLocalAuth: true               // telemetry needs Entra auth
  }
}

// <listing:bicep-servicebus>
resource bus 'Microsoft.ServiceBus/namespaces@2021-11-01' = {
  name: 'sb-hlconnect-${environmentName}-${suffix}'
  location: location
  tags: tags
  sku: { name: 'Standard' }              // topics need Standard
}

resource orderQueue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  parent: bus
  name: 'order-intake'
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT1M'
    requiresDuplicateDetection: true     // MessageId = order number
    duplicateDetectionHistoryTimeWindow: 'PT1H'
    deadLetteringOnMessageExpiration: true
  }
}

resource signingQueue 'Microsoft.ServiceBus/namespaces/queues@2021-11-01' = {
  parent: bus
  name: 'lease-signing'
  properties: { maxDeliveryCount: 5 }
}

resource accountTopic 'Microsoft.ServiceBus/namespaces/topics@2021-11-01' = {
  parent: bus
  name: 'account-events'

  resource ledgerSubscription 'subscriptions' = {
    name: 'ledgerone'
    properties: {
      maxDeliveryCount: 10
      deadLetteringOnMessageExpiration: true
    }
  }

  // Dataverse's service endpoint authenticates with a SAS key. Give
  // it a rule that can only SEND, and only to this topic.
  resource dataverseSendRule 'authorizationRules' = {
    name: 'dataverse-send'
    properties: { rights: ['Send'] }
  }
}
// </listing:bicep-servicebus>

resource vault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-hl-${environmentName}-${take(suffix, 8)}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true        // roles, not access policies
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

resource scheduler 'Microsoft.DurableTask/schedulers@2025-04-01-preview' = {
  name: 'dts-hlconnect-${environmentName}-${suffix}'
  location: location
  tags: tags
  properties: {
    ipAllowlist: ['0.0.0.0/0']           // restrict in production
    sku: { name: 'Consumption' }        // Dedicated for steady high load
  }
}

resource taskHub 'Microsoft.DurableTask/schedulers/taskHubs@2025-04-01-preview' = {
  parent: scheduler
  name: 'default'
  properties: {}
}

// <listing:bicep-plan>
resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'plan-hlconnect-${environmentName}-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp'
  sku: { name: 'FC1', tier: 'FlexConsumption' }
  properties: { reserved: true }         // Linux
}
// </listing:bicep-plan>

// <listing:bicep-site>
resource site 'Microsoft.Web/sites@2024-11-01' = {
  name: 'func-hlconnect-${environmentName}-${suffix}'
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: { type: 'SystemAssigned' }   // <1>
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}deployments'
          authentication: { type: 'SystemAssignedIdentity' } // <2>
        }
      }
      scaleAndConcurrency: {
        maximumInstanceCount: maximumInstanceCount           // <3>
        instanceMemoryMB: 2048
      }
      runtime: { name: 'dotnet-isolated', version: '10.0' }
      siteUpdateStrategy: { type: 'RollingUpdate' }          // <4>
    }
    siteConfig: {
      minTlsVersion: '1.2'
      appSettings: [
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: insights.properties.ConnectionString
        }
        {
          name: 'APPLICATIONINSIGHTS_AUTHENTICATION_STRING'
          value: 'Authorization=AAD'
        }
        {
          name: 'Dataverse__Url'
          value: dataverseUrl
        }
        {
          name: 'ServiceBus__fullyQualifiedNamespace'
          value: '${bus.name}.servicebus.windows.net'
        }
        {
          name: 'FeedStorage__blobServiceUri'
          value: storage.properties.primaryEndpoints.blob
        }
        {
          name: 'FeedStorage__queueServiceUri'
          value: storage.properties.primaryEndpoints.queue
        }
        {
          name: 'Exports__BlobServiceUri'
          value: storage.properties.primaryEndpoints.blob
        }
        {
          name: 'DURABLE_TASK_SCHEDULER_CONNECTION_STRING'
          value: join([
            'Endpoint=${scheduler.properties.endpoint}'
            'TaskHub=${taskHub.name}'
            'Authentication=ManagedIdentity'
          ], ';')
        }
        {
          name: 'InvoiceSyncSchedule'
          value: '0 0 2 * * *'
        }
        {
          name: 'LedgerOne__ApiKey'
          // A Key Vault reference: resolved by the platform. // <5>
          value: keyVaultRef(vault.name, 'ledgerone-api-key')
        }
        {
          name: 'SignBridge__CallbackSecret'
          value: keyVaultRef(vault.name, 'signbridge-callback-secret')
        }
      ]
    }
  }
}
// </listing:bicep-site>

// <listing:bicep-roles>
var roles = {
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  storageQueueDataContributor: '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
  serviceBusDataReceiver: '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
  serviceBusDataSender: '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  monitoringMetricsPublisher: '3913510d-42f4-4e42-8a64-420c390055eb'
}

resource storageRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [roles.storageBlobDataOwner, roles.storageQueueDataContributor]: {
    scope: storage
    name: guid(storage.id, site.id, role)
    properties: {
      principalId: site.identity.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId(
        'Microsoft.Authorization/roleDefinitions', role)
    }
  }
]

resource busRoles 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for role in [roles.serviceBusDataReceiver, roles.serviceBusDataSender]: {
    scope: bus
    name: guid(bus.id, site.id, role)
    properties: {
      principalId: site.identity.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId(
        'Microsoft.Authorization/roleDefinitions', role)
    }
  }
]
// </listing:bicep-roles>

resource vaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: vault
  name: guid(vault.id, site.id, roles.keyVaultSecretsUser)
  properties: {
    principalId: site.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
  }
}

resource insightsRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: insights
  name: guid(insights.id, site.id, roles.monitoringMetricsPublisher)
  properties: {
    principalId: site.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions', roles.monitoringMetricsPublisher)
  }
}

resource taskHubRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(durableTaskRoleId)) {
  scope: taskHub
  name: guid(taskHub.id, site.id, durableTaskRoleId)
  properties: {
    principalId: site.identity.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId(
      'Microsoft.Authorization/roleDefinitions', durableTaskRoleId)
  }
}

output functionAppName string = site.name
output principalId string = site.identity.principalId
output serviceBusNamespace string = bus.name
output keyVaultName string = vault.name
