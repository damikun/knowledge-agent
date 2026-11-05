# Developer shortcuts for the Knowledge Agent. `make` (or `make help`) lists the targets.
.DEFAULT_GOAL := help
SHELL := /bin/bash

.PHONY: help dev dev-backend api api-inmemory web-dev up infra down reset logs ps reset-aspire smoke test test-fast test-web build eval eval-llm eval-baseline eval-sync

help: ## List the targets
	@grep -E '^[a-zA-Z_-]+:.*?## ' $(MAKEFILE_LIST) | awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-13s\033[0m %s\n", $$1, $$2}'

# --- Aspire --------------------------------------------------------------------------------------------------
dev: ## Aspire: infrastructure + Langfuse + API + portal, with the dashboard
	dotnet run --project src/KnowledgeAgent.AppHost

dev-backend: ## Aspire without the portal (run `make web-dev` yourself)
	dotnet run --project src/KnowledgeAgent.AppHost -- --Portal:Enabled=false

reset-aspire: ## Delete the Aspire data volumes (stop the AppHost first)
	@vols=$$(docker volume ls -q --filter name=knowledge-agent-aspire-); \
	if [ -n "$$vols" ]; then docker volume rm $$vols; else echo "no Aspire volumes"; fi

# --- Without Aspire ------------------------------------------------------------------------------------------
api: ## API on http://localhost:5080 against local infrastructure (e.g. `make infra`)
	dotnet run --project src/KnowledgeAgent.Api --launch-profile http

api-inmemory: ## API on http://localhost:5080 without Docker (in-memory stores, embedded prompts)
	dotnet run --project src/KnowledgeAgent.Api --launch-profile inmemory

web-dev: ## Portal dev server on http://localhost:5173 (proxies /api to http://localhost:5080)
	cd web && { [ -d node_modules ] || npm install; } && npm run dev

# --- Docker Compose ------------------------------------------------------------------------------------------
up: ## Build and start the full stack (portal :8080, API :5080, Langfuse :3000)
	docker compose up --build -d

infra: ## Start only Postgres, ClickHouse, Redis, MinIO and Langfuse
	docker compose up -d langfuse-web langfuse-worker

down: ## Stop the stack (keeps the data volumes)
	docker compose down

reset: ## Stop the stack and delete its data volumes
	docker compose down -v

logs: ## Follow the logs of all services
	docker compose logs -f --tail=100

ps: ## Service status
	docker compose ps

# --- Build and test ------------------------------------------------------------------------------------------
build: ## Build the .NET solution and the portal
	dotnet build KnowledgeAgent.slnx
	cd web && { [ -d node_modules ] || npm install; } && npm run build

test: ## All .NET tests (container suites skip without Docker) and the web tests
	dotnet test --solution KnowledgeAgent.slnx
	cd web && { [ -d node_modules ] || npm install; } && npm test

test-fast: ## .NET tests without the Testcontainers suites
	KA_SKIP_CONTAINER_TESTS=1 dotnet test --solution KnowledgeAgent.slnx

test-web: ## Web tests (Vitest)
	cd web && { [ -d node_modules ] || npm install; } && npm test

# --- Evaluations ---------------------------------------------------------------------------------------------
eval: ## Golden set with your configured models and the quality gate (costs tokens; keys from user secrets or env)
	dotnet run --project src/KnowledgeAgent.Eval -- --gate

eval-llm: ## Golden set with your configured LLM and the LLM judge (costs tokens; keys from user secrets or env)
	dotnet run --project src/KnowledgeAgent.Eval -- --judge

eval-baseline: ## Accept this run as the baseline of its model configuration (commit evals/datasets/*/baselines/)
	dotnet run --project src/KnowledgeAgent.Eval -- --update-baseline

eval-sync: ## Create or update the Langfuse datasets of the golden sets
	dotnet run --project src/KnowledgeAgent.Eval -- sync

smoke: ## Smoke-test a running stack (compose defaults; PORTAL_URL=http://localhost:5173 for Aspire)
	scripts/smoke-test.sh
