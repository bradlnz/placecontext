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

- **Branding** changes the workspace name, logo, and colours.
- **Menu** controls navigation labels, order, and visibility.
- **Locality** sets the timezone used by schedules and displayed dates.
- **Backup** exports or imports workspace configuration. It can also download all job source
  files as a ZIP arranged by project and job.
- **Access** manages members, roles, and permission overrides.
- **Security** manages sign-in security.
- **API tokens** creates personal tokens for the entity data API.

Backup exports do not include run history or vault secrets. The job-code ZIP also excludes
environment values.
