#!/usr/bin/env bash
# Bring up the LOCAL hot-standby: an etcd member (#2 of the 3-member DCS) + a Patroni-managed
# PostgreSQL 16 (pgvector) hot-standby that streams ASYNChronously from the DO primary over the
# Tailscale mesh. Run this ON THE LOCAL k3s node (it must already be joined to the mesh — `pctl join`
# / `pctl server up` do that). Patroni + etcd run as a small docker-compose unit on the host so they
# bind the node's tailnet IP directly and talk to the other members by MagicDNS name — exactly like
# the DO primary's cloud-init. `pctl db-ha-join` is a thin wrapper around this script.
#
# Auto-failover: because all three etcd members share one scope, if the DO site is unreachable this
# node + the witness keep a 2-of-3 quorum and Patroni AUTO-PROMOTES this standby to read-write. The
# app (Npgsql Target Session Attributes=primary) then follows the new leader with no config change.
#
# Required env (use the credentials configured on the externally provisioned primary):
#   REPLICATION_PASSWORD   replication user's password
#   SUPERUSER_PASSWORD     PostgreSQL superuser password
# Optional env (override when the remote deployment uses different values):
#   PATRONI_SCOPE=placecontext
#   SUPERUSER_USERNAME=postgres
#   REPLICATION_USERNAME=replicator
#   MESH_HOSTNAME_PRIMARY=placecontext-db-primary
#   MESH_HOSTNAME_WITNESS=placecontext-db-witness
#   MESH_HOSTNAME_LOCAL=placecontext-db-local     (this node's MagicDNS name)
#   PATRONI_IMAGE=ghcr.io/bradlnz/placecontext-patroni:16-pgvector
#   ETCD_IMAGE=quay.io/coreos/etcd:v3.5.16
set -euo pipefail

DEST=/opt/placecontext-db-local
PATRONI_SCOPE="${PATRONI_SCOPE:-placecontext}"
SUPERUSER_USERNAME="${SUPERUSER_USERNAME:-postgres}"
REPLICATION_USERNAME="${REPLICATION_USERNAME:-replicator}"
MESH_HOSTNAME_PRIMARY="${MESH_HOSTNAME_PRIMARY:-placecontext-db-primary}"
MESH_HOSTNAME_WITNESS="${MESH_HOSTNAME_WITNESS:-placecontext-db-witness}"
MESH_HOSTNAME_LOCAL="${MESH_HOSTNAME_LOCAL:-placecontext-db-local}"
PATRONI_IMAGE="${PATRONI_IMAGE:-ghcr.io/bradlnz/placecontext-patroni:16-pgvector}"
ETCD_IMAGE="${ETCD_IMAGE:-quay.io/coreos/etcd:v3.5.16}"

: "${REPLICATION_PASSWORD:?set REPLICATION_PASSWORD to the primary's replication password}"
: "${SUPERUSER_PASSWORD:?set SUPERUSER_PASSWORD to the primary's PostgreSQL superuser password}"

command -v docker >/dev/null 2>&1 || { echo "docker is required"; exit 1; }
command -v tailscale >/dev/null 2>&1 || { echo "tailscale is required (join the mesh first: pctl join)"; exit 1; }

# This node MUST be registered under MESH_HOSTNAME_LOCAL so the etcd peer URLs resolve. Re-tag if not.
CURRENT_NAME="$(tailscale status --json 2>/dev/null | { command -v jq >/dev/null && jq -r '.Self.HostName' || cat; } 2>/dev/null || true)"
if [ -n "$CURRENT_NAME" ] && [ "$CURRENT_NAME" != "$MESH_HOSTNAME_LOCAL" ]; then
  echo "NOTE: this node is meshed as '$CURRENT_NAME' but the DCS expects '$MESH_HOSTNAME_LOCAL'."
  echo "      Re-register it:  sudo tailscale up --hostname $MESH_HOSTNAME_LOCAL  (keeps the same tailnet IP)"
fi

TS_IP="$(tailscale ip -4 2>/dev/null | head -1 || true)"
[ -n "$TS_IP" ] || { echo "no tailnet IP — is tailscaled up and joined?"; exit 1; }
echo "[replica] tailnet IP: $TS_IP  (name: ${MESH_HOSTNAME_LOCAL})"

ETCD_INITIAL_CLUSTER="${MESH_HOSTNAME_PRIMARY}=http://${MESH_HOSTNAME_PRIMARY}:2380,${MESH_HOSTNAME_LOCAL}=http://${MESH_HOSTNAME_LOCAL}:2380,${MESH_HOSTNAME_WITNESS}=http://${MESH_HOSTNAME_WITNESS}:2380"

mkdir -p "$DEST/patroni"

cat > "$DEST/.env" <<EOF
TS_IP=$TS_IP
PATRONI_IMAGE=$PATRONI_IMAGE
ETCD_IMAGE=$ETCD_IMAGE
ETCD_NAME=$MESH_HOSTNAME_LOCAL
ETCD_INITIAL_CLUSTER=$ETCD_INITIAL_CLUSTER
MESH_HOSTNAME=$MESH_HOSTNAME_LOCAL
EOF

# Patroni config for the standby. Same scope/DCS as the primary; Patroni sees the cluster is already
# initialized in etcd and clones this node from the current leader (pg_basebackup), then streams.
# No initdb/post_bootstrap here — this node never bootstraps the cluster.
cat > "$DEST/patroni/patroni.yml" <<EOF
scope: ${PATRONI_SCOPE}
namespace: /service/
name: ${MESH_HOSTNAME_LOCAL}

restapi:
  listen: ${TS_IP}:8008
  connect_address: ${MESH_HOSTNAME_LOCAL}:8008

etcd3:
  host: 127.0.0.1:2379
  protocol: http

postgresql:
  listen: ${TS_IP}:5432
  connect_address: ${MESH_HOSTNAME_LOCAL}:5432
  data_dir: /home/postgres/pgdata/data
  bin_dir: /usr/lib/postgresql/16/bin
  pgpass: /tmp/pgpass
  authentication:
    superuser:
      username: ${SUPERUSER_USERNAME}
      password: "${SUPERUSER_PASSWORD}"
    replication:
      username: ${REPLICATION_USERNAME}
      password: "${REPLICATION_PASSWORD}"
  parameters:
    password_encryption: scram-sha-256

tags:
  nofailover: false
  noloadbalance: false
  clonefrom: false
  nosync: false
EOF

cat > "$DEST/docker-compose.yml" <<'EOF'
services:
  etcd:
    image: ${ETCD_IMAGE}
    restart: unless-stopped
    network_mode: host
    command:
      - /usr/local/bin/etcd
      - --name=${ETCD_NAME}
      - --data-dir=/etcd-data
      - --listen-peer-urls=http://${TS_IP}:2380
      - --listen-client-urls=http://${TS_IP}:2379,http://127.0.0.1:2379
      - --advertise-client-urls=http://${MESH_HOSTNAME}:2379
      - --initial-advertise-peer-urls=http://${MESH_HOSTNAME}:2380
      - --initial-cluster=${ETCD_INITIAL_CLUSTER}
      - --initial-cluster-state=new
      - --initial-cluster-token=placecontext-dcs
    volumes:
      - etcd-data:/etcd-data

  patroni:
    image: ${PATRONI_IMAGE}
    restart: unless-stopped
    depends_on: [etcd]
    network_mode: host
    volumes:
      - ./patroni/patroni.yml:/etc/patroni/patroni.yml:ro
      - pgdata:/home/postgres/pgdata
    command: ["patroni", "/etc/patroni/patroni.yml"]

volumes:
  etcd-data:
  pgdata:
EOF

echo "[replica] starting local etcd member + Patroni standby…"
cd "$DEST"
docker compose --env-file "$DEST/.env" pull || true
docker compose --env-file "$DEST/.env" up -d

cat <<EOF

[replica] up. Verify (once the primary + witness etcd are also up so the DCS has quorum):
  docker compose -f $DEST/docker-compose.yml exec -T patroni \\
    patronictl -c /etc/patroni/patroni.yml list
The local node should appear as a Replica in State: streaming. If it shows 'creating replica', it is
running the initial pg_basebackup from the primary over the tailnet — wait for it to finish.
EOF
