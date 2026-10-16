COMPOSE = docker compose

.PHONY: build up bup down test test-local dev logs clean

build:
	$(COMPOSE) build

up:
	$(COMPOSE) up

bup: 	down
	$(COMPOSE) up --build -d

down:
	$(COMPOSE) down

test:
	$(COMPOSE) run --rm --build tests

test-local:
	$(COMPOSE) up -d db mailpit
	dotnet test DashboardBackEnd.Tests

dev:
	$(COMPOSE) up -d db mailpit

logs:
	$(COMPOSE) logs -f server

clean:
	$(COMPOSE) down -v
