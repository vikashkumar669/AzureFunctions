---
chapter: "H"
appendix: true
title: "Interview Questions"
---

Questions for interviewing, or being interviewed for, a Dynamics 365 integration role. Each points to the chapter where the answer is developed. Good answers explain trade-offs, not just definitions.

## Foundations

1. When would you implement a rule as a plug-in, an Azure Function or a Power Automate flow? (1)
2. What runs where in the isolated worker model, and why does it matter for dependencies? (2)
3. How does Flex Consumption scale, and how could scaling hurt Dataverse? (2, 15)
4. What does the 230-second HTTP limit mean for API design? (2, 24)

## Dataverse

5. When would you use the Web API rather than the SDK for .NET? (4)
6. Why are alternate keys important in integrations? (4, 10, 16)
7. How do you page through 200,000 rows safely? (4, 7, 23)
8. Explain optimistic concurrency in Dataverse and how you'd expose it in a REST API. (7)
9. What are the service protection limits, and how do you design within them? (15)
10. On a standard table, what happens when one row in a bulk request fails, and how do you handle it? (15)
11. How does change tracking work, and what happens if a token expires? (10)

## Security

12. Walk through how a function authenticates to Dataverse without a secret. (5)
13. What privileges would you give an integration's application user, and how do you decide? (5)
14. How would you secure a webhook endpoint that Dataverse calls? (9)
15. How do you make sure a user can only export records they can see in Dynamics 365? (22)
16. How do you remove secrets from a CI/CD pipeline? (20)

## Integration patterns

17. What makes an operation idempotent, and how do you make a POST safe to retry? (8)
18. Webhook or Service Bus endpoint for Dataverse events: how do you choose? (9, 11)
19. How do you decide whether a failed message is retried or dead-lettered? (11, 24)
20. Design a nightly incremental load. Where do you store the watermark, and when do you advance it? (10)
21. How would you prevent loops in a two-way synchronization? (21)
22. When are Durable Functions the right tool, and what constraints do they impose? (12)
23. How would you import a partner's file with bad rows in it? (13)
24. How would you plan and prove a data migration? (16)

## Operations

25. How do you trace one business transaction across functions, queues and Dataverse? (18)
26. Which alerts would you set up first for an integration app? (18, 25)
27. How do you deploy to Flex Consumption with no downtime, and how do you roll back? (20)
28. How would you measure bulk-load throughput, and why not trust published numbers? (15)
29. What goes in a production-readiness review? (25)
30. Tell me about an integration failure you investigated: how did you find the cause? (18)
