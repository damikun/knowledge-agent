# Add knowledge

The agent answers from your sources: web pages, PDF, TXT and Markdown files, and text you paste. Add and organize them on [Sources](/sources).

## Add a source

1. Open [Sources](/sources) and press Add a source.
2. Choose Website (one page or a whole site), Files (PDF, TXT or Markdown, up to 25 MB each) or Text (paste or type it).
3. Save. The source is read in the background: fetching, extracting, chunking and embedding. The list shows each stage with a progress bar.

A source is Ready when it is searchable. A Failed source shows its error when you open it.

## Add a website

Under Website, choose how much of the site to read: This page only, This page and the pages under it (the default), or The whole site. Max pages sets how many pages are read, 100 unless you change it. Under Advanced you can leave parts of the site out, one pattern per line, such as `/blog/*`.

Each page becomes a source of its own, so answers cite the page they came from, and the pages land in the folder you added the website to. Open the website on [Sources](/sources) to see its pages with their status while they are read. Deleting the website deletes its pages. A page marked Needs a browser shows almost no text until JavaScript runs, so nothing was indexed from it; add that content as a file or as text instead.

## Organize sources in folders

Folders only organize the list. Drag sources onto a folder, or select them and use Move to folder. Folders nest up to three levels. Search and agents never see folders, except when an agent is limited to some of them on its Knowledge tab ([Agents](/agents)).

## Keep sources for your team only

The Internal folder, with the lock, is for documents only your team should get answers from, such as your own procedures. Only the help assistant searches it; no agent does, so its sources never reach a widget, an agent's MCP server or its HTTP API, even for an agent that answers from all sources. Add sources and subfolders inside it like anywhere else; the folder itself cannot be renamed, moved or deleted.

## Suspicious content

A source gets the Suspicious content label when some of its passages look written to the assistant rather than to readers, such as text that names the agent's tools or says "ignore previous instructions". This often comes from comments or reviews on crawled pages. Open the source and read those passages; remove the source, or exclude those pages from the crawl, if they are not yours. The agent treats every passage as information, never as instructions, either way.

## Keep web pages current

Re-ingest a source to read it again. Re-ingesting a website reads it again too: new pages are added, pages that are gone are removed, and unchanged pages are kept as they are. A schedule on the System page can refresh every web source at night; it is off by default. See [Schedules](/system?tab=schedules).

## Why a source failed

- The page could not be reached, or it answered with an error such as 404.
- The address points to a private network, which is blocked for safety unless the server allows it.
- The site's robots.txt does not allow reading the page.
- The file has no text, such as a scanned PDF without text.

Open the source on [Sources](/sources) to see its error, fix the cause and re-ingest it.

## Test what the agent finds

Ask a question in the [Playground](/playground). Each answer shows the sources it cites; open one to see the passage it used.
