---
# Copy this file to book/chapters/chNN-short-name.md and fill it in.
chapter: NN
part: "Part X — Part title"
title: "Chapter title in title case"
build-summary: "One sentence: what the reader has working at the end."
builds:
  - "First concrete thing the reader builds"
  - "Second concrete thing"
  - "Third concrete thing"
versions: ".NET 10 · Functions 4.x isolated worker · Flex Consumption · Dataverse.Client 1.2.27"
---

<!--
  CHAPTER TEMPLATE — 21 SECTIONS
  Keep every heading below unless the chapter genuinely has nothing for
  it; then delete the heading rather than writing filler. Short chapters
  may merge sections 3+4 and 15+16. Headings use sentence case.

  Conventions (full list in book/STYLE.md):
    Callouts       ::: tip / ::: note / ::: warning / ::: security /
                   ::: performance            (one idea, <= 60 words)
    Code listing   ```{.csharp #lst:id include="src/...cs" region="name"
                      caption="Listing caption"}
                   ```                      (code is pulled from the repo)
    Figure         ![Caption.](figures/chNN/file.png){#fig:id}
    Screenshot     ::: {.capture #fig:id caption="..." app="..."
                      path="..." annotations="1 ...; 2 ..."}
                   :::                      (placeholder until captured)
    Table          ::: {#tbl:id caption="..."}  + pipe table + :::
    Cross-ref      @fig:id  @lst:id  @tbl:id  → "Figure 6.1" etc.
-->

::: objectives
- Objective written as something the reader can do and you can test.
- Four to six objectives.
:::

## The business problem

Two or three short paragraphs. Name who at Harbourline (fictional) has
the problem, what it costs them today, and what "done" looks like.

## How it works

Conceptual explanation. Introduce each new term once, in **bold**, and
add it to the glossary list at the end of this file.

## Architecture

![Caption that states the point of the diagram.](figures/chNN/architecture.png){#fig:arch}

::: {#tbl:components caption="Components in @fig:arch"}
| Component | Role | Authenticates with | Failure path |
| --- | --- | --- | --- |
| | | | |
:::

Data flows left to right. Dashed lines are authentication boundaries;
amber arrows are failure and retry paths; the eye icon marks a
monitoring point.

## Prerequisites

- Chapters this one builds on (cross-reference by number).
- Azure resources that must already exist.
- Repository tag to start from: `ch-NN-start`.

## Step-by-step implementation

### Step 1 – Imperative step title

Explain what and why in two or three sentences, then show the code.

```{.csharp #lst:example include="src/Harbourline.Connect.Core/File.cs" region="example" caption="Listing caption"}
```

Callout explanations follow every listing that has markers:

1. What marker ❶ does and why.
2. What marker ❷ does.

## Screens you will use

::: {.capture #fig:screen caption="Caption describing the screen." app="Azure portal" path="Function App › Settings › Identity" annotations="1 Status toggle; 2 Save; 3 Object (principal) ID"}
:::

Steps on this screen, numbered to match the annotations, then:

**Expected result:** what the reader sees when it worked.

::: note
**If your screen differs:** the most likely reason and where the option
moved to.
:::

## Complete working code

Point to the full files in the repository tag `ch-NN-end`, and list the
solution tree for this chapter.

## Configuration

::: {#tbl:settings caption="App settings used in this chapter"}
| Setting | Local (`local.settings.json`) | Azure | Secret? |
| --- | --- | --- | --- |
| | | | |
:::

## Sample input and output

Request and response, or file in and records out, as listings.

## Testing

Unit tests (Chapter 19 style), then a local run, then a run in Azure.

## Common errors and troubleshooting

::: {#tbl:errors caption="Errors you may see in this chapter"}
| Symptom | Likely cause | Fix |
| --- | --- | --- |
| | | |
:::

## Security considerations

::: security
The single most important security point of the chapter.
:::

## Performance and scalability

::: performance
Only measured numbers, with workload and method; otherwise principles.
:::

## Alternatives and design decisions

A decision matrix when there is a real choice.

## Best practices

- Five to eight, each one line, each actionable.

## Hands-on exercise

::: exercise
**Goal:** … **Starting point:** tag `ch-NN-end`. **Done when:** …
**Stretch:** …
:::

## Knowledge check

::: knowledge-check
1. Question?
2. Question?
3. Question?
4. Question?
5. Question?
:::

Answers are in Appendix G.

::: summary
- Five to seven key takeaways.
:::

## References

- [Page title](https://learn.microsoft.com/...) – Microsoft Learn,
  checked YYYY-MM-DD.

<!-- Glossary terms introduced in this chapter: term, term, term -->
