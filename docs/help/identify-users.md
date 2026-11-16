# Identify signed-in users

Visitors are anonymous until your website tells the widget who is signed in. Identified users get one contact across browsers and devices, and their open chat follows them. Turn it on in the widget editor under Embed code ([Widgets](/widgets)).

## Anonymous visitors

The widget remembers a visitor in its own frame for 180 days after their last visit; nothing is stored on your website. A visitor who comes back continues their open chat, and More options in the chat header lists their earlier conversations: an open one goes on, an ended one can be read. Contacts count visits, a new one after 30 minutes without activity. If the browser blocks storage in the frame, the visitor stays the same only until the page reloads.

## Turn on identity verification

1. Open the widget on [Widgets](/widgets) and choose Embed code.
2. Turn on identity verification and copy the secret. It shows only once; you can create a new one there, and the old one stops working at once.
3. Store the secret on your server, never in the page.

## Compute the user hash on your server

Your server signs the user id with the secret: userHash = hex(HMAC-SHA256(secret, user id)). Embed code shows ready snippets for Node.js, C# and Python. Then pass the user to the widget:

- Pages rendered on the server: add data-user-id, data-user-hash, and optionally data-user-name and data-user-email to the script tag.
- Single-page apps: call window.KnowledgeAgent.identify({ userId, userHash, name, email }) after sign-in, and window.KnowledgeAgent.logout() on sign-out. After logout() the next visitor starts anonymous and does not see the previous user's chats.

A missing or wrong hash is ignored, and the visitor stays anonymous.

## Only signed-in users

Under the widget's security settings you can show the chat only to identified users. The launcher then appears after identify(). It needs identity verification on.

## Send attributes about the user

Plan, company or seats can travel with the user as contact attributes: from the page with update(), or, trusted, from your server. Staff filter contacts by them on [Contacts](/contacts), and you can let the agent see chosen attributes. See Labels and segments in this help.

## Where identified users show

Each identified user is one contact on [Contacts](/contacts), with every conversation from any device; what they did before signing in joins that contact. In the widget they see their conversations from every device. Admins can export or erase a contact there.
