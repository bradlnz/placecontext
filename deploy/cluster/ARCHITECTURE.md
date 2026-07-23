# PlaceContext Cluster Architecture

## System Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              USER / BROWSER                                │
│                         https://placecontext.dev                           │
└─────────────────────────────────┬───────────────────────────────────────────┘
                                  │ HTTPS
                                  ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                         K8s MASTER NODE                                    │
│                    (feasiblity-node-1: 100.81.205.22)                      │
│                                                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    PlaceContext Host (.NET)                         │   │
│  │                                                                     │   │
│  │  ┌──────────────┐  ┌──────────────────┐  ┌───────────────────┐    │   │
│  │  │ Chat.razor   │  │ ClusterProxy     │  │ ClusterPipeline   │    │   │
│  │  │ (Blazor UI)  │──│ Controller       │──│ (Service)         │    │   │
│  │  │              │  │ /api/cluster/*   │  │ chains shards     │    │   │
│  │  └──────────────┘  └──────────────────┘  └─────────┬─────────┘    │   │
│  │                                                     │              │   │
│  │  ┌──────────────────┐  ┌────────────────────────────┘              │   │
│  │  │ ClusterProxy     │  │  TCP (HTTP/JSON)                         │   │
│  │  │ Service          │  │  hidden states + logits                  │   │
│  │  │ (health checks)  │  ▼                                          │   │
│  │  └──────────────────┘                                             │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    Tailscale Network                                │   │
│  │                    100.x.x.x/32                                    │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└────────────┬──────────────────────────────────────────┬─────────────────────┘
             │                                          │
             │ TCP/8080                                 │ TCP/8080
             ▼                                          ▼
┌────────────────────────────┐          ┌────────────────────────────┐
│    SHARD 0: Mac Mini       │          │    SHARD 1: Mac Pro        │
│    (100.83.58.75:8080)     │          │    (100.x.x.x:8080)        │
│                            │          │                            │
│  ┌──────────────────────┐  │          │  ┌──────────────────────┐  │
│  │ mlx-lm (Metal)       │  │          │  │ mlx-lm (Metal)       │  │
│  │                      │  │          │  │                      │  │
│  │ Embedding + Layers   │  │          │  │ Layers 18-35 +       │  │
│  │ 0-17                 │  │          │  │ LM Head              │  │
│  │                      │  │          │  │                      │  │
│  │ POST /v1/forward     │  │          │  │ POST /v1/forward     │  │
│  │ POST /v1/decode      │  │          │  │ POST /v1/decode      │  │
│  │ GET  /health         │  │          │  │ GET  /health         │  │
│  └──────────────────────┘  │          │  └──────────────────────┘  │
│                            │          │                            │
│  SafeTensors: Qwen3.5-4B  │          │  SafeTensors: Qwen3.5-4B  │
│  (layers 0-17, ~4GB)      │          │  (layers 18-35, ~4GB)     │
└────────────────────────────┘          └────────────────────────────┘
```

## Pipeline Parallelism Flow

```
User sends: "Explain quantum computing"

    ┌──────────────┐
    │ Prompt Token │
    │ IDs: [1,2,3] │
    └──────┬───────┘
           │
           ▼
    ┌──────────────┐     ┌──────────────────────┐
    │   Shard 0    │────▶│ Hidden States        │
    │ Embed+Layers │     │ shape: (1, 3, 2560)  │
    │ 0-17         │     │                      │
    └──────────────┘     └──────────┬───────────┘
                                   │
                                   ▼
                        ┌──────────────────────┐
                        │   Shard 1            │
                        │ Layers 18-35 + LM    │
                        │ Head → Logits        │
                        │ shape: (1, 3, 151936)│
                        └──────────┬───────────┘
                                   │
                                   ▼
                        ┌──────────────────────┐
                        │ Sample(logits,       │
                        │   temp=0.7, top_p=0.9)│
                        │ → token_id: 42       │
                        └──────────┬───────────┘
                                   │
                    ┌──────────────┴──────────────┐
                    │                             │
                    ▼                             ▼
             ┌────────────┐              ┌────────────────┐
             │ Decode     │              │ Re-embed full  │
             │ token 42   │              │ sequence +     │
             │ → "Quantum"│              │ forward pass   │
             └────────────┘              └────────────────┘
                    │                             │
                    ▼                             ▼
             ┌────────────┐              ┌────────────────┐
             │ Yield to   │              │ Next iteration │
             │ SSE stream │              │ (token 43...)  │
             └────────────┘              └────────────────┘
```

## Deployment Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    DigitalOcean Spaces Bucket                   │
│                    (placecontext-deploy.nyc3)                   │
│                                                                 │
│  ┌─────────────┐  ┌──────────────┐  ┌──────────────────────┐  │
│  │ install.sh  │  │ setup-shard  │  │ server.py            │  │
│  │ (entry point│  │ .sh          │  │ (shard server)       │  │
│  └──────┬──────┘  └──────┬───────┘  └──────────┬───────────┘  │
└─────────┼────────────────┼──────────────────────┼──────────────┘
          │                │                      │
          ▼                ▼                      ▼
┌─────────────────────────────────────────────────────────────────┐
│                    NEW MAC NODE                                 │
│                                                                 │
│  curl -fsSL .../install.sh | bash -s -- --role shard \         │
│    --shard-index 1 --total-shards 2 --master-ip 100.83.58.75   │
│                                                                 │
│  1. Detect platform (macos-arm64)                               │
│  2. Install deps (mlx-lm, fastapi, uvicorn)                    │
│  3. Download server.py from bucket                              │
│  4. Create launchd service (auto-start on boot)                │
│  5. Register with master node                                   │
└─────────────────────────────────────────────────────────────────┘
```

## Job Chain Replay

```
┌─────────────────────────────────────────────────────────────────┐
│                    Job Chain Execution                          │
│                                                                 │
│  ┌─────┐    ┌─────┐    ┌─────┐    ┌─────┐    ┌─────┐         │
│  │Step1│───▶│Step2│───▶│Step3│───▶│Step4│───▶│Step5│         │
│  └──┬──┘    └─────┘    └──┬──┘    └─────┘    └─────┘         │
│     │                      │                                   │
│     │ FAIL                 │ FAIL                              │
│     ▼                      ▼                                   │
│  ┌─────────────────────────────────────────┐                  │
│  │ Replay: Resume from last successful     │                  │
│  │ step (Step2) with cached state          │                  │
│  │                                         │                  │
│  │ State saved at each step:               │                  │
│  │ - Input/Output snapshots                │                  │
│  │ - Token counts                          │                  │
│  │ - Execution metadata                    │                  │
│  └─────────────────────────────────────────┘                  │
└─────────────────────────────────────────────────────────────────┘
```

## Network Topology

```
                    ┌─────────────────────────────┐
                    │      Tailscale Network      │
                    │      100.x.x.x/32           │
                    │                             │
                    │  ┌───────────────────────┐  │
                    │  │ K8s Master            │  │
                    │  │ 100.81.205.22         │  │
                    │  │ (feasiblity-node-1)   │  │
                    │  └───────────┬───────────┘  │
                    │              │              │
                    │     ┌───────┴───────┐      │
                    │     │               │      │
                    │     ▼               ▼      │
                    │  ┌──────┐       ┌──────┐   │
                    │  │Mac   │       │Mac   │   │
                    │  │Mini  │       │Pro   │   │
                    │  │.75   │       │.x.x  │   │
                    │  └──────┘       └──────┘   │
                    │                             │
                    └─────────────────────────────┘

    All traffic is encrypted via Tailscale WireGuard tunnel.
    No public IP exposure required for cluster nodes.
```

## Data Flow: Chat Request

```
Browser                K8s Master              Shard 0            Shard 1
   │                      │                      │                  │
   │ POST /chat           │                      │                  │
   │ {messages:[...]}     │                      │                  │
   ├─────────────────────▶│                      │                  │
   │                      │                      │                  │
   │                      │ POST /v1/forward     │                  │
   │                      │ {prompt:"..."}       │                  │
   │                      ├─────────────────────▶│                  │
   │                      │                      │                  │
   │                      │ {hidden_states:...}  │                  │
   │                      │◀─────────────────────┤                  │
   │                      │                      │                  │
   │                      │ POST /v1/forward     │                  │
   │                      │ {hidden_states:...}  │                  │
   │                      ├──────────────────────┼─────────────────▶│
   │                      │                      │                  │
   │                      │ {logits:...}         │                  │
   │                      │◀─────────────────────┼──────────────────┤
   │                      │                      │                  │
   │                      │ [sample token]       │                  │
   │                      │                      │                  │
   │                      │ POST /v1/decode      │                  │
   │                      │ {token_id:42}        │                  │
   │                      ├─────────────────────▶│                  │
   │                      │ {text:"Quantum"}     │                  │
   │                      │◀─────────────────────┤                  │
   │                      │                      │                  │
   │ {choices:[...]}      │                      │                  │
   │◀─────────────────────┤                      │                  │
   │                      │                      │                  │
```
