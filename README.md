# Knowledge Agent

A self-hosted AI support agent. Add your web pages, files and text, create agents that answer from them with
citations, and put an agent on your website as a chat widget that hands visitors to your team when needed.

Stack: .NET 10, PostgreSQL, ClickHouse, Langfuse, Inngest, Keycloak, .NET Aspire, React 19.

## Getting started

Requirements: [.NET SDK 10](https://dotnet.microsoft.com/download), Docker with 6 GB of memory and Node.js 22.

```bash
dotnet run --project src/KnowledgeAgent.AppHost
```

Or without .NET and Node:

```bash
cp .env.example .env && docker compose up --build -d
```

The portal runs on http://localhost:5173 (Aspire) or http://localhost:8080 (Docker Compose).
Sign in with `admin` / `admin`, then add a chat model and an embedding model under **AI models**.

## Development

```bash
dotnet test --solution KnowledgeAgent.slnx
cd web && npm run lint && npm test
```
