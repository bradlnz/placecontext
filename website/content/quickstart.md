# Get started locally

Set up PlaceContext, open your first project, and connect a model when you are ready to use the assistant.

## Prerequisites

Use Linux or macOS with `curl` and Docker access. The installer uses Docker and k3d to create a local Kubernetes environment. Installing prerequisites may require administrator permissions.

This is a single-host evaluation installation. Production deployment has a separate path.

## Install PlaceContext

Review the [installer and release instructions](https://github.com/bradlnz/placecontext/blob/main/deploy/release/README.md), then run:

```bash
curl -fsSL https://get.placecontext.io/install.sh | bash
```

The installer verifies a release bundle, creates the local environment, and starts the portal. It does not bundle a model inference service or download a browser desktop image.

## Open the portal

Open [localhost:7700](http://localhost:7700) on the installation host. Complete the initial setup and create a project for the work you want to manage.

Projects group data, jobs, workflows, and their results. Start with a small set of sample records so you can inspect each step.

## Connect a model

As the default administrator, open **Settings → Models → Add connection**. Choose a preset, add the API base URL and credential, and discover or enter model IDs. Enable the connection and choose a model that supports tool calling.

Local endpoint addresses resolve from the application host or container, rather than from your browser. [Model setup →](/docs/models/)

## Run something useful

1. Import a small CSV into a project table, or connect a data source.
2. Open **Workflows** and add a **Query data** step.
3. Preview the query and choose an output field.
4. Add a human checkpoint and explicit conditions for the two outcomes.
5. Run the workflow, answer the checkpoint, and inspect the recorded output.

The [first workflow guide](/docs/first-workflow/) walks through this pattern. You can add an automatic agent or a code job once its model or execution environment is configured.

## Add capabilities as you need them

- [Connected data](/docs/data/) supplies the records your workflows use.
- [Agents and chat](/docs/agents/) provides a conversational way to work with a project.
- [Jobs and artifacts](/docs/jobs/) adds code execution and stored outputs.
- [Browser automation](/docs/browser/) uses a prepared QEMU desktop and configured models.
- [MCP integrations](/docs/mcp/) connects external AI clients and project tools.
