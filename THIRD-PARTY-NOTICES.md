# Third-party notices

PlaceContext is open-source software licensed under the MIT License
(see [LICENSE](LICENSE)). Instance data and jobs remain the operator's property.
It builds on the following third-party software. Each component is the property of
its respective authors and is used under the license noted. Version numbers track
the manifests (`src/*/*.csproj`, `deploy/tui/go.mod`) — those files are authoritative.

## .NET (server, portal, MCP)

| Component | License |
|---|---|
| [.NET / ASP.NET Core / Entity Framework Core](https://github.com/dotnet) and `Microsoft.Extensions.*` | MIT |
| [ModelContextProtocol / ModelContextProtocol.AspNetCore](https://github.com/modelcontextprotocol/csharp-sdk) | MIT |
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://github.com/npgsql/efcore.pg) | PostgreSQL License |
| [KubernetesClient (kubernetes-client/csharp)](https://github.com/kubernetes-client/csharp) | Apache-2.0 |
| [AWSSDK.S3](https://github.com/aws/aws-sdk-net) (S3-compatible object-store client) | Apache-2.0 |
| [Markdig](https://github.com/xoofx/markdig) | BSD-2-Clause |
| [Cronos](https://github.com/HangfireIO/Cronos) | MIT |
| [Microsoft.AspNetCore.Authentication.JwtBearer](https://github.com/dotnet/aspnetcore) | MIT |

## Go (pctl TUI)

| Component | License |
|---|---|
| [Bubble Tea](https://github.com/charmbracelet/bubbletea), [Bubbles](https://github.com/charmbracelet/bubbles), [Lip Gloss](https://github.com/charmbracelet/lipgloss), [Glamour](https://github.com/charmbracelet/glamour) | MIT |
| Their transitive modules (chroma, go-colorful, mattn/*, muesli/*, …) | MIT / BSD-style — see `deploy/tui/go.sum` |

## Services orchestrated at deploy time (not linked, run as separate processes)

| Component | License |
|---|---|
| [PostgreSQL](https://www.postgresql.org/) | PostgreSQL License |
| [k3s](https://k3s.io/) / [k3d](https://k3d.io/) | Apache-2.0 |
| [MinIO](https://min.io/) (object store, run unmodified as a service) | AGPL-3.0 |
| [Docker Engine](https://www.docker.com/) / [kubectl](https://kubernetes.io/) | Apache-2.0 |
| [PdfPig](https://github.com/UglyToad/PdfPig) (PDF text extraction for entity tagging) | Apache-2.0 |
| [Tailscale](https://tailscale.com/) client (optional mesh networking) | BSD-3-Clause |

## Frontend assets

| Component | License |
|---|---|
| [Monaco Editor](https://github.com/microsoft/monaco-editor) (loaded from jsDelivr CDN) | MIT |
| [Chart.js](https://github.com/chartjs/Chart.js) (bundled at `wwwroot/vendor/`) | MIT |
| [Geist / Geist Mono](https://vercel.com/font) typefaces (Google Fonts) | SIL OFL 1.1 |

If you believe a required notice is missing, please open an issue.
