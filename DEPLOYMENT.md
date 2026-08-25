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

The script deploys the current working tree, including uncommitted changes. It:

1. Runs `npm run check` for the frontend.
2. Copies a clean build context to a temporary directory on the server.
3. Builds a candidate Docker image and automatically increments the deployed
   four-part version number.
4. Tags the existing image for rollback.
5. Recreates only the `immichframe` Compose service.
6. Checks the service from both the server and the local machine.
7. Automatically restores the previous image if startup or verification fails.

To set the version explicitly:

```bash
VERSION=1.0.25.2 ./scripts/deploy-prod.sh
```

The connection and deployment settings can be overridden when needed:

```bash
PROD_HOST=root@example \
PROD_URL=https://frame.example/ \
REMOTE_STACK_DIR=/opt/stacks/immich \
./scripts/deploy-prod.sh
```

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
