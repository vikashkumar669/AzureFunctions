---
chapter: "E"
appendix: true
title: "Troubleshooting Catalogue"
---

Every error table from the chapters, in one place, by chapter. Search this appendix for the message or symptom you see; the chapter has the context.

## Chapter 2 – Azure Functions Architecture and Core Concepts

| Symptom | Likely cause | Fix |
|---|---|---|
| Timer fires at the "wrong" hour | Schedules are in UTC; `WEBSITE_TIME_ZONE` isn't supported on Flex | Convert the time to UTC in the expression |
| Blob function never fires on Flex | Trigger uses the default polling source | Set `Source = BlobTriggerSource.EventGrid` and add the event subscription |
| HTTP caller gets a timeout at about four minutes | 230-second HTTP limit | Return 202 and continue asynchronously |
| Host fails to start | `AzureWebJobsStorage` missing or start-up code slow | Add the setting; keep `Program.cs` free of network calls |
| Requests rejected under load | Maximum instance count set too low | Load-test, then raise the limit |
| Dataverse returns 429 under load | Instances × concurrency exceeds Dataverse limits | Lower concurrency or instances; batch and back off (Chapter 15) |

## Chapter 3 – Development Environment Setup

| Symptom | Likely cause | Fix |
|---|---|---|
| `Missing value for AzureWebJobsStorage` | No `local.settings.json` | Copy the sample file (Step 5) |
| Host waits, then fails on storage | Azurite not running | Start Azurite first |
| `The Functions extensions project was not restored` warning | Build restores the extensions project during the build | Harmless; a restore first removes it |
| Port 7071 in use | Another host still running | Stop it, or `func start --port 7072` |
| `DefaultAzureCredential failed to retrieve a token` | Not signed in, or wrong tenant | `az login --tenant <tenant-id>` |
| Build fails only on your machine | Different SDK | Install the version in `global.json` |

## Chapter 4 – Understanding the Dataverse Web API and SDK

| Symptom | Likely cause | Fix |
|---|---|---|
| `PrivilegeDenied` (0x80040220) when creating a table | Your role lacks customization privileges | Use System Customizer or Administrator in your developer environment |
| `Could not find entity hl_property` in a query | Wrong logical name or schema not created | Check the name in `Schema.cs`; run the setup tool |
| Web API 404 on `hl_property` | Used the logical name, not the entity set name | Use `hl_properties` |
| Upsert by key fails right after creating the key | Key index still building | Wait until the key status is Active |
| Choice filter returns nothing | Filtered on the label | Filter on the value (`100000000`) |
| Exactly 5,000 rows every time | Paging not implemented | Follow the paging cookie or `@odata.nextLink` |

## Chapter 5 – Authentication and Secure Connectivity

| Symptom | Likely cause | Fix |
|---|---|---|
| `DefaultAzureCredential failed to retrieve a token` locally | Not signed in, or signed in to another tenant | `az login --tenant <tenant-id>` |
| `AADSTS500011`: resource principal not found | Scope URL wrong (typo or wrong region) | Copy the URL from the admin center |
| `AADSTS700016`: application not found in the directory | Wrong tenant or client ID | Check `AZURE_TENANT_ID`, `AZURE_CLIENT_ID` |
| HTTP 401/403 from Dataverse with a valid token | No application user for this identity | Create it (Step 3) |
| `PrivilegeDenied` (0x80040220) naming a privilege | Role lacks that privilege | Add exactly that privilege to the role |
| App setting value starts with `@Microsoft.KeyVault(` at run time | Reference not resolved | Grant *Key Vault Secrets User*; check the secret name |

## Chapter 6 – Your First Azure Function Connected to Dataverse

| Symptom | Likely cause | Fix |
|---|---|---|
| Host fails at start-up: `DataAnnotation validation failed for 'DataverseOptions'` | `Dataverse__Url` missing or not a URL | Set the app setting |
| 500 with `PrivilegeDenied` in the logs | Application user lacks `prvReadhl_property` | Add the privilege to the role |
| 500 on the first call after deployment, 200 afterwards | First call pays for authentication and metadata | Expected; see Chapter 15 on warm-up |
| 404 for a code that exists | Key not active, or wrong environment | Check the key status and `Dataverse__Url` |
| 401 in Azure | Missing `code` query parameter | Pass the function key |

## Chapter 7 – CRUD APIs for Dynamics 365

| Symptom | Likely cause | Fix |
|---|---|---|
| Every PATCH returns 412 | Client sends the ETag without quotes, or an old one | Send the latest `ETag` value exactly |
| 409 on create | Code already exists (`DuplicateRecordEntityKey`) | Expected; show "code already used" |
| Pages repeat or skip rows | Sort order not unique, or cookie not passed | Add the ID as last sort key; pass the cookie |
| 400 "token does not match this query" | Client changed filters but kept the token | Start again without a token |
| 500 with `ConcurrencyVersionNotProvided` | `IfRowVersionMatches` without a version | Only set the behaviour when `If-Match` is present |

## Chapter 8 – Integrating External REST APIs

| Symptom | Likely cause | Fix |
|---|---|---|
| `InvalidOperationException` about sampling duration at start-up | Circuit breaker sampling < 2 × attempt timeout | Raise the sampling duration |
| Relative URL calls the wrong path | Base address without trailing slash | End `BaseUrl` with `/` |
| `BrokenCircuitException` | Circuit open after repeated failures | Expected; the partner is down. Treat as transient |
| `TimeoutRejectedException` | Attempt or total timeout reached | Check the partner's latency; adjust timeouts deliberately |
| Duplicate customers in LedgerOne | Retried write without idempotency | Use `PUT` or an idempotency key; disable retry otherwise |
| 401 from SignBridge | Identity not granted on SignBridge's app registration | Ask the partner to grant your identity's app ID |

## Chapter 9 – Webhooks, Plug-ins, and Power Automate Integration

| Symptom | Likely cause | Fix |
|---|---|---|
| System Job: 401 | Wrong or rotated function key | Update the webhook's key value |
| Function returns 403 | `Dataverse__OrganizationId` doesn't match | Set it for this environment |
| Log: "event has no lease number" | Post-image missing or lacks `hl_leasenumber` | Register the image with that column |
| Function called on every lease edit | No filtering attributes | Filter on `hl_leasestatus` |
| Users see "Endpoint unavailable" | Step is synchronous and the function failed | Make the step asynchronous |
| Context has no `InputParameters` | Payload over 256 KB (header `x-ms-dynamics-msg-size-exceeded`) | Read the row from Dataverse |

## Chapter 10 – Scheduled Jobs and Background Processing

| Symptom | Likely cause | Fix |
|---|---|---|
| Job runs at the wrong hour | Schedule written in local time | Write it in UTC |
| `IsPastDue` every morning | App was stopped or deploying at 02:00 | Expected after outages; investigate if constant |
| Rows missing after a crash | Watermark saved before processing finished | Save only after success (rule 3) |
| Same failed row every night | Bad data upstream, e.g. unknown lease | Fix the data or the mapping; the log names the row |
| `RetrieveEntityChanges` fails after a holiday | Token older than the expiry (7 days by default) | Start again without a token: a full load |
| Duplicates after a rerun | Create instead of upsert | Upsert by alternate key |

## Chapter 11 – Azure Service Bus and Event-Driven Architecture

| Symptom | Likely cause | Fix |
|---|---|---|
| `Unauthorized` / claim 'Listen' required | Identity lacks Data Receiver role | Assign the role on the namespace or entity |
| Messages processed twice | At-least-once delivery after a crash or lock loss | Make the handler idempotent |
| `MessageLockLostException` | Processing longer than the lock, no renewal | Raise `maxAutoLockRenewalDuration` or speed up |
| Every message dead-lettered after 10 deliveries | A permanent failure treated as transient | Classify it as permanent; fix the data |
| Dataverse System Job: endpoint failed | Wrong SAS key or topic name | Re-register the endpoint |
| Basic tier: topics unavailable | Wrong tier | Use Standard or Premium |

## Chapter 12 – Durable Functions and Long-Running Workflows

| Symptom | Likely cause | Fix |
|---|---|---|
| Orchestration behaves differently after a restart | Nondeterministic code (clock, GUID, I/O) in the orchestrator | Move it into an activity |
| Orchestration never completes after the event | Timer not cancelled | Cancel the timer when the event wins |
| Event raised but nothing happens | Wrong instance ID or event name | Use the same helper for IDs; constants for names |
| Two envelopes for one lease | Two instances for one lease | Derive instance IDs from the lease number |
| Connection refused on start-up locally | Emulator not running | Start the Docker container |
| 403 from the scheduler in Azure | Identity lacks *Durable Task Data Contributor* | Assign the role on the task hub |

## Chapter 13 – File Processing with Azure Blob Storage

| Symptom | Likely cause | Fix |
|---|---|---|
| Function never fires on Flex | Trigger without `Source = EventGrid`, or no subscription | Set the source; create the subscription |
| Subscription validation fails | Wrong URL or key | Use `/runtime/webhooks/blobs`, `Host.Functions.<name>`, `blobs_extension` key |
| Reports trigger more imports | Subscription without subject filter | Filter on the `property-feeds` container |
| Whole batch fails | Duplicate code in one `UpsertMultiple` | De-duplicate before writing |
| Out of memory on a large file | `byte[]` binding with a huge file | Bind to `Stream`; split into chunks |
| Same file processed twice | Event Grid redelivery or re-upload | Expected and harmless: upsert by key |

## Chapter 14 – Building a Secure Integration API

| Symptom | Likely cause | Fix |
|---|---|---|
| 401 with a fresh token | Wrong audience (`aud`) or missing role | Request the API's `/.default` scope; grant the role and consent |
| 401 from the Function App behind APIM | APIM's identity not in allowed clients, or wrong resource in `authentication-managed-identity` | Match the app's ID URI; allow APIM's app ID |
| 429 for a partner | Over 60 calls a minute | Expected; partner should honour `Retry-After` |
| Policy rejected at deployment | Unsupported policy for the tier | Check the policy's tier support; e.g. `rate-limit-by-key` isn't on Consumption |
| Partners see backend stack traces | No `on-error` handling | Return problem details from `on-error` |

## Chapter 15 – High-Volume Data Processing

| Symptom | Likely cause | Fix |
|---|---|---|
| Throttling fault (`0x80072322` etc.) reaches your code | Load exceeds limits even after client retries | Lower parallelism or instances; bigger batches |
| Whole batch rejected | One bad row in an all-or-nothing batch | Split-on-failure (Step 3) |
| `UpsertMultiple` error about duplicate keys | Same key twice in one request | De-duplicate before batching |
| Timeouts on large batches | Request too big or too slow (plug-ins) | Smaller batches |
| `UpsertMultiple` not supported for a table | Some standard tables don't support bulk messages | Use `ExecuteMultiple` or single requests |
| Fast in test, throttled in production | Other integrations share the user or environment | Measure in production-like conditions; leave headroom |

## Chapter 16 – Data Migration and Reconciliation

| Symptom | Likely cause | Fix |
|---|---|---|
| Reconcile shows many "different" rows | Fingerprint formats differ (decimals, case) | Normalise both sides the same way |
| Rows for a lookup fail with "does not exist" | Parent table not migrated yet | Load parents first |
| Resume skips the wrong rows | Source file changed between runs | Freeze the extract; new run name for new data |
| Upserts fail right after deployment | Alternate key not active yet | Wait for the key to be Active |
| Numbers wrong by a factor of 1,000 | Culture-specific parsing | Parse with an explicit culture (`en-GB` here) |

## Chapter 17 – Excel Export and Import Automation

| Symptom | Likely cause | Fix |
|---|---|---|
| Amounts can't be summed in Excel | Written as text | Write numbers; format with number formats |
| Dates appear as numbers | No date format on the range | Apply a date format |
| Font or sizing errors on Linux | Automatic column sizing measures installed fonts | Set widths explicitly |
| Out of memory on big exports | ClosedXML holds the workbook in memory | Stream with `OpenXmlWriter` |
| Upload returns 400 "Not an Excel workbook" | `.xls` or `.csv` uploaded | Accept `.xlsx` only, or convert first |
| Day and month swapped | Dates parsed as text with a culture | Read typed cell values, not text |

## Chapter 18 – Logging, Monitoring, and Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| No telemetry at all | Exporter not authorised (local auth disabled) | Grant *Monitoring Metrics Publisher*; set the authentication string |
| Host telemetry but no worker logs | Worker OTel not configured | `UseFunctionsWorkerDefaults()` and an exporter |
| Logs missing custom properties | String interpolation instead of templates | Use `LoggerMessage` templates |
| Portal log stream empty | Not supported with OTel mode | Use Live Metrics or queries |
| Huge ingestion bill | Per-row logging or debug level in production | Log per step; raise levels; sample |

## Chapter 19 – Automated Testing and Code Quality

| Symptom | Likely cause | Fix |
|---|---|---|
| `xUnit1051` warning fails the build | A call that takes a `CancellationToken` wasn't given one | Pass `TestContext.Current.CancellationToken` |
| `dotnet test` says VSTest isn't supported | Test runner not set | Set `"test": {"runner": "Microsoft.Testing.Platform"}` in `global.json` |
| Substitute returns null unexpectedly | Arguments didn't match the configured call | Use `Arg.Any<T>()` for parameters you don't care about |
| Test passes alone, fails in a run | Shared static state | Create everything per test |
| Build fails only in CI | Different SDK or warnings | Use `global.json`; build with `-c Release` locally |

## Chapter 20 – Deployment and DevOps

| Symptom | Likely cause | Fix |
|---|---|---|
| `AADSTS70021`: no matching federated identity record | Subject doesn't match repo, branch or environment | Match the subject exactly |
| Role assignment fails | Pipeline lacks rights to assign roles | Grant *User Access Administrator* or *RBAC Administrator* on the group |
| App starts but can't read storage | Role assignment still propagating | Retry; propagation can take minutes |
| Deployment succeeds, functions missing | Package not in `released-package.zip` form or wrong folder | Deploy with the functions action or `func` |
| Brief errors during a release | One instance only, or incompatible change | Expected with one instance; keep changes backward compatible |
| Key Vault reference unresolved after deploy | Secret not created yet | Create secrets before first deployment |

## Chapter 21 – Project 1 — Customer and Contact Synchronization

| Symptom | Likely cause | Fix |
|---|---|---|
| The same customer updates every 5 minutes | Hash not stamped, or fields normalised differently on each side | Stamp after every write; normalise in `Hash()` only |
| A finance edit is lost | Field owner rule wrong for that field | Fix the rule and its test |
| "No account has LedgerOne id" warnings | Customers created in LedgerOne first | Agree the creation process; create accounts |
| Push sees no changes | Change tracking not enabled on `account` | Enable it |

## Chapter 22 – Project 2 — Secure Excel Export API

| Symptom | Likely cause | Fix |
|---|---|---|
| 401 for signed-in users | Authentication not configured or token for another audience | Configure Easy Auth with the right app registration |
| `PrivilegeDenied` on every call | App user lacks *Act on Behalf of Another User*, or it comes via a team | Assign the privilege directly |
| Agent sees nothing | Agent has no Dynamics 365 user or role in this environment | Add the user and role |
| SAS link returns 403 | Key not delegated, or link expired | Grant Blob Data roles; check clock and expiry |

## Chapter 23 – Project 3 — High-Volume Property Search API

| Symptom | Likely cause | Fix |
|---|---|---|
| Rows repeated or skipped across pages | Sort not unique | Always include the ID as last sort key and in the cursor |
| Stale results for longer than expected | Several cache layers adding up | Remember: APIM, function and browser caches each up to 60 s |
| Cache hit rate low | Cache key varies by unimportant parameters | Vary only by parameters that change the result |
| Throttling during the load test | Cache disabled or too many cold misses | Cache at the gateway; rate-limit callers |

## Chapter 24 – Project 4 — Event-Driven Order Processing

| Symptom | Likely cause | Fix |
|---|---|---|
| Partner retries create duplicates | Different message IDs per retry | Use the order number as `MessageId` |
| Replayed messages vanish | Duplicate detection discarded them within the window | Replay after the window, or adjust it for the replay |
| Valid orders dead-lettered | A transient failure classified as permanent | Fix `FailureClassifier`; replay |
| DLQ grows during an outage | Expected: deliveries exhausted | Replay after recovery; consider a longer retry horizon |
