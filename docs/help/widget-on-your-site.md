# Put a widget on your site

A widget puts a chat with one of your agents on any website with one script tag. Create and design it on [Widgets](/widgets/new).

## Create a widget

1. Create an agent first if you have none yet ([Agents](/agents/new)). The built-in agent cannot answer on a website.
2. Open [Widgets](/widgets/new), choose the agent and give the widget a name.
3. Click any part of the live preview, such as the launcher, the header or a message, to change its texts and colors. The Theme tab sets the look of the whole widget: Glow, Glass or Solid, your brand color, and light, dark or the visitor's system mode.
4. Save. Undo and Redo work until you leave the editor.

## Paste the snippet

1. Open the widget on [Widgets](/widgets) and open its Embed code section.
2. Press Copy snippet and paste the script tag into every page that should show the chat, before the closing body tag.
3. Open your site: the launcher appears in the corner. The setup step on [Home](/home) is done after the first conversation.

## Set the public address

The snippet, the identify and server code, an agent's MCP and API addresses, and the links in emails and Slack messages use the public address of this installation, such as https://support.acme.com. Admins set it under Settings > System ([General](/system#public-address)). Until it is set, the code shows the address in your browser, or yourdomain.com while the portal runs on your own computer.

## Allowed sites

In the widget's settings, list the sites that may show it, such as https://shop.example.com. Other sites then cannot embed it. With no sites listed, any site may.

## Limits and behavior

The widget's settings also hold the greeting and suggested questions, whether visitors can ask for a person, opening hours, how many messages a visitor may send per minute, a daily cap for the widget, and how long conversations are kept (90 days by default). Quiet conversations close on their own: after 30 minutes with the assistant and 24 hours after the team's last reply, unless you change it. See [Widgets](/widgets).

## Test the widget

Choose the widget in the [Playground](/playground) to chat as its visitors would, with its agent and handover settings. Playground tests count nowhere.
