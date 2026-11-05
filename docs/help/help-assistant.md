# The help assistant

The help assistant answers your team from two places: this help, which matches the version you run, and your Internal sources on the [Sources](/sources) page. It is built in, has no settings of its own and cannot be edited.

## What it can do

- Explain how to set up and use every page, with links that open the page.
- Tailor answers to the page you are on and to the setup steps still missing.
- Read the sections of this help that fit your question and write an answer that cites them. It needs a chat model: without one the panel says "No AI model connected" and admins get a link to [AI models](/models).
- Search the sources your team filed in the Internal folder, such as your own procedures, and say whether an answer comes from the help or from them. Internal citations open the source on the Sources page. This needs an embedding model; without one, or with an empty Internal folder, it answers from the help alone.

## What it never does

- It does not change your sources, and reads none of them except those in the Internal folder. It does not read or change your agents, widgets, conversations or contacts.
- It has no integrations and never hands you to a person.
- Its chats count nowhere: not in Analytics, Home, the Inbox, Contacts, alerts, labels, feedback or datasets. The chat lives in your browser tab and is gone when you close it.

## Turn it on or off

Admins turn the help assistant on or off for everyone under Settings > System ([System](/system)). When it is off, the question mark in the header is hidden.
