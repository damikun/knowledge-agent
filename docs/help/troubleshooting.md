# Troubleshooting

Common problems and where to fix them. The System page shows the state of every part of the installation: [System overview](/system?tab=overview).

## The agent does not answer

- No chat model: add an API key on [AI models](/models#providers). The Playground then answers.
- The provider rejected the key: the bell shows an alert. Save a new key and press Test connection on [AI models](/models#providers).
- The provider did not respond in time: try again; check the provider's status page.

## The agent says it cannot find anything

- Sources still being indexed or waiting for an embedding model: check [Sources](/sources).
- The agent is limited to folders that do not hold the right sources: check its Knowledge tab on [Agents](/agents).
- The question uses other words than your sources: add a source that answers it. Analytics lists the questions the agent could not answer ([Analytics](/analytics)).

## A source failed

Open it on [Sources](/sources) to see the error. Pages that cannot be reached, private addresses and files without text fail. Fix the cause and re-ingest it.

## The widget does not show on my site

- The snippet is missing or wrong: copy it again from the widget's Embed code ([Widgets](/widgets)).
- The site is not in the widget's allowed sites: add its address in the widget's settings.
- Only signed-in users is on: the launcher appears only after identify().

## Requests for a person do not reach email or Slack

Check the cards on [Notifications](/notifications) and send a test. A request that could not be posted shows in the Inbox and raises an alert. Every request always reaches the [Inbox](/inbox).

## Background work is stuck

Ingestion, evaluations and email checks run as background jobs. The System page shows their schedules and links to the run history when Inngest runs them ([Schedules](/system?tab=schedules)).
