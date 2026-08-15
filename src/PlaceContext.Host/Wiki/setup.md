# Setup and settings

*Install PlaceContext, check it, and configure the workspace.*

## Install

Install the client:

```bash
curl -fsSL https://get.placecontext.ai/install.sh | bash
```

Then run the guided installer:

```bash
placecontext
```

The guided installer will download the image from the remote registry when needed
(it will use `lib/placecontext-local.tar` first if present).

You can also choose a mode directly:

```bash
placecontext install --docker   # laptop or single-machine install
placecontext install --service  # server or fleet master
```

The portal is normally available at `http://localhost:7700`.

## Useful commands

```bash
placecontext status
placecontext logs -f
placecontext url
placecontext doctor
placecontext upgrade
```

## Workspace settings

- **Branding** changes the workspace name, logo, accent, and dark-mode colours.
- **Menu** controls navigation labels, order, and visibility.
- **Artifacts** controls the file categories shown on the Artifacts page.
- **Communications** connects email and SMS delivery for chain actions. Jobs and users see
  the generic Email or SMS channel rather than needing to choose a delivery provider.
- **MCP servers** connects extra tools that approved agents and jobs may use.
- **Locality** sets the timezone used by schedules and displayed dates.
- **Backup** exports or imports workspace configuration. It can also download all job source
  files as a ZIP arranged by project and job.
- **Access** manages members, roles, and permission overrides.
- **Security** manages sign-in security.
- **API tokens** creates personal tokens for the entity data and project search APIs.

Backup exports do not include run history or vault secrets. The job-code ZIP also excludes
environment values.

Most settings pages are available only to the default workspace administrator. **API tokens** is
self-service, so signed-in users can manage their own tokens.

## Your display theme

The light/dark switch is in the user area at the bottom of the main menu. It is a personal browser
preference, so changing it does not alter workspace branding or another person's screen. If you use
more than one browser or device, choose the mode separately on each one.
