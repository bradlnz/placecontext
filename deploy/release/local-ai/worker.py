"""
Cluster shard server — pipeline-parallel SafeTensors inference.

Apple Silicon (MPS/Metal): uses mlx-lm for Metal-native inference.
Linux (CPU/CUDA): uses transformers + torch.

Each shard holds a layer slice of the model. The proxy chains hidden
states through shards over TCP to produce the final output.

Single-node mode (--shard omitted): full model, /v1/chat endpoint.
Pipeline mode (--shard 0/2, --shard 1/2): layer slice, /v1/forward endpoint.

Layer mapping for Qwen3.5-4B (36 layers, 0-indexed):
  Shard 0/2: layers 0-17  + embedding  (first half)
  Shard 1/2: layers 18-35 + LM head    (second half)
"""

import argparse
import hmac
import json
import logging
import math
import os
import platform
import sys
import time
from contextlib import asynccontextmanager
from typing import Optional, List, Tuple

import numpy as np
import uvicorn
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse, StreamingResponse
from pydantic import BaseModel, Field

logger = logging.getLogger("shard-server")

# ---------------------------------------------------------------------------
# Globals — initialized in lifespan
# ---------------------------------------------------------------------------
model = None
tokenizer = None
shard_config = None
backend = None  # "mlx" or "torch"
total_layers = 0  # total transformer layers in the model

AUTH_HEADER = "X-PlaceContext-AI-Token"
MAX_GENERATED_TOKENS = 4096
MAX_MESSAGES = 64
MAX_MESSAGE_CHARS = 32_768
MAX_PROMPT_CHARS = 131_072
MAX_SEQUENCE_TOKENS = 8192
MAX_EMBEDDING_INPUTS = 32
MAX_EMBEDDING_CHARS = 4000


def is_apple_silicon():
    return platform.system() == "Darwin" and platform.machine() == "arm64"


class ShardConfig:
    def __init__(self, total_shards: int, shard_index: int, num_model_layers: int):
        self.total_shards = total_shards
        self.shard_index = shard_index
        self.is_first = shard_index == 0
        self.is_last = shard_index == total_shards - 1
        self.num_layers = num_model_layers

        if total_shards == 1:
            self.layer_start = 0
            self.layer_end = num_model_layers  # exclusive
        else:
            layers_per = math.ceil(num_model_layers / total_shards)
            self.layer_start = shard_index * layers_per
            self.layer_end = min(self.layer_start + layers_per, num_model_layers)

        self.layer_count = self.layer_end - self.layer_start
        logger.info(
            "Shard %d/%d: layers %d-%d (%d layers) | first=%s last=%s",
            shard_index, total_shards,
            self.layer_start, self.layer_end - 1, self.layer_count,
            self.is_first, self.is_last,
        )


# ---------------------------------------------------------------------------
# MLX backend (Apple Silicon) — pipeline forward pass
# ---------------------------------------------------------------------------

def load_model_mlx(model_path: str):
    """Load model via mlx-lm — Metal-native on Apple Silicon."""
    from mlx_lm import load
    logger.info("Loading model %s via mlx-lm ...", model_path)
    t0 = time.time()
    mdl, tok = load(model_path)
    logger.info("Model loaded in %.1fs", time.time() - t0)
    return mdl, tok


def mlx_forward_slice(hidden_states, attention_mask, layer_start, layer_end):
    """Run a slice of transformer layers on hidden states using MLX."""
    from mlx_lm.models.base import create_attention_mask, create_ssm_mask

    layers = _mlx_inner_model().layers[layer_start:layer_end]
    attention_mask = create_attention_mask(hidden_states)
    recurrent_mask = create_ssm_mask(hidden_states)

    hs = hidden_states
    for layer in layers:
        # Qwen 3.5 alternates recurrent Gated DeltaNet and full-attention
        # layers. Calling the model's layer implementation preserves those
        # architecture-specific details; manually invoking self_attn does not.
        mask = recurrent_mask if getattr(layer, "is_linear", False) else attention_mask
        hs = layer(hs, mask=mask)

    return hs


def mlx_embed(token_ids):
    """Convert token IDs to embeddings."""
    import mlx.core as mx
    ids = mx.array([token_ids]) if isinstance(token_ids, list) else mx.expand_dims(mx.array(token_ids), 0)
    return _mlx_inner_model().embed_tokens(ids)


def mlx_lm_head(hidden_states):
    """Apply final layer norm + LM head to get logits."""
    inner = _mlx_inner_model()
    h = inner.norm(hidden_states)
    if hasattr(model, "lm_head"):
        return model.lm_head(h)
    language_model = getattr(model, "language_model", None)
    if language_model is not None and hasattr(language_model, "lm_head"):
        return language_model.lm_head(h)
    if hasattr(inner.embed_tokens, "as_linear"):
        return inner.embed_tokens.as_linear(h)
    raise RuntimeError("Cannot locate the MLX language-model head")


def mlx_sample(logits, temperature=0.7, top_p=0.9):
    """Sample next token from logits."""
    import mlx.core as mx

    if temperature <= 0:
        return int(mx.argmax(logits[:, -1, :], axis=-1).item())

    # Temperature scaling
    logits = logits[:, -1, :] / temperature

    # Top-p filtering
    if top_p < 1.0:
        sorted_indices = mx.argsort(logits, axis=-1, descending=True)
        sorted_logits = mx.take_along_axis(logits, sorted_indices, axis=-1)
        cumulative_probs = mx.cumsum(mx.softmax(sorted_logits, axis=-1), axis=-1)
        # Remove tokens with cumulative prob above top_p
        mask = cumulative_probs - mx.softmax(sorted_logits, axis=-1) >= top_p
        sorted_logits = mx.where(mask, -float('inf'), sorted_logits)
        # Scatter back
        logits = mx.zeros_like(logits).at[sorted_indices].add(sorted_logits)

    probs = mx.softmax(logits, axis=-1)
    # Sample from distribution (use numpy for multinomial since mlx doesn't have it)
    probs_np = np.array(probs)
    probs_np = probs_np / probs_np.sum()
    token = np.random.choice(probs_np.shape[-1], p=probs_np)
    return int(token)


# ---------------------------------------------------------------------------
# Torch backend (Linux / fallback)
# ---------------------------------------------------------------------------

def load_model_torch(model_path: str):
    """Load model via transformers + torch."""
    import torch
    from transformers import AutoModelForCausalLM, AutoTokenizer

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    dtype = torch.float16 if device.type != "cpu" else torch.float32

    logger.info("Loading model %s on %s (dtype=%s) via transformers ...", model_path, device, dtype)
    t0 = time.time()

    tok = AutoTokenizer.from_pretrained(model_path, trust_remote_code=False)
    mdl = AutoModelForCausalLM.from_pretrained(
        model_path,
        torch_dtype=dtype,
        device_map="auto" if device.type == "cuda" else None,
        trust_remote_code=False,
    )
    if device.type == "cpu":
        mdl = mdl.to(device)
    mdl.eval()

    logger.info("Model loaded in %.1fs", time.time() - t0)
    return mdl, tok, device


def _torch_inner_model():
    """Resolve the text transformer body across Transformers model layouts."""
    candidates = []
    outer = getattr(model, "model", None)
    if outer is not None:
        language_model = getattr(outer, "language_model", None)
        if language_model is not None:
            candidates.append(language_model)
        candidates.append(outer)
    language_model = getattr(model, "language_model", None)
    if language_model is not None:
        candidates.append(language_model)
    candidates.append(model)
    for candidate in candidates:
        if all(hasattr(candidate, attribute) for attribute in ("embed_tokens", "layers", "norm")):
            return candidate
    raise RuntimeError("Cannot locate the Torch transformer body")


def torch_forward_slice(hidden_states, attention_mask, layer_start, layer_end):
    """Run a slice of transformer layers on hidden states using torch."""
    import torch

    body = _torch_inner_model()
    layers = body.layers[layer_start:layer_end]
    device = hidden_states.device
    seq_len = hidden_states.shape[1]
    batch_size = hidden_states.shape[0]
    positions = torch.arange(seq_len, device=device).unsqueeze(0).expand(batch_size, -1)

    if hasattr(body, "rotary_emb"):
        from transformers.masking_utils import create_causal_mask

        model_type = getattr(body.config, "model_type", "")
        if model_type in ("qwen3_5", "qwen3_5_text") or any(
            hasattr(layer, "linear_attn") for layer in layers
        ):
            from transformers.masking_utils import create_recurrent_attention_mask

            text_positions = positions
            rotary_positions = positions.unsqueeze(0).expand(3, -1, -1)
            mask_args = {
                "config": body.config,
                "inputs_embeds": hidden_states,
                "attention_mask": attention_mask,
                "past_key_values": None,
                "position_ids": text_positions,
            }
            masks = {
                "full_attention": create_causal_mask(**mask_args),
                "linear_attention": create_recurrent_attention_mask(**mask_args),
            }
            position_embeddings = body.rotary_emb(hidden_states, rotary_positions)
            hs = hidden_states
            for index, layer in enumerate(layers, start=layer_start):
                hs = layer(
                    hs,
                    position_embeddings=position_embeddings,
                    attention_mask=masks[body.config.layer_types[index]],
                    position_ids=text_positions,
                    past_key_values=None,
                    use_cache=False,
                )
            return hs

        causal_mask = create_causal_mask(
            config=body.config,
            inputs_embeds=hidden_states,
            attention_mask=attention_mask,
            past_key_values=None,
            position_ids=positions,
        )
        position_embeddings = body.rotary_emb(hidden_states, positions)
        hs = hidden_states
        for layer in layers:
            hs = layer(
                hs,
                position_embeddings=position_embeddings,
                attention_mask=causal_mask,
                position_ids=positions,
                past_key_values=None,
                use_cache=False,
            )
        return hs

    # Compatibility fallback for older decoder implementations.
    mask = torch.triu(torch.full((seq_len, seq_len), float('-inf'), device=device), diagonal=1)
    hs = hidden_states
    for layer in layers:
        residual = hs
        normalized = layer.input_layernorm(residual) if hasattr(layer, 'input_layernorm') else residual
        attention = layer.self_attn(normalized, attention_mask=mask)
        if isinstance(attention, tuple):
            attention = attention[0]
        hs = residual + attention
        normalized = layer.post_attention_layernorm(hs) if hasattr(layer, 'post_attention_layernorm') else hs
        hs = hs + layer.mlp(normalized)
    return hs


def render_chat_prompt(messages):
    """Render the chat template, disabling Qwen3 'thinking' blocks when supported.

    Thinking mode roughly doubles time-to-first-token for interactive chat; the
    portal prefers fast, direct answers. Templates without the kwarg (non-Qwen3
    models) fall back to the default rendering.
    """
    msgs = [{"role": m["role"], "content": m["content"]} for m in messages]
    try:
        return tokenizer.apply_chat_template(
            msgs, tokenize=False, add_generation_prompt=True, enable_thinking=False,
        )
    except TypeError:
        return tokenizer.apply_chat_template(
            msgs, tokenize=False, add_generation_prompt=True,
        )


def _mlx_inner_model():
    """Resolve the submodule that maps token ids → final hidden states
    (embed_tokens + layers + norm), across mlx-lm model layouts
    (e.g. <model>.language_model.model for qwen3_5, <model>.model for qwen3)."""
    candidates = []
    lm = getattr(model, "language_model", None)
    if lm is not None and hasattr(lm, "model"):
        candidates.append(lm.model)
    if hasattr(model, "model"):
        candidates.append(model.model)
    candidates.append(model)
    for c in candidates:
        if all(hasattr(c, a) for a in ("embed_tokens", "layers", "norm")):
            return c
    raise RuntimeError("Cannot locate transformer body (embed_tokens/layers/norm) on this model")


def embed_texts_mlx(texts):
    """Embed texts via the full model: one forward pass → final hidden states →
    mean-pool over the sequence → L2-normalize. Single-node mode only."""
    import mlx.core as mx

    inner = _mlx_inner_model()
    vectors = []
    for text in texts:
        ids = mx.array([tokenizer.encode(text, add_special_tokens=True)[:2048]])
        hs = inner(ids)  # (1, seq, hidden) — final hidden states, norm included
        vec = mx.mean(hs, axis=1)[0]
        vec = vec / mx.sqrt(mx.sum(vec * vec))
        vec = vec.astype(mx.float32)  # model runs bfloat16 — numpy can't read it directly
        vectors.append([float(x) for x in np.array(vec)])
    return vectors


def embed_texts_torch(texts):
    """Torch equivalent of embed_texts_mlx (attention-mask-weighted mean pool)."""
    import torch

    vectors = []
    device = next(model.parameters()).device
    body = _torch_inner_model()
    for text in texts:
        inputs = tokenizer(text, return_tensors="pt", truncation=True, max_length=2048).to(device)
        with torch.no_grad():
            out = body(**inputs)
            hs = out.last_hidden_state
            mask = inputs["attention_mask"].unsqueeze(-1).float()
            vec = (hs * mask).sum(dim=1) / mask.sum(dim=1).clamp(min=1e-9)
            vec = torch.nn.functional.normalize(vec, p=2, dim=-1)
        vectors.append(vec[0].cpu().float().tolist())
    return vectors


def chat_mlx(messages, temperature=0.7, top_p=0.9, max_tokens=2048):
    """Generate a chat completion via mlx-lm (full model, single-node)."""
    from mlx_lm import generate
    from mlx_lm.sample_utils import make_sampler

    prompt = render_chat_prompt(messages)
    sampler = make_sampler(temp=temperature, top_p=top_p)
    t0 = time.time()
    response = generate(model, tokenizer, prompt=prompt, max_tokens=max_tokens, sampler=sampler)
    elapsed = time.time() - t0
    return response, elapsed


def chat_stream_mlx(messages, temperature=0.7, top_p=0.9, max_tokens=2048):
    """Yield tokens via mlx-lm streaming (full model, single-node)."""
    from mlx_lm import stream_generate
    from mlx_lm.sample_utils import make_sampler

    prompt = render_chat_prompt(messages)
    
    logger.info(f"chat_stream_mlx: {len(messages)} messages, prompt length: {len(prompt)} chars")
    logger.debug(f"Prompt preview: {prompt[:500]}...")
    
    sampler = make_sampler(temp=temperature, top_p=top_p)
    token_count = 0
    for response in stream_generate(model, tokenizer, prompt=prompt, max_tokens=max_tokens, sampler=sampler):
        if response.text:  # only yield non-empty chunks
            token_count += 1
            yield response.text
    
    logger.info(f"chat_stream_mlx completed: {token_count} tokens generated")


def chat_torch(messages, temperature=0.7, top_p=0.9, max_tokens=2048):
    """Generate via transformers (full model, single-node)."""
    import torch

    prompt = render_chat_prompt(messages)
    inputs = tokenizer(prompt, return_tensors="pt").to(model.device)
    t0 = time.time()
    with torch.no_grad():
        outputs = model.generate(
            **inputs, max_new_tokens=max_tokens,
            temperature=temperature, top_p=top_p, do_sample=True,
        )
    elapsed = time.time() - t0
    new_tokens = outputs[0][inputs["input_ids"].shape[-1]:]
    response_text = tokenizer.decode(new_tokens, skip_special_tokens=True)
    return response_text, elapsed, inputs["input_ids"].shape[-1], len(new_tokens)


def chat_stream_torch(messages, temperature=0.7, top_p=0.9, max_tokens=2048):
    """Yield tokens via transformers with KV-cache (full model, single-node)."""
    import torch

    prompt = render_chat_prompt(messages)
    
    logger.info(f"chat_stream_torch: {len(messages)} messages, prompt length: {len(prompt)} chars")
    
    inputs = tokenizer(prompt, return_tensors="pt").to(model.device)
    past = None
    input_ids = inputs["input_ids"]
    token_count = 0

    with torch.no_grad():
        for _ in range(max_tokens):
            model_inputs = {"input_ids": input_ids[:, -1:]} if past is not None else {"input_ids": input_ids}
            out = model(**model_inputs, past_key_values=past, use_cache=True)
            past = out.past_key_values
            logits = out.logits[:, -1, :]
            if temperature and temperature > 0:
                probs = torch.softmax(logits / temperature, dim=-1)
                next_token = torch.multinomial(probs, num_samples=1)
            else:
                next_token = torch.argmax(logits, dim=-1, keepdim=True)
            token_id = next_token.item()
            if token_id == tokenizer.eos_token_id:
                break
            text = tokenizer.decode(token_id, skip_special_tokens=True)
            token_count += 1
            yield text
            input_ids = torch.cat([input_ids, next_token], dim=-1)
    
    logger.info(f"chat_stream_torch completed: {token_count} tokens generated")


# ---------------------------------------------------------------------------
# FastAPI
# ---------------------------------------------------------------------------

class ChatMessage(BaseModel):
    role: str = Field(min_length=1, max_length=16)
    content: str = Field(min_length=1, max_length=MAX_MESSAGE_CHARS)


class ChatRequest(BaseModel):
    model: str = "qwen3.5-4b"
    messages: List[ChatMessage] = Field(min_length=1, max_length=MAX_MESSAGES)
    stream: bool = False
    temperature: Optional[float] = Field(default=0.7, ge=0, le=2)
    top_p: Optional[float] = Field(default=0.9, gt=0, le=1)
    max_tokens: Optional[int] = Field(default=2048, ge=1, le=MAX_GENERATED_TOKENS)


class TokenizeRequest(BaseModel):
    messages: List[ChatMessage] = Field(min_length=1, max_length=MAX_MESSAGES)


class ForwardRequest(BaseModel):
    """Pipeline mode: hidden states in, updated states out.

    First shard: provide prompt (raw text, shard tokenizes) OR token_ids.
    Subsequent shards: provide hidden_states.
    """
    prompt: Optional[str] = Field(default=None, max_length=MAX_PROMPT_CHARS)  # first shard: raw text
    token_ids: Optional[List[int]] = Field(default=None, max_length=MAX_SEQUENCE_TOKENS)
    hidden_states: Optional[List[List[List[float]]]] = None  # subsequent shards: (batch, seq, hidden)
    attention_mask: Optional[List[int]] = None
    temperature: Optional[float] = Field(default=0.7, ge=0, le=2)
    top_p: Optional[float] = Field(default=0.9, gt=0, le=1)
    max_tokens: Optional[int] = Field(default=2048, ge=1, le=MAX_GENERATED_TOKENS)
    generate: bool = False  # if True, sample tokens and return logits + generated token


@asynccontextmanager
async def lifespan(app: FastAPI):
    global model, tokenizer, shard_config, backend, total_layers

    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default=os.environ.get("MODEL_PATH", "Qwen/Qwen3.5-4B"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("PORT", "8080")))
    parser.add_argument("--host", default=os.environ.get("PLACECONTEXT_AI_BIND", "127.0.0.1"))
    parser.add_argument("--shard", default=os.environ.get("SHARD_SPEC"), help="Shard spec: index/total, e.g. 0/2")
    args = parser.parse_args()

    # Load full model first, then determine layer count
    if is_apple_silicon():
        backend = "mlx"
        model, tokenizer = load_model_mlx(args.model)
        total_layers = len(_mlx_inner_model().layers)
    else:
        backend = "torch"
        model, tokenizer, device = load_model_torch(args.model)
        total_layers = len(_torch_inner_model().layers)

    logger.info("Model has %d transformer layers", total_layers)

    if args.shard:
        idx, total = (int(x) for x in args.shard.split("/"))
        shard_config = ShardConfig(total, idx, total_layers)
    else:
        shard_config = ShardConfig(1, 0, total_layers)

    logger.info("Backend: %s | Model: %s | Layers: %d", backend, args.model, total_layers)
    yield

    del model, tokenizer
    model = tokenizer = None


app = FastAPI(title="Cluster Shard Server", lifespan=lifespan)


@app.middleware("http")
async def authenticate_compute_requests(request: Request, call_next):
    """Keep probes public, but fail closed for every model or tokenizer operation."""
    if request.url.path == "/health":
        return await call_next(request)

    configured = os.environ.get("PLACECONTEXT_AI_TOKEN", "")
    supplied = request.headers.get(AUTH_HEADER, "")
    if not configured:
        return JSONResponse(
            status_code=503,
            content={"error": "AI worker authentication is not configured."},
        )
    if not hmac.compare_digest(supplied, configured):
        return JSONResponse(status_code=401, content={"error": "Unauthorized."})
    return await call_next(request)


def validated_messages(messages):
    if any(message.role not in ("system", "user", "assistant") for message in messages):
        raise HTTPException(400, "Unsupported message role")
    if sum(len(message.content) for message in messages) > MAX_PROMPT_CHARS:
        raise HTTPException(400, f"Combined message content exceeds {MAX_PROMPT_CHARS} characters")


@app.get("/health")
async def health():
    return {
        "status": "ok",
        "backend": backend,
        "shard": f"{shard_config.shard_index}/{shard_config.total_shards}",
        "layers": f"{shard_config.layer_start}-{shard_config.layer_end - 1}",
        "layer_count": shard_config.layer_count,
        "total_layers": total_layers,
        "is_first": shard_config.is_first,
        "is_last": shard_config.is_last,
    }


@app.post("/v1/chat")
async def chat(req: ChatRequest):
    """OpenAI-compatible chat endpoint (single-node mode only)."""
    if model is None or tokenizer is None:
        raise HTTPException(503, "Model not loaded")
    if shard_config.total_shards > 1:
        raise HTTPException(400, "Use /v1/forward in pipeline mode")

    validated_messages(req.messages)
    messages = [{"role": m.role, "content": m.content} for m in req.messages]
    temp = req.temperature or 0.7
    top = req.top_p or 0.9
    tokens = req.max_tokens or 2048

    if backend == "mlx":
        response_text, elapsed = chat_mlx(messages, temp, top, tokens)
        usage = {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0}
    else:
        response_text, elapsed, prompt_t, comp_t = chat_torch(messages, temp, top, tokens)
        usage = {"prompt_tokens": prompt_t, "completion_tokens": comp_t, "total_tokens": prompt_t + comp_t}

    return {
        "id": f"chatcmpl-{int(time.time()*1000)}",
        "object": "chat.completion",
        "created": int(time.time()),
        "model": req.model,
        "choices": [{
            "index": 0,
            "message": {"role": "assistant", "content": response_text},
            "finish_reason": "stop",
        }],
        "usage": usage,
        "elapsed_seconds": round(elapsed, 2),
    }


class EmbeddingRequest(BaseModel):
    model: str = "qwen3.5-4b"
    input: List[str] = Field(min_length=1, max_length=MAX_EMBEDDING_INPUTS)


@app.post("/v1/embeddings")
async def embeddings(req: EmbeddingRequest):
    """OpenAI-compatible embeddings endpoint (single-node mode only).

    Vectors come from the chat model itself (mean-pooled final hidden states,
    L2-normalized) — a self-hosted semantic signal for RAG/graph linking without
    a separate embedding model. Also returns the vector size so callers can
    validate against their configured dimensions.
    """
    if model is None or tokenizer is None:
        raise HTTPException(503, "Model not loaded")
    if shard_config.total_shards > 1:
        raise HTTPException(400, "Embeddings are only supported in single-node mode")
    if any(not text or len(text) > MAX_EMBEDDING_CHARS for text in req.input):
        raise HTTPException(
            400,
            f"input strings must be non-empty and at most {MAX_EMBEDDING_CHARS} characters",
        )

    # Cap batch + per-text size defensively (the chat context window is shared).
    texts = req.input
    vectors = embed_texts_mlx(texts) if backend == "mlx" else embed_texts_torch(texts)

    return {
        "object": "list",
        "data": [{"object": "embedding", "index": i, "embedding": v} for i, v in enumerate(vectors)],
        "model": req.model,
        "dimensions": len(vectors[0]) if vectors else 0,
    }


@app.post("/v1/chat/stream")
async def chat_stream(req: ChatRequest):
    """Streaming chat endpoint (single-node mode only)."""
    if model is None or tokenizer is None:
        raise HTTPException(503, "Model not loaded")
    if shard_config.total_shards > 1:
        raise HTTPException(400, "Use /v1/forward in pipeline mode")

    validated_messages(req.messages)
    messages = [{"role": m.role, "content": m.content} for m in req.messages]
    temp = req.temperature or 0.7
    top = req.top_p or 0.9
    tokens = req.max_tokens or 2048

    if backend == "mlx":
        gen = chat_stream_mlx(messages, temp, top, tokens)
    else:
        gen = chat_stream_torch(messages, temp, top, tokens)

    def sse():
        try:
            logger.info(f"Starting SSE stream for {len(messages)} messages")
            for text in gen:
                chunk = {"choices": [{"delta": {"content": text}, "finish_reason": None}]}
                yield f"data: {json.dumps(chunk)}\n\n"
            logger.info("SSE stream completed successfully")
        except Exception as e:
            logger.error(f"SSE stream error: {e}", exc_info=True)
            # Send error as a message so the client knows what happened
            error_chunk = {"choices": [{"delta": {"content": f"\n\n[Error: {str(e)}]"}, "finish_reason": "error"}]}
            yield f"data: {json.dumps(error_chunk)}\n\n"
        finally:
            yield "data: [DONE]\n\n"

    return StreamingResponse(sse(), media_type="text/event-stream")


@app.post("/v1/tokenize")
async def tokenize(req: TokenizeRequest):
    """Render the model's chat template and return exact IDs for the .NET coordinator."""
    if tokenizer is None:
        raise HTTPException(503, "Tokenizer not loaded")

    validated_messages(req.messages)
    messages = [{"role": m.role, "content": m.content} for m in req.messages]
    prompt = render_chat_prompt(messages)
    token_ids = tokenizer.encode(prompt, add_special_tokens=False)
    return {
        "token_ids": token_ids,
        "eos_token_id": tokenizer.eos_token_id,
    }


@app.post("/v1/forward")
async def forward(req: ForwardRequest):
    """Pipeline mode: run hidden states through this shard's layer slice.

    First shard (is_first=True): provide token_ids — embeds and runs through layers.
    Subsequent shards: provide hidden_states — runs through layers.
    Last shard (is_last=True): also applies LM head and returns logits.
    """
    if model is None:
        raise HTTPException(503, "Model not loaded")

    sc = shard_config

    if req.hidden_states is not None:
        if len(req.hidden_states) != 1 or len(req.hidden_states[0]) > MAX_SEQUENCE_TOKENS:
            raise HTTPException(400, "hidden_states must contain one bounded sequence")
    if req.attention_mask is not None and len(req.attention_mask) > MAX_SEQUENCE_TOKENS:
        raise HTTPException(400, "attention_mask is too long")

    if backend == "mlx":
        import mlx.core as mx

        # Get input tensor
        if sc.is_first and req.prompt is not None:
            # First shard: tokenize prompt and embed
            token_ids = tokenizer.encode(req.prompt, add_special_tokens=False)
            hidden_states = mlx_embed(token_ids)
        elif sc.is_first and req.token_ids is not None:
            hidden_states = mlx_embed(req.token_ids)
        elif req.hidden_states is not None:
            hidden_states = mx.array(req.hidden_states)
            if hidden_states.ndim == 2:
                hidden_states = mx.expand_dims(hidden_states, 0)
        else:
            raise HTTPException(400, "Provide prompt/token_ids (first shard) or hidden_states (subsequent shards)")

        # Build attention mask
        seq_len = hidden_states.shape[1]
        if req.attention_mask is not None:
            mask = mx.array(req.attention_mask)
            if mask.ndim == 1:
                mask = mx.expand_dims(mask, 0)
        else:
            mask = mx.ones((1, seq_len))

        # Run through layer slice
        hidden_states = mlx_forward_slice(hidden_states, mask, sc.layer_start, sc.layer_end)

        # Last shard: apply LM head and return logits
        if sc.is_last:
            logits = mlx_lm_head(hidden_states)
            result = {
                "hidden_states": hidden_states.tolist(),
                "logits": logits.tolist(),
                "shard": f"{sc.shard_index}/{sc.total_shards}",
            }

            # If generate=True, sample tokens autoregressively
            if req.generate:
                temp = req.temperature or 0.7
                top = req.top_p or 0.9
                max_tok = req.max_tokens or 2048

                generated = []
                current_ids = list(req.token_ids) if req.token_ids else []

                for _ in range(max_tok):
                    next_token = mlx_sample(logits, temp, top)
                    if next_token == tokenizer.eos_token_id:
                        break
                    generated.append(next_token)
                    current_ids.append(next_token)

                    # Forward pass for next token
                    hs = mlx_embed(current_ids)
                    hs = mlx_forward_slice(hs, mx.ones((1, len(current_ids))), sc.layer_start, sc.layer_end)
                    logits = mlx_lm_head(hs)

                result["generated_tokens"] = generated
                result["generated_text"] = tokenizer.decode(generated, skip_special_tokens=True)

            return result
        else:
            return {
                "hidden_states": hidden_states.tolist(),
                "shard": f"{sc.shard_index}/{sc.total_shards}",
            }

    elif backend == "torch":
        import torch
        device = next(model.parameters()).device
        dtype = next(model.parameters()).dtype
        body = _torch_inner_model()

        # Get input tensor
        if sc.is_first and req.prompt is not None:
            token_ids = tokenizer.encode(req.prompt, add_special_tokens=False)
            ids = torch.tensor([token_ids], dtype=torch.long, device=device)
            hidden_states = body.embed_tokens(ids).to(dtype)
        elif sc.is_first and req.token_ids is not None:
            ids = torch.tensor([req.token_ids], dtype=torch.long, device=device)
            hidden_states = body.embed_tokens(ids).to(dtype)
        elif req.hidden_states is not None:
            hidden_states = torch.tensor(req.hidden_states, dtype=dtype, device=device)
            if hidden_states.dim() == 2:
                hidden_states = hidden_states.unsqueeze(0)
        else:
            raise HTTPException(400, "Provide prompt/token_ids (first shard) or hidden_states (subsequent shards)")

        # Build attention mask
        seq_len = hidden_states.shape[1]
        if req.attention_mask is not None:
            mask = torch.tensor(req.attention_mask, dtype=torch.long, device=device)
            if mask.dim() == 1:
                mask = mask.unsqueeze(0)
        else:
            mask = torch.ones((1, seq_len), dtype=torch.long, device=device)

        # Run through layer slice
        with torch.no_grad():
            hidden_states = torch_forward_slice(hidden_states, mask, sc.layer_start, sc.layer_end)

        if sc.is_last:
            hidden_states = body.norm(hidden_states)
            logits = model.lm_head(hidden_states)
            result = {
                "hidden_states": hidden_states.cpu().tolist(),
                "logits": logits.cpu().tolist(),
                "shard": f"{sc.shard_index}/{sc.total_shards}",
            }

            if req.generate:
                temp = req.temperature or 0.7
                top = req.top_p or 0.9
                max_tok = req.max_tokens or 2048

                generated = []
                current_ids = list(req.token_ids) if req.token_ids else []

                for _ in range(max_tok):
                    next_logits = logits[:, -1, :]
                    if temp > 0:
                        probs = torch.softmax(next_logits / temp, dim=-1)
                        next_token = torch.multinomial(probs, num_samples=1)
                    else:
                        next_token = torch.argmax(next_logits, dim=-1, keepdim=True)

                    token_id = next_token.item()
                    if token_id == tokenizer.eos_token_id:
                        break
                    generated.append(token_id)
                    current_ids.append(token_id)

                    ids_arr = torch.tensor([current_ids], dtype=torch.long, device=device)
                    hs = body.embed_tokens(ids_arr).to(dtype)
                    with torch.no_grad():
                        hs = torch_forward_slice(hs, mask[:, :len(current_ids)], sc.layer_start, sc.layer_end)
                        hs = body.norm(hs)
                        logits = model.lm_head(hs)

                result["generated_tokens"] = generated
                result["generated_text"] = tokenizer.decode(generated, skip_special_tokens=True)

            return {k: v for k, v in result.items()}  # ensure JSON serializable
        else:
            return {
                "hidden_states": hidden_states.cpu().tolist(),
                "shard": f"{sc.shard_index}/{sc.total_shards}",
            }

    else:
        raise HTTPException(500, f"Unknown backend: {backend}")

class DecodeRequest(BaseModel):
    token_id: int


@app.post("/v1/decode")
async def decode_token(req: DecodeRequest):
    """Decode a single token ID to text."""
    if tokenizer is None:
        raise HTTPException(503, "Tokenizer not loaded")
    text = tokenizer.decode([req.token_id], skip_special_tokens=True)
    return {"text": text, "token_id": req.token_id}


if __name__ == "__main__":
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(name)s %(levelname)s %(message)s")
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", default=os.environ.get("MODEL_PATH", "Qwen/Qwen3.5-4B"))
    parser.add_argument("--port", type=int, default=int(os.environ.get("PORT", "8080")))
    parser.add_argument("--host", default=os.environ.get("PLACECONTEXT_AI_BIND", "127.0.0.1"))
    parser.add_argument("--shard", default=os.environ.get("SHARD_SPEC"), help="Shard spec: index/total, e.g. 0/2")
    args, _ = parser.parse_known_args()
    uvicorn.run(app, host=args.host, port=args.port, limit_concurrency=8)
