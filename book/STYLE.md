# Style guide

House rules for *Azure Functions for Dynamics 365 & Power Platform*. The build (`book/build/`) enforces the visual rules; this file covers what the build cannot.

## 1. Voice and wording

- Second person, present tense: "You register the identity", not "We will register".
- Plain words. Sentences under 25 words; paragraphs of three sentences or fewer.
- Introduce a term once, in **bold**, then use it the same way everywhere. The glossary is built from those terms.
- Product names exactly as Microsoft writes them: Azure Functions, Dataverse, Dynamics 365, Power Platform admin center, Microsoft Entra ID, Application Insights, Flex Consumption plan.
- Never "simply", "just", "easy". Never claim a number you did not measure.
- Every Microsoft-specific claim that can change (limits, versions, portal paths, availability) cites Microsoft Learn in the chapter's References with the date checked.
- Label each statement's status where it matters: **Microsoft guidance** (cited), **recommendation** (the author's engineering advice), **illustrative** (example data or mockups).
- Fictional names only: Harbourline Group, Harbourline Connect, LedgerOne, SignBridge, people from the fictional staff list in Appendix F. No real customer data, tenant IDs or credentials, ever.

## 2. Structure

- Every chapter starts from `templates/chapter-template.md` and keeps its 21 sections unless one is genuinely empty.
- Headings: H1 is the chapter title (from front matter); H2 for template sections; H3 for steps ("Step 3 – Register the application user"). No H4.
- Cross-reference with `@fig:`, `@lst:`, `@tbl:` and "Chapter 5", never "see above" or "the next section".

## 3. Callouts

| Class | Use for | Icon / colour |
| --- | --- | --- |
| `tip` | A shortcut or better way | Azure blue, lightbulb |
| `note` | Context, "if your screen differs" | Slate |
| `warning` | Data loss, breaking change, cost trap | Amber |
| `security` | Secrets, permissions, exposure | Crimson |
| `performance` | Limits, throughput, memory | Teal |

One idea per callout, 60 words or fewer, no more than three callouts per page.

## 4. Code

- Listings are pulled from compiled projects with region markers; never paste code into the manuscript by hand:

  ```csharp
  // <listing:dataverse-factory>
  ...code...
  // </listing:dataverse-factory>
  ```

- 72 characters per line at most (`.editorconfig` sets `max_line_length = 72`).
- Callout markers are comments `// <1>`, `// <2>` at the end of a line. The build turns them into ❶ ❷ and every listing with markers is followed by a numbered explanation list.
- Shell commands as `bash` blocks; PowerShell only where Windows differs.
- Placeholders in angle brackets: `<your-dev-org>`, `<resource-group>`. Never real values.

## 5. Figures

### 5.1 Diagrams

- Authored in Mermaid (`figures/chNN/*.mmd`, rendered by the build with `build/mermaid-config.json`) or draw.io for icon-based architecture.
- **Canvas size: 640 units wide** (`viewBox="0 0 640 H"`), which maps to the 5.98 in text column at about 0.67 pt per unit. Minimum text size 11 units (7.4 pt in print); body labels 12.5 units; titles 14–15 units. A wider canvas shrinks every label below a readable size.
- One message per diagram, stated in the caption: "The function never holds a secret: Entra ID issues tokens to its managed identity", not "Architecture".
- Left to right for data flow; top to bottom for hierarchies.
- Colours: navy boxes for our code, white boxes for Microsoft services, sky-tint for external systems; dashed borders for authentication boundaries; amber arrows for failure and retry paths.
- Microsoft's [Azure architecture icons](https://learn.microsoft.com/azure/architecture/icons/) may be used inside architecture diagrams under their terms. Microsoft logos are never used on the cover.

### 5.2 Screenshots

- Captured on the author's computer in the book's sandbox tenant (`Harbourline-Dev`). Light theme, 100% zoom, at least 1600 px wide, PNG.
- Each screenshot is planned first as a row in `capture-plan.csv`; until captured, the manuscript holds a `::: capture` placeholder that the build renders as a clearly labelled **ILLUSTRATIVE PLACEHOLDER**.
- Annotations: Azure-blue numbered circles, 2 pt arrows, five callouts at most. Mask tenant IDs, object IDs and e-mail addresses with solid navy bars.
- Caption ends with "Captured <date>, <portal area>".

## 6. Tables

- Every table has an id and caption (`::: {#tbl:id caption="..."}`), a header row, units in headers, and is referenced in the text.
- Decision matrices put the recommended option in the first column after the criteria.

## 7. Accessibility

- Every figure has alt text (the caption text is used) that states what the figure shows.
- Colour never carries meaning alone: failure paths are amber **and** labelled.
- Text contrast at least 4.5:1; the palette in `build/book.css` is checked.
- The PDF is tagged; headings are real headings; tables have header rows.

## 8. Palette and type (implemented in `build/book.css` and `build/reference.docx`)

| Token | Hex | Use |
| --- | --- | --- |
| Navy | `#0B1F3A` | Headings, body text, code background |
| Azure blue | `#0F6CBD` | Chapter numbers, links, figure labels |
| Sky tint | `#E8F2FB` | Tip boxes, table headers |
| Slate | `#4A5568` | Captions, secondary text |
| Amber | `#9A5100` on `#FFF4E0` | Warnings |
| Crimson | `#B4232A` on `#FDECEC` | Security notes |
| Teal | `#0E7C66` on `#E6F5F1` | Performance notes |

Type: headings Inter; body Source Serif 4 at 10.5 pt; code Cascadia Code at 8.5 pt. All three are under the SIL Open Font License and are embedded in the PDF.
