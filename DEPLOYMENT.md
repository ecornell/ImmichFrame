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
4. Verifies that the Compose service, current container, and current image exist,
   then tags the running image for rollback.
5. Recreates only the `immichframe` Compose service.
6. Checks the service from both the server and the local machine, including the
   `/settings` frontend route, admin settings API, asset list, and an actual image download.
7. Automatically restores the previous image after errors, termination, or failed
   verification, then verifies the restored image identity and readiness.

To set the version explicitly:

```bash
VERSION=1.0.25.2 ./scripts/deploy-prod.sh
```

Normal update mode intentionally refuses to continue when the container is absent. After
confirming that `/opt/stacks/immich/compose.yaml`, its environment, configuration mounts,
and the `immichframe` service are the intended desired state, bootstrap an empty host with:

```bash
BOOTSTRAP=1 VERSION=1.0.25.2 ./scripts/deploy-prod.sh
```

Bootstrap is explicit, requires a version, and refuses to replace an existing container. If the
stable image tag already exists, bootstrap preserves it as the rollback image before replacing it;
otherwise a failed bootstrap retains its uniquely tagged candidate for diagnosis or a safe retry.

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
contains `/settings`, validates that `GET /api/AdminSettings` returns a writable
configuration without warnings, fetches `GET /api/Asset`, and downloads one image
through its media endpoint. This catches broken credentials and media proxying that
shell-only checks cannot detect. If authentication is enabled, pass the
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

## Opt-in health monitoring and durable recovery

The repository does not install or activate monitoring. On a host where an operator has
chosen to enable it, `scripts/check-compose-health.sh` verifies the Compose definition,
container presence/running state, and local HTTP reachability. Set `ALERT_WEBHOOK_URL` to
opt into a generic JSON webhook notification; leaving it unset only logs and exits nonzero.
Schedule the command with the host's existing monitoring system or a systemd timer, and
alert on any nonzero exit. Keep the Compose restart policy at `unless-stopped` so Docker
restarts ordinary stopped processes; monitoring is still required because a restart policy
cannot recreate a manually removed container.

A local rollback tag is not durable against disk loss or `docker system prune -a`. After a
verified deployment, create a checksummed archive and copy both files to operator-managed
off-host storage:

```bash
sudo bash ./scripts/archive-image.sh save immichframe:custom /var/backups/immichframe/custom-$(date +%Y%m%d).tar.gz
# copy the .tar.gz and .sha256 files off-host using your approved backup system
```

To recover an absent image, return both files to the host, verify/load them, restore the
stable tag if necessary, and reconcile the pinned Compose definition:

```bash
sudo bash ./scripts/archive-image.sh restore /var/backups/immichframe/custom-YYYYMMDD.tar.gz
docker tag <loaded-image-reference> immichframe:custom
cd /opt/stacks/immich && docker compose up -d immichframe
bash ./scripts/check-compose-health.sh
```

Do not run unrestricted image pruning until a verified off-host archive exists. The health
and archive scripts are guidance/tools only: they do not provision services, schedule jobs,
or transmit archives.
