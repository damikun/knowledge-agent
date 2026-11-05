# Alerts

Alerts tell you what needs attention now. The bell in the header lists them on every page; they can also go to Slack and email.

## What raises an alert

- Visitors waiting for a person longer than the threshold (5 minutes by default), and requests nobody answered in the last 24 hours.
- No chat or embedding model, or an API key the provider rejected. Fix it on [AI models](/models#providers).
- Sources that failed or wait for an embedding model ([Sources](/sources)).
- Slack down for a while, or requests that could not be posted or emailed ([Notifications](/notifications)).
- Quality: ratings or thumbs much lower than the week before, or many more unanswered questions than usual ([Analytics](/analytics)).

Support users see only waiting visitors and missed requests.

## Snooze or dismiss

Admins can snooze an alert for a while or dismiss it. It comes back when its count grows, it gets more severe, or it resolves and comes back.

## Send alerts to Slack and email

On [Notifications](/notifications), the Alerts card sends new alerts to the Slack channel and the support address, and chooses which kinds and the waiting threshold. Each alert is sent once, and again only after it resolved and came back.
