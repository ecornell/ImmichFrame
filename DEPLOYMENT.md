# Production deployment

The production ImmichFrame instance runs at <http://192.168.1.24:8080/> as the
`immichframe` service in `/opt/stacks/immich/compose.yaml`. The server is reached
as `root@192.168.1.24`, and the Compose service uses the local
`immichframe:custom` image.

## Deploy

From the repository root, run:

```bash
./scripts/deploy-prod.sh
```

The script deploys the current committed working tree. It refuses uncommitted or
untracked changes by default so every production image can be reproduced. It:

1. Runs the backend test suite and `npm run check` for the frontend.
2. Exports the committed revision with `git archive` to a temporary directory on
   the server, excluding ignored, untracked, and machine-local files.
3. Builds a candidate Docker image and automatically increments the deployed
   four-part version number.
4. Tags the existing image for rollback.
5. Recreates only the `immichframe` Compose service.
6. Checks the service from both the server and the local machine, including the
   `/settings` frontend route and the admin settings API.
7. Automatically restores the previous image if startup or verification fails,
   then verifies the restored image identity and internal/external readiness.

To set the version explicitly:

```bash
VERSION=1.0.25.2 ./scripts/deploy-prod.sh
```

The image is labelled with the source revision. For an emergency change, create
a temporary commit rather than bypassing the clean-tree check; that preserves an
auditable and reproducible rollback point.

The connection and deployment settings can be overridden when needed:

```bash
PROD_HOST=root@example \
PROD_URL=https://frame.example/ \
REMOTE_STACK_DIR=/opt/stacks/immich \
./scripts/deploy-prod.sh
```

## Smoke test

Run the production checks without deploying:

```bash
./scripts/smoke-prod.sh
```

The check verifies the frame shell, confirms that the compiled Svelte manifest
contains `/settings`, and validates that `GET /api/AdminSettings` returns a
writable configuration without warnings. If authentication is enabled, pass the
secret without placing it on the command line or in Git:

```bash
read -rsp 'Authentication secret: ' AUTHENTICATION_SECRET
export AUTHENTICATION_SECRET
./scripts/smoke-prod.sh
unset AUTHENTICATION_SECRET
```

The settings editor is available at <http://192.168.1.24:8080/settings>.
Because it can change Immich API keys and server configuration, production
should set `AuthenticationSecret`; otherwise every client that can reach the
service can read and modify its settings.

Each successful deployment prints its rollback tag. To restore one manually:

```bash
ssh root@192.168.1.24
docker tag immichframe:rollback-YYYYMMDD-HHMMSS immichframe:custom
cd /opt/stacks/immich
docker compose up -d --no-deps --force-recreate immichframe
curl -fsS -o /dev/null http://127.0.0.1:8080/
```

Old `immichframe:rollback-*` images are intentionally retained until manually
removed.
