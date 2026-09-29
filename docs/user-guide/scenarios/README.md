# Agent-X Real-World Scenarios

**Practical workflows built from the features Agent-X has today**

---

## Overview

Each scenario below strings together pages that exist in the app, with the button and page
names the English interface uses. They assume you finished the setup wizard and have an AI
provider and an embedding model working (see the [Quick Start](../getting-started/quick-start.md)).

Two things to keep in mind throughout:

- **Ask Your Files** answers from your documents and cites them. **AI Chat** answers from the
  model and the conversation only: it does not search your Knowledge Vault, so questions
  about your files belong in Ask Your Files.
- Everything that uses the AI model (answers, tags, AI titles, Quick Actions, Workflows)
  sends its text to the active provider. With the built-in model or Ollama on your computer,
  nothing leaves the machine.

---

## Available Scenarios

| Scenario | What you get | Pages used |
|----------|--------------|------------|
| **1. Research Paper Analysis** | Cited answers across a set of papers, side-by-side comparisons, annotated passages | Knowledge Vault, Collections, Ask Your Files, Compare Documents, Quick Actions, Annotations |
| **2. Meeting Notes** | Decisions and action items pulled from a folder of notes that keeps itself up to date | Settings (Watch Folders), Collections, Ask Your Files, Semantic Search, Workflows |
| **3. Code and Technical Documentation** | Answers about a codebase and its docs, found by identifier or by meaning | Knowledge Vault, Semantic Search, Ask Your Files, AI Chat |
| **4. Organizing an Existing Collection** | Duplicates found, collections suggested and created, a backup before and after | Knowledge Vault, Quick Actions, Collections, Knowledge Graph, Backup & Restore |
| **5. Personal Knowledge Base from the Web** | Articles, videos and feeds in one searchable vault | Web Import, Smart Inbox, Collections, Ask Your Files, Semantic Search, Weekly Digest |
| **Advanced: Web and Vault Research** | Live web results next to answers from your own documents | AI Chat (Research Mode), Web Import, Ask Your Files |

---

## Scenario 1: Research Paper Analysis

**Goal:** Understand a set of academic papers quickly, with every claim traceable to a page.

**Prerequisites:**
- The papers as PDF or Word (`.docx`) files
- An embedding model available (the built-in model, or Ollama with `all-minilm`)

### Workflow

**Step 1: Import the papers**

In the **Knowledge Vault**, click **Import Folder** and pick the folder with the papers, or
**Import Files** to choose them one by one. Wait until each paper's badge reads **Indexed**.
The AI model adds up to five tags to each paper; **AI Title** gives a paper a readable title
if its file name is not one.

**Step 2: Put them in a collection**

On the **Collections** page, type a name such as `Literature review` and click
**Create Collection**. Select it, click **Add Documents** and pick the same files: files
already in the vault are added as the existing documents, not imported twice.

**Step 3: Ask across the papers**

Open **Ask Your Files**, choose `Literature review` in the collection list, and ask:

```
What research questions do these papers address, and which methods does each one use?
```

```
Where do the papers disagree about the results, and what reasons do they give?
```

Each answer cites passages as `[1]`, `[2]` and so on. The **Sources** panel shows the paper,
the page and an excerpt for each; **Open source document** shows the file in File Explorer.

**Step 4: Compare two papers side by side**

On **Compare Documents**, select two or more papers, optionally type a focus topic such as
`methodology`, pick a **Detail Level** and click **Compare Documents**. The report lists
**Similarities**, **Differences**, **Contradictions** and **Unique Points by Document**;
**Export Report** saves it.

**Step 5: Summarize one paper**

On **Quick Actions**, choose the paper in the document list. **Summarize** >
**Generate Layered Summary** summarizes it section by section and then as a whole;
**Key Points** > **Extract Key Points** lists its main facts and findings.

**Step 6: Keep the passages that matter**

In the Knowledge Vault, click **Detail** on a paper. Under **DOCUMENT TEXT**, move through the
passages with **Previous passage** and **Next passage**, select a sentence, add a note under
**NEW ANNOTATION** and click **Save Annotation**. The **Annotations** page collects all of them
and **Export as Markdown** writes them to a file for your literature review.

**Outcome:**
- Cited answers across the whole set of papers
- A comparison report and per-paper summaries
- Your own highlights and notes, exported as Markdown

---

## Scenario 2: Meeting Notes

**Goal:** Turn a folder of meeting notes into decisions and action items you can look up.

**Prerequisites:**
- Meeting notes saved as `.txt`, `.md` or `.docx` files in one folder

### Workflow

**Step 1: Watch the notes folder**

In **Settings > Knowledge Vault > Watch Folders**, click **Add Folder** and pick the notes
folder (check **Include subfolders** first if the notes are in subfolders). Agent-X imports
the notes already there and, while it runs, new and changed ones. **Auto-index watch folders**
must be on; it is on by default.

**Step 2: Group the notes**

Create a collection such as `Team meetings` on the **Collections** page and use
**Add Documents** to add the notes.

**Step 3: Pull out decisions and action items**

In **Ask Your Files**, with `Team meetings` selected:

```
List the action items from the meetings, with the owner and due date where the notes give them.
```

```
What was decided about the release schedule, and in which meeting?
```

**Step 4: Find every mention of a person or project**

On **Semantic Search**, choose **Keyword** under **SEARCH MODE** and search for a name or
project code. Keyword mode finds the words themselves; **Semantic** mode finds passages about
the same subject in other words. **Save** keeps the search under **Saved Filters** for next
time.

**Step 5: Turn one set of notes into action items**

Open **Workflows**, pick the **Summarize & Act** template, paste the text of one meeting into
**Input** and click **Run**. The template summarizes the notes, extracts key points and writes a
checklist of action items. **Save as Document** adds the result to the Knowledge Vault, and
**Export Result** saves it to a file.

The **Workflow** button on a vault document also opens this page, with the document's name,
title and the start of its text as input. For a long document, paste its full text instead.

**Outcome:**
- Meeting notes imported automatically as you save them
- Action items and decisions with citations to the meeting they came from
- Summaries saved back into the vault

---

## Scenario 3: Code and Technical Documentation

**Goal:** Get answers about a codebase and its documentation without reading every file.

**Prerequisites:**
- Source files in a supported language (C#, JavaScript, TypeScript, Python, Java, C, C++, Go,
  Rust, Swift, Kotlin, Ruby, PHP, SQL, shell scripts, and more) and the project's docs

### Workflow

**Step 1: Import the repository**

In the **Knowledge Vault**, click **Import Folder** and pick the repository folder. Agent-X
imports every supported file in it and its subfolders, including Markdown docs and
configuration files (`.json`, `.yaml`, `.toml`, `.ini`).

**Step 2: Look up identifiers**

On **Semantic Search**, choose **Keyword** mode and the **Code** file type, and search for a
class, function or error code. Each result shows the file, an excerpt and the chunk it came
from; **Open** shows the file in File Explorer.

**Step 3: Ask how things work**

In **Ask Your Files**:

```
How does the application load its settings at startup, and which files are involved?
```

```
Which parts of the code handle authentication, and what happens when a token expires?
```

The citations point to the files and chunks the answer is based on.

**Step 4: Discuss a snippet with the AI**

For a general question about code you paste in, use **AI Chat**. The prompt button in the top
bar applies one of the built-in system prompts, such as **Code Helper** or
**Technical Explainer**, to the conversation. Remember that chat does not read the vault:
paste the code you want to discuss.

**Outcome:**
- Identifiers found by keyword, concepts found by meaning
- Answers about the codebase with citations to the files

---

## Scenario 4: Organizing an Existing Collection

**Goal:** Bring an unsorted pile of documents into the vault and give it structure.

**Prerequisites:**
- The documents in one or more folders

### Workflow

**Step 1: Back up first**

On **Backup & Restore**, choose a **Destination Folder**, optionally check
**Encrypt backup (AES-256)** with a password, and click **Create Backup**. Backups are
`.agentxbak` files that **Restore from Backup** can bring back.

**Step 2: Import everything**

In the **Knowledge Vault**, use **Import Folder** for each folder. Files whose content is
already in the vault are skipped, and the summary says how many.

**Step 3: Find duplicates**

On **Quick Actions**, open **Duplicates**. **Scan Exact Duplicates** groups files with identical
content and shows the space they take; **Scan Semantic Near-Duplicates** groups documents that
say nearly the same thing. Both scans only report: delete the copies you do not want from the
Knowledge Vault (deleting never removes the file on disk).

**Step 4: Get collection suggestions**

On **Quick Actions**, open **Organize** and click **Analyze & Suggest**. For up to 20 documents
that are in no collection, it suggests a collection and tags, with its reasoning. The
suggestions are not applied for you.

**Step 5: Create the structure**

On **Collections**, create the collections you settled on and fill them with **Add Documents**.
To nest one collection in another, select it and use **Move into...**; collections nest one
level deep.

**Step 6: Check the result**

The **Knowledge Graph** draws documents, collections and tags as nodes, so documents that share
a collection or tag cluster together and stray documents stand out. Click a collection or tag
to highlight its cluster. When you are done, create another backup.

**Outcome:**
- Everything imported once, with duplicates identified
- A collection structure that Ask Your Files and Semantic Search can be limited to
- Backups from before and after the reorganization

---

## Scenario 5: Personal Knowledge Base from the Web

**Goal:** Collect articles, videos and posts you want to keep, and find them again by meaning.

**Prerequisites:**
- For clipping while you browse: the Agent-X browser extension, paired with the token from
  **Settings > Connections**

### Workflow

**Step 1: Import pages you already know**

On **Web Import**, paste URLs under **Enter URLs**, one per line (web articles and YouTube
videos), optionally choose a collection under **Add to Collection (optional)**, and click
**Import All**. **Preview First URL** shows the title, site, author and word count first.

**Step 2: Import a feed or a site**

Still on **Web Import**, **RSS / Atom Feed** imports every item the feed lists at that moment
(it is read once, not followed), and **Sitemap Import** imports up to 100 pages found in a
`sitemap.xml`.

**Step 3: Clip while you browse**

Pages you clip with the browser extension land in the **Smart Inbox**. **Generate AI Previews**
suggests a collection and tags for each clip; **Accept** imports it into the Knowledge Vault
(into the collection you chose, or the suggested one), **Defer** keeps it for later, and
**Reject** discards it.

**Step 4: Organize by interest**

Create collections such as `Cooking`, `Travel` or `Career` on the **Collections** page, or
choose them when importing and accepting.

**Step 5: Ask and search**

In **Ask Your Files**:

```
What have I saved about sourdough starters, and what do the sources disagree on?
```

On **Semantic Search**, save the searches you repeat with **Save** so they appear under
**Saved Filters**.

**Step 6: Review the week**

On **Weekly Digest**, **Generate New Digest** summarizes the week: new documents,
conversations, searches, tokens used, top searches and collections, and conversation
highlights. **REPORT HISTORY** keeps earlier digests.

**Outcome:**
- Web content from several sources in one vault
- Answers with citations to the pages you saved
- A weekly overview of what you collected

---

## Advanced Scenario: Web and Vault Research

**Goal:** Research a topic with current web results and with what you already have.

**Prerequisites:**
- A web search provider: a Brave or Serper API key, or the address of a SearXNG instance

### Workflow

**Step 1: Turn on Research Mode**

In **Settings > Research Mode**, switch on **Enable Research Mode**, choose the
**Search Provider** and enter its key or address, then click **Save Settings**. Research Mode
is off by default. With it on, the questions you send in Research Mode go to the search
provider.

**Step 2: Search the web from AI Chat**

In **AI Chat**, switch on the Research mode button next to the message box and ask your
question. The answer uses the search results and lists them under **Web sources**; click one
to open the page.

**Step 3: Keep the useful pages**

Paste the URLs worth keeping into **Web Import** and click **Import All**, or clip them with
the browser extension.

**Step 4: Ask your own documents**

Research Mode never searches your vault. Once the pages are indexed, ask the same question
in **Ask Your Files** to get an answer from your saved pages and existing documents, with
citations.

**Outcome:**
- Current web results with their sources
- The pages worth keeping, indexed next to your own documents
- Cited answers from everything you have

---

## Tips for Success

1. **Start small.** Import a few documents first and check that they reach **Indexed** before
   importing thousands.
2. **Use collections.** Limiting Ask Your Files to a collection keeps answers focused.
3. **Ask specific questions.** "Which papers use a control group?" retrieves better passages
   than "Tell me about the papers."
4. **Check the sources.** The citations show where an answer came from; open the source when
   it matters.
5. **Back up before big changes.** Backup & Restore takes a minute and makes a reorganization
   reversible.

---

## Scenario Templates

Use this outline to write down your own scenarios:

```markdown
# Scenario Name

**Goal:** [What you are trying to accomplish]
**Prerequisites:** [What you need before starting]

### Workflow

**Step 1: [Action]**
[Which page, which button, what to type]

**Step 2: [Action]**
[Which page, which button, what to type]

...

### Expected Outcome

[What success looks like]

### Variations

[Alternative approaches or special cases]
```
