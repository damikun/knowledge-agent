# Tools, notifications and models

Connections give your workspace access to other systems. An admin sets them up once: tools from MCP servers on [Tools](/tools), email, Slack and alerts on [Notifications](/notifications), and the AI providers on [AI models](/models#providers).

## Models

AI models has its own page under Settings, with the providers, their API keys and the model for each role. See [AI models](/models#providers) and Connect a model in this help.

## Tools from MCP servers

An MCP server gives agents tools from your other systems, such as looking up an order in your shop or a customer in your CRM.

1. On [Tools](/tools), press Add MCP server and enter its address, and a token if it needs one. Tokens are stored encrypted.
2. Test it to check that it connects and to see its tools.
3. Open an agent on [Agents](/agents) and turn the app on, on its Tools tab. It then uses all of the app's tools, also ones the app adds later. Under Choose tools you can limit it to the tools you pick instead.

Tools that only look things up run on their own. Tools that change something wait for the person to confirm in the chat, or are kept for visitors your website identified. Every tool call is logged on the conversation, so your team sees what happened.

## Email

Email sends requests for a person to your support address and brings replies back into the chat. Fill in sending (SMTP) and the mailbox for replies (IMAP) on [Notifications](/notifications), then send a test. Any mailbox works, including Gmail or Microsoft 365 with an app password.

## Slack

Slack posts requests for a person to a channel, and the team answers in its thread. It uses Socket Mode, so no public address is needed. Create the Slack app from the manifest in the docs, then paste its bot and app tokens and the channel on [Notifications](/notifications).

## Alerts

The Alerts card on [Notifications](/notifications) decides which alerts go to Slack and email. See Alerts in this help.
