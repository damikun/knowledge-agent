# Connect a model

The agent needs two models: a chat model that writes the answers and an embedding model that makes your sources searchable. Until both are connected, the portal shows a setup banner, the chat cannot answer and new sources wait. Add them on [AI models](/models#providers).

## Add an API key

1. Open AI models (under Settings) and go to Providers & API keys ([AI models](/models#providers)).
2. Choose a provider, paste the key and save.
3. Press Test connection to check the key without spending tokens.

Keys are stored encrypted and never shown again, only their last four characters. A key saved in the portal wins over one in the server configuration; Reset to configuration removes it.

## Which providers to pick

- Anthropic: Claude writes the answers. Claude has no embeddings, so add Voyage, OpenAI or Ollama for them.
- OpenAI: one key gives both the chat model and embeddings.
- Voyage: embeddings, and it also reranks search results for better answers.
- Ollama: open models on your own server with no key, for chat and embeddings.

A common choice is Anthropic plus Voyage, or OpenAI alone.

## Choose the model for each role

The AI models page lists the roles: answering, research, utility (titles and labels), judge (evaluations), rerank and embedding. Each role can use another provider and model. Changes apply to the next request, with no restart. See [AI models](/models).

## Change the embedding model

Vectors made by one embedding model do not work with another. After a change, the Sources page offers to re-index your sources. Until then, search finds only sources indexed with the current model. See [Sources](/sources).

## Sources wait for a model

Sources added before an embedding model was connected show Waiting for a model. They are indexed on their own once an embedding provider is saved. See [Sources](/sources).
