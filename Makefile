.PHONY: docs
.PHONY: immichFrame.Web

dev:
	dotnet run --project ./ImmichFrame.WebApi

test-webapi:
	dotnet test ./ImmichFrame.WebApi.Tests/ImmichFrame.WebApi.Tests.csproj

test-core:
	dotnet test ./ImmichFrame.Core.Tests/ImmichFrame.Core.Tests.csproj

test-ops:
	bash -n scripts/*.sh scripts/tests/*.sh
	bash scripts/tests/smoke-prod-test.sh
	bash scripts/tests/deploy-prod-test.sh

docs:
	npm --prefix docs run start

api:
	curl http://localhost:5217/swagger/v1/swagger.json -o ./openApi/swagger.json
	npm --prefix immichFrame.Web run api


# Local single-platform build of the production image. Override e.g. PLATFORM=linux/arm64 or
# IMAGE=immichframe:custom; set NO_CACHE=--no-cache for a clean rebuild.
VERSION ?= $(shell sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' ImmichFrame.WebApi/ImmichFrame.WebApi.csproj)
PLATFORM ?= linux/amd64
IMAGE ?= immichframe:local
NO_CACHE ?=

docker-build-prod:
	docker buildx build --platform $(PLATFORM) $(NO_CACHE) --load . --target final -t $(IMAGE) --build-arg VERSION=$(VERSION)

docker-prod:
	docker compose -f ./docker/docker-compose.yml up --build -V --remove-orphans

docker-prune:
	docker system prune -a
