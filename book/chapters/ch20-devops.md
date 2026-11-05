---
chapter: 20
part: "Part IV — Advanced Enterprise Implementations"
title: "Deployment and DevOps"
build-summary: "Harbourline Connect's infrastructure is code, its pipeline signs in to Azure without a secret, and every release moves the same tested package from dev to test to prod with approvals, smoke tests and zero-downtime rolling updates."
builds:
  - "Bicep templates for the Function App, storage, Service Bus, Key Vault, Durable Task Scheduler, Application Insights and roles"
  - "A GitHub Actions workflow with OpenID Connect and environment approvals"
  - "An Azure DevOps pipeline alternative, and Dataverse solution deployment"
versions: "Bicep · GitHub Actions (azure/login v3, Azure/functions-action v1) · Azure Pipelines (AzureFunctionApp@2) · Flex Consumption rolling updates"
---

::: objectives
- Describe a Flex Consumption app and its dependencies in Bicep, with identity-based access throughout.
- Authenticate a pipeline to Azure with OpenID Connect instead of a stored secret.
- Promote one build through dev, test and prod with approvals and smoke tests.
- Deploy with rolling updates, and roll back without deployment slots.
- Ship Dataverse schema changes as solutions alongside the Azure release.
:::

## The business problem

Harbourline's old integration was deployed by copying files to a server on Friday evenings. Each environment had drifted: settings typed by hand, a firewall rule added in a hurry, a storage key nobody could trace. When test passed and production failed, nobody could say what was different.

The goal: environments created from the same code, releases that need no one's password, and a deployment that users don't notice.

## How it works

### Infrastructure as code with Bicep

**Bicep** is Azure's declarative language for resources. A template states what should exist; Azure Resource Manager makes it so, creating or updating resources. Running the same template with `environmentName=test` and `environmentName=prod` produces environments that differ only where parameters say they should.

### Flex Consumption specifics

A Flex Consumption app is a `Microsoft.Web/sites` resource on a `FC1` plan, with its behaviour in a `functionAppConfig` section: the **deployment storage** container that holds the code package, the runtime and version, instance memory, maximum instance count, and the **site update strategy**. There are no deployment slots on Flex. Instead, the `RollingUpdate` strategy drains instances in batches while new instances start with the new version; in-progress executions finish, within the usual limits. Microsoft's documentation lists caveats worth knowing: the platform controls batch sizes and timing, there is no progress indicator or built-in rollback, a one-instance app still sees a brief gap, and changes must work alongside the previous version while the update rolls.

### Pipelines without secrets

**OpenID Connect (OIDC)** lets GitHub Actions or Azure Pipelines get an Azure token by presenting a short-lived token issued by the CI system. In Entra ID you add a **federated credential** to an app registration (or user-assigned managed identity) that trusts, for example, `repo:<owner>/<repo>:environment:prod`. Nothing secret is stored in GitHub: the repository only knows the client, tenant and subscription IDs.

![One build, three environments: the same package and templates move from dev to prod, with an approval and a smoke test at each step.](figures/ch20/pipeline.svg){#fig:pipeline}

## Architecture

::: {#tbl:components caption="Deployment components"}
| Component | Role | Authenticates with | Failure path |
|---|---|---|---|
| `infra/main.bicep` | Every Azure resource and role | Pipeline identity (Contributor + User Access Administrator on the resource group) | Deployment fails, nothing half-changed in a resource |
| GitHub workflow | Build once, deploy three times | OIDC federated credential per environment | Job fails; later environments don't run |
| GitHub environments | Approvals and variables | Required reviewers | Release waits |
| Dataverse solution | Schema and app changes | Power Platform build tools | Import fails; Azure release not started |
:::

## Prerequisites

- Chapters 5, 11, 12, 14 and 18: the resources the template creates are explained there.
- Owner rights on the target resource groups (once, to grant the pipeline identity its roles).

## Step-by-step implementation

### Step 1 – Storage that accepts identities only

```{.bicep #lst:storage include="infra/main.bicep" region="bicep-storage" caption="A storage account with shared keys disabled"}
```

`allowSharedKeyAccess: false` means no connection string with a key works against this account. The host, the deployment container and the feed triggers all use the app's identity; Bicep later grants the roles they need.

### Step 2 – The plan and the app

```{.bicep #lst:plan include="infra/main.bicep" region="bicep-plan" caption="A Flex Consumption plan"}
```

```{.bicep #lst:site include="infra/main.bicep" region="bicep-site" caption="The Function App: identity, deployment container, scale, runtime and settings"}
```

1. A system-assigned identity, created with the app.
2. Code packages are stored in the `deployments` container, read with that identity.
3. The maximum instance count is a parameter: the number you multiplied by concurrency in Chapter 2.
4. Rolling updates for zero-downtime deployments.
5. Secrets appear only as Key Vault references, built by a small Bicep function. The secret values are set once, by a person, in Key Vault; they are never in the template, the pipeline or the repository.

The app settings use identity-based forms: `AzureWebJobsStorage__accountName`, `ServiceBus__fullyQualifiedNamespace`, `FeedStorage__blobServiceUri`. Not one connection string with a key.

### Step 3 – Grant the identity its roles

```{.bicep #lst:roles include="infra/main.bicep" region="bicep-roles" caption="Role assignments for the app's identity"}
```

Role assignment names are GUIDs derived from the scope, the app and the role, so redeploying doesn't create duplicates. The Durable Task Scheduler's role ID is a parameter: look it up once with `az role definition list --name "Durable Task Data Contributor"`, rather than trusting a GUID copied from a blog. The template also creates the Service Bus entities (Chapter 11), Key Vault with RBAC, Application Insights with local authentication disabled, and the scheduler and task hub.

### Step 4 – Set up OIDC

For each environment:

1. Create an app registration (or user-assigned identity) `hlconnect-deploy-<env>`.
2. Add a federated credential: issuer `https://token.actions.githubusercontent.com`, subject `repo:<owner>/<repo>:environment:<env>`.
3. Grant it *Contributor* and *User Access Administrator* (or *Role Based Access Control Administrator*, constrained to the roles in the template) on `rg-hlconnect-<env>`.
4. In GitHub, create environments `dev`, `test` and `prod`; add required reviewers to `test` and `prod`; set variables `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `RESOURCE_GROUP` and `DATAVERSE_URL` per environment.

### Step 5 – The workflow

```{.yaml #lst:workflow include=".github/workflows/azure-functions-deploy.yml" caption="Build once, deploy to dev, test and prod"}
```

The build job tests and publishes once; the deploy job runs per environment, in order (`max-parallel: 1`), each waiting for its environment's approval. `azure/login` exchanges the GitHub token for an Azure token; `az deployment group create` applies the Bicep; `Azure/functions-action` uploads the package to the deployment container and starts the rolling update; the smoke test calls `/api/health` until it answers or gives up. The workflow starts manually until the environments are configured; Chapter 25's checklist turns on the push trigger.

### Step 6 – The Azure DevOps alternative

```{.yaml #lst:ado include="pipelines/azure-pipelines.yml" caption="The same flow in Azure Pipelines"}
```

Use a service connection with **workload identity federation**, the Azure DevOps equivalent of OIDC, and an **environment** with approvals and checks. For Flex Consumption, `AzureFunctionApp@2` needs `appType: functionAppLinux` and `isFlexConsumption: true`; version 1 of the task doesn't support Flex.

### Step 7 – Ship Dataverse changes as solutions

The schema from Chapter 4 reaches test and production as a **managed solution**, never by running the setup tool. Export the `HarbourlineConnect` solution from development with the Power Platform CLI (`pac solution export`), keep the unpacked source in the repository (`pac solution unpack`), and import it in the pipeline with the Power Platform Build Tools before the Azure deployment. Environment-specific values, such as the webhook URL, go in **environment variables** and **connection references** in the solution, set per environment at import.

Order matters: schema first, then code that uses it. Make changes backward compatible (add columns before code uses them, stop using them before removing them) so that rolling updates and staggered imports never meet an incompatible pair.

### Step 8 – Roll back

There are no slots to swap. To roll back, redeploy the previous build's artifact through the same pipeline, which starts another rolling update. Keep build artifacts for at least as long as you might need them, and tag releases in Git so "the previous version" is unambiguous. For data changes, roll *forward*: a fix is safer than reversing a migration.

## Deep dive: a release you can trust

A pipeline isn't valuable because it's automated; it's valuable because every release goes through the same checks, leaves the same evidence, and can be repeated or reversed. This section looks at the properties that make Harbourline's release trustworthy, and at the gaps it deliberately leaves to people.

### Build once, deploy many

The workflow builds and tests **once**, uploads the published output as an artifact, and deploys that **same** artifact to dev, test and prod. Rebuilding for each environment would mean that the code in production was never tested in test. The same rule applies to configuration: environment-specific values come from GitHub **environment variables** (`vars.RESOURCE_GROUP`, `vars.DATAVERSE_URL`) and Bicep parameters, never from different builds.

### What-if before you change infrastructure

`az deployment group create` applies the Bicep template. Before applying to production, it's worth seeing what will change. Azure Resource Manager offers a **what-if** operation that compares the template with the current resources and lists creates, modifications and deletions without making them:

```bash
az deployment group what-if -g <rg> --template-file infra/main.bicep \
  --parameters environmentName=prod dataverseUrl=<url>
```

Harbourline runs what-if in the pull request for any change under `infra/`, so reviewers see "this change modifies the Service Bus subscription's max delivery count" next to the code diff.

### The gates

::: {#tbl:gates caption="Checks between a commit and production"}
| Gate | Where | Catches |
|---|---|---|
| Build with warnings as errors | CI workflow | Compiler and analyzer findings |
| Unit tests | CI and deploy workflow | Logic and error-mapping regressions |
| Bicep build | CI workflow | Template syntax and type errors |
| Pull request review | GitHub branch protection | Design problems, missing tests, secrets in code |
| Deploy to dev, smoke checks | Deploy workflow | Wiring: settings, identities, roles |
| Required reviewer for prod | GitHub environment protection | Releases nobody agreed to |
:::

The required reviewer isn't bureaucracy; it's the moment when someone checks that dev and test look healthy and that the release notes match the business's expectations. Make the reviewer's job easy: link the workbook and the smoke-check results in the run summary.

### Secrets: none to steal

The pipeline holds no Azure credentials. GitHub issues a short-lived OIDC token for each job; Entra ID trusts it only for the exact repository and environment named in the federated credential's **subject** (`repo:vikashkumar669/AzureFunctions:environment:prod`). A fork, another branch's workflow without the environment, or a different repository gets nothing. Scope the identity's Azure roles to the resource group it deploys, not the subscription.

### Rolling updates and rollback

Flex Consumption doesn't have deployment slots. Its **rolling update** strategy (where available) replaces instances in batches so in-flight executions can complete, which removes most of the disruption of a deployment. Rollback is "deploy the previous artifact": keep artifacts for at least as long as you might need to go back, and practise it. Two things can't be rolled back by redeploying code:

- **Dataverse schema changes** shipped in solutions. Make them additive (new columns, not renamed ones) so old code keeps working, as Chapter 7's expand-and-contract pattern describes.
- **Durable Functions orchestrations** in flight. Follow Chapter 12's versioning rules so a rollback doesn't break running instances.

### What the pipeline doesn't do

It doesn't decide whether a release is a good idea, migrate data, or tell users what changed. Those remain people's jobs, and the runbooks in Chapter 25 cover them.

## Screens you will use

::: {.capture #fig:fedcred caption="A federated credential trusting one GitHub environment." app="Microsoft Entra admin center" path="App registrations › hlconnect-deploy-prod › Certificates & secrets › Federated credentials" annotations="1 Issuer; 2 Subject: repo:…:environment:prod; 3 No client secrets"}
:::

::: {.capture #fig:approval caption="A production deployment waiting for approval." app="GitHub" path="Actions › azure-functions-deploy › run › deploy (prod)" annotations="1 Waiting for review; 2 Required reviewers; 3 Approve and deploy"}
:::

::: {.capture #fig:siteupdate caption="Flex Consumption site update strategy set to rolling updates." app="Azure portal" path="Function App › Settings › Configuration › Site update strategy" annotations="1 RollingUpdate"}
:::

## Complete working code

- `infra/main.bicep`, `infra/apim.bicep` and `infra/apim/*`
- `.github/workflows/azure-functions-book.yml` (CI) and `azure-functions-deploy.yml` (CD)
- `pipelines/azure-pipelines.yml`

## Configuration

::: {#tbl:settings caption="Pipeline variables per GitHub environment"}
| Variable | Example | Secret? |
|---|---|---|
| `AZURE_CLIENT_ID` | Deploy identity's client ID | No |
| `AZURE_TENANT_ID` | Tenant ID | No |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID | No |
| `RESOURCE_GROUP` | `rg-hlconnect-test` | No |
| `DATAVERSE_URL` | `https://harbourline-test.crm11.dynamics.com` | No |
:::

## Sample input and output

```text
deploy (test)  Waiting for approval from @integration-leads
  ✓ Sign in to Azure (OIDC)
  ✓ Infrastructure      func-hlconnect-test-k3q7…
  ✓ Deploy code
  ✓ Smoke test          /api/health → 200
```

(Illustrative output.)

## Testing

CI compiles every Bicep file on every push, catching syntax and type errors. Before a template change reaches production, run `az deployment group what-if` against test to see exactly which resources would change, and read it as carefully as a code diff.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see when deploying"}
| Symptom | Likely cause | Fix |
|---|---|---|
| `AADSTS70021`: no matching federated identity record | Subject doesn't match repo, branch or environment | Match the subject exactly |
| Role assignment fails | Pipeline lacks rights to assign roles | Grant *User Access Administrator* or *RBAC Administrator* on the group |
| App starts but can't read storage | Role assignment still propagating | Retry; propagation can take minutes |
| Deployment succeeds, functions missing | Package not in `released-package.zip` form or wrong folder | Deploy with the functions action or `func` |
| Brief errors during a release | One instance only, or incompatible change | Expected with one instance; keep changes backward compatible |
| Key Vault reference unresolved after deploy | Secret not created yet | Create secrets before first deployment |
:::

## Security considerations

::: security
The pipeline's identity can change your production environment: scope it to one resource group, require approvals for test and prod, and protect the workflow files with code owners. Never store Azure credentials in GitHub or Azure DevOps when OIDC or workload identity federation is available.
:::

## Performance and scalability

::: performance
Deploy outside the busiest hours even with rolling updates: draining instances finish work while new ones start, so capacity dips briefly. Keep start-up fast (no network calls in `Program.cs`) so new instances become ready quickly.
:::

## Alternatives and design decisions

Terraform describes the same resources and suits organisations that standardise on it; Bicep was chosen because it is Azure-native and needs no state file. Azure Developer CLI (`azd`) templates can create and deploy Flex apps quickly and are a good start for prototypes. Container deployment to Azure Container Apps is the choice when a team already runs containers (Chapter 2).

## Best practices

- Describe every resource and role in Bicep; never configure production by hand.
- Prefer identity-based settings; disable storage shared keys.
- Authenticate pipelines with OIDC or workload identity federation.
- Build once, promote the same artifact, with approvals and smoke tests.
- Use rolling updates and backward-compatible changes; roll back by redeploying.
- Ship Dataverse changes as managed solutions, schema before code.

## Guided lab: set up a secret-free deployment from scratch

**Requires:** your fork of the companion repository on GitHub, rights to create an app registration and role assignments in a development subscription, and the Azure CLI.

### Lab 20.1 – Create the deployment identity

```bash
RG=rg-hlconnect-lab
az group create -n $RG -l uksouth
APP_ID=$(az ad app create --display-name hlconnect-deploy-lab --query appId -o tsv)
az ad sp create --id $APP_ID
az role assignment create --assignee $APP_ID --role Contributor \
  --scope $(az group show -n $RG --query id -o tsv)
```

The template also creates role assignments for the app's managed identity, which needs *User Access Administrator* or *Role Based Access Control Administrator* on the resource group as well; add that assignment the same way (and in production, constrain it with a condition so it can only assign the roles the template uses).

**Checkpoint:** `az role assignment list --assignee $APP_ID -o table` shows the assignments, scoped to the resource group only.

### Lab 20.2 – Trust your repository's `dev` environment

```bash
cat > fed.json <<EOF
{ "name": "github-dev",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:<your-account>/AzureFunctions:environment:dev",
  "audiences": ["api://AzureADTokenExchange"] }
EOF
az ad app federated-credential create --id $APP_ID --parameters fed.json
```

**Checkpoint:** the app registration's **Certificates & secrets › Federated credentials** tab lists `github-dev`, and the **Client secrets** tab is empty.

### Lab 20.3 – Configure the GitHub environment

In your fork, open **Settings › Environments**, create `dev`, and add these environment variables: `AZURE_CLIENT_ID` (`$APP_ID`), `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `RESOURCE_GROUP` (`rg-hlconnect-lab`) and `DATAVERSE_URL`. For this lab, edit the workflow's matrix to `[dev]` only, on a branch.

**Checkpoint:** no secrets are defined, only variables. Nothing in the repository or its settings can be used to sign in to Azure from outside the workflow.

### Lab 20.4 – Preview the infrastructure

```bash
az deployment group what-if -g $RG --template-file infra/main.bicep \
  --parameters environmentName=dev dataverseUrl=https://<your-dev-org>.crm11.dynamics.com
```

**Checkpoint:** a list of resources to be **created**: storage, Service Bus namespace with its queues and topic, Key Vault, Durable Task Scheduler, Application Insights, the plan and the Function App, plus role assignments. Nothing is created yet.

### Lab 20.5 – Run the workflow

In **Actions › azure-functions-deploy**, choose **Run workflow** on your branch.

**Checkpoint:** the *build* job runs the tests and uploads the artifact; the *deploy (dev)* job signs in without a secret, applies the template and deploys the code. Open the Function App in the portal: the functions from Chapter 3's list appear. Run the health check:

```bash
curl -s https://<app-name>.azurewebsites.net/api/health
```

### Lab 20.6 – Prove the trust is narrow

Temporarily change the workflow's `environment:` line to a name that has no federated credential (for example `scratch`, after creating that environment with the same variables) and run it again.

**Checkpoint:** the *Sign in to Azure* step fails with an error saying no matching federated identity record was found. The identity trusts exactly one repository and environment. Revert the change.

### Lab 20.7 – Roll back

Make a visible change (for example, the health service name), deploy it, then re-run the **previous** successful workflow run (**Re-run all jobs** on that run, if its artifact hasn't expired).

**Checkpoint:** the health endpoint shows the old name again. Rollback is just another deployment of a known artifact.

### Lab 20.8 – Clean up

```bash
az group delete -n $RG --yes --no-wait
az ad app delete --id $APP_ID
```

Purge the Key Vault afterwards if your subscription requires it (`az keyvault purge`), because soft-deleted vaults keep their names reserved.

## Hands-on exercise

::: exercise
**Goal:** add a staging smoke check that touches Dataverse.

**Steps:** add a `GET /api/health/ready` function that reads one `hl_syncstate` row and returns 200 or 503, exclude it from Easy Auth, and call it from the smoke test after `/api/health`.

**Done when:** a deployment to an environment without the application user fails at the smoke test, not at the first real request.

**Stretch:** add an `az deployment group what-if` step that runs before the infrastructure step in test and prod, and publish its output to the job summary.
:::

## Knowledge check

::: knowledge-check
1. What does `allowSharedKeyAccess: false` force, and why is it safe for this app?
2. How do you roll back a Flex Consumption app without slots?
3. What does a federated credential trust, and what is stored in GitHub?
4. Why does the workflow build once and deploy three times?
5. In which order are Dataverse solutions and Azure code deployed, and why?
:::

Answers are in Appendix G.

::: summary
- Bicep describes the whole environment, identities and roles included; parameters make dev, test and prod.
- Flex Consumption uses a deployment container and rolling updates instead of slots.
- OIDC and workload identity federation remove deployment secrets.
- One artifact moves through approvals and smoke tests; rollback is a redeployment.
- Dataverse changes travel as managed solutions, schema before code.
:::

## References

- [Automate function app resource deployment (Bicep/ARM)](https://learn.microsoft.com/azure/azure-functions/functions-infrastructure-as-code) – Microsoft Learn, checked 2026-10-27.
- [Rolling updates in Flex Consumption](https://learn.microsoft.com/azure/azure-functions/flex-consumption-site-updates) – Microsoft Learn, checked 2026-10-27.
- [Continuous delivery with GitHub Actions](https://learn.microsoft.com/azure/azure-functions/functions-how-to-github-actions) – Microsoft Learn, checked 2026-10-27.
- [Continuous delivery with Azure Pipelines](https://learn.microsoft.com/azure/azure-functions/functions-how-to-azure-devops) – Microsoft Learn, checked 2026-10-27.
- [Recover from a bad Flex Consumption deployment](https://learn.microsoft.com/azure/azure-functions/functions-rollback-deployments) – Microsoft Learn, checked 2026-10-27.
- [Use GitHub Actions to connect to Azure with OpenID Connect](https://learn.microsoft.com/azure/developer/github/connect-from-azure-openid-connect) – Microsoft Learn, checked 2026-10-27.
- [Power Platform Build Tools for Azure DevOps](https://learn.microsoft.com/power-platform/alm/devops-build-tools) – Microsoft Learn, checked 2026-10-27.

<!-- Glossary terms introduced in this chapter: Bicep, infrastructure as code, deployment storage, site update strategy, rolling update, OpenID Connect, federated credential, workload identity federation, managed solution -->

<!-- expanded: deep dive + guided lab -->
