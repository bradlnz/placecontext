# Deploy on your infrastructure

Evaluate on one machine, then choose a production environment for your data, compute, and operational requirements.

## Local evaluation

The public installer uses k3d to run a single-host Kubernetes environment inside Docker. It installs the versioned PlaceContext image and starts the portal. This route is intended for evaluation and is not highly available.

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

[Local quickstart →](/docs/quickstart/)

## Production Kubernetes

The production installer targets an existing cluster. Prepare external PostgreSQL, S3-compatible artifact storage, TLS, persistent encryption/signing keys, and a workload pool with the required network controls.

Use the digest-pinned image from the verified release bundle. The production installer performs readiness checks rather than silently installing into an unprepared environment.

DigitalOcean Kubernetes, managed PostgreSQL, and Spaces are one documented deployment option. The application uses standard Kubernetes, PostgreSQL, and S3-compatible interfaces; infrastructure provisioning stays outside application code.

## Separate the workloads

Tenant jobs use isolated namespaces, resource quotas, workload service accounts, and per-run network policies. Those controls still share the underlying cluster and require validation against your workload risk and operating requirements.

Keep platform capacity separate from the workload pool. Set autoscaling limits that match your resource budget. QEMU browser execution uses a separate host process environment and needs its own capacity and security planning.

## Make recovery part of setup

Configure database backups, point-in-time recovery where available, artifact retention, persistent keys, monitoring, and restore tests. A backup is useful only when the deployment can restore and decrypt it.

## Configure capabilities explicitly

| Capability | Required setup |
| --- | --- |
| AI chat and agent steps | Enabled model connections and suitable models |
| Generated files and reports | Working artifact storage |
| Browser tasks | Prepared QEMU desktop image and model/verification providers |
| External sign-in | Stable public HTTPS origin and compatible identity configuration |
| Managed signup | Explicit launch configuration, email delivery, and entitlement setup |

The [deployment reference](https://github.com/bradlnz/placecontext/blob/main/deploy/release/README.md) contains the authoritative secret names, image requirements, Kubernetes checks, and production commands. Use it for installation rather than treating this overview as a complete manifest.
