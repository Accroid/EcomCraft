#!/usr/bin/env python3
"""
Self-hosted prompt-enhancement server for EcomCraft.
Serves Alibaba-PAI BeautifulPrompt (pai-bloom-1b1-text2prompt-sd) as an OpenAI-compatible API.

Why this script exists:
  pai-bloom is a *base* model (no chat template). EcomCraft's "beautiful_prompt" flavor therefore
  calls /v1/completions with the model's official fixed instruction template. This server applies
  that template server-side, so it works whether the endpoint is hit with /completions or /chat.

Endpoints:
  GET  /v1/models
  POST /v1/completions       (recommended for pai-bloom)
  POST /v1/chat/completions   (convenience; uses the last user message)

Device selection is automatic: CUDA -> Apple MPS -> CPU.

Quick start:
  pip install -r requirements.txt
  python serve_prompt_enhancer.py --host 0.0.0.0 --port 8000
Then in EcomCraft admin settings:
  flavor = BeautifulPrompt, endpoint = http://<this-host>:8000/v1, model = alibaba-pai/pai-bloom-1b1-text2prompt-sd
"""

import argparse
import time
import uuid

import torch
import uvicorn
from fastapi import FastAPI
from pydantic import BaseModel
from transformers import AutoModelForCausalLM, AutoTokenizer

DEFAULT_MODEL = "alibaba-pai/pai-bloom-1b1-text2prompt-sd"
TEMPLATE = "Instruction: Give a simple description of the image to generate a drawing prompt.\nInput: {raw}\nOutput:"

app = FastAPI(title="EcomCraft Prompt Enhancer")
MODEL = {"name": None, "tokenizer": None, "model": None, "device": None}


class CompletionRequest(BaseModel):
    model: str | None = None
    prompt: str
    max_tokens: int = 384
    temperature: float = 1.0
    top_p: float = 0.95
    repetition_penalty: float = 1.2


class ChatMessage(BaseModel):
    role: str
    content: str


class ChatRequest(BaseModel):
    model: str | None = None
    messages: list[ChatMessage]
    max_tokens: int = 384
    temperature: float = 1.0
    top_p: float = 0.95


def now_ts():
    return int(time.time())


def generate(raw, max_tokens=384, temperature=1.0, top_p=0.95, repetition_penalty=1.2):
    tok = MODEL["tokenizer"]
    model = MODEL["model"]
    device = MODEL["device"]
    text = TEMPLATE.format(raw=raw.strip())
    encoded = tok(text, return_tensors="pt").to(device)
    do_sample = temperature is not None and temperature > 0
    with torch.no_grad():
        out = model.generate(
            **encoded,
            max_new_tokens=max_tokens,
            do_sample=do_sample,
            temperature=max(float(temperature), 1e-5),
            top_p=float(top_p),
            repetition_penalty=float(repetition_penalty),
            pad_token_id=tok.eos_token_id,
        )
    gen = out[0][encoded["input_ids"].shape[1]:]
    return tok.decode(gen, skip_special_tokens=True).strip()


@app.get("/v1/models")
def list_models():
    return {
        "object": "list",
        "data": [{"id": MODEL["name"], "object": "model", "created": now_ts(), "owned_by": "self-hosted"}],
    }


@app.post("/v1/completions")
def completions(req: CompletionRequest):
    text = generate(req.prompt, req.max_tokens, req.temperature, req.top_p, req.repetition_penalty)
    return {
        "id": "cmpl-" + uuid.uuid4().hex[:24],
        "object": "text_completion",
        "created": now_ts(),
        "model": MODEL["name"],
        "choices": [{"index": 0, "text": text, "finish_reason": "stop"}],
        "usage": {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0},
    }


@app.post("/v1/chat/completions")
def chat(req: ChatRequest):
    raw = req.messages[-1].content
    text = generate(raw, req.max_tokens, req.temperature, req.top_p)
    return {
        "id": "chatcmpl-" + uuid.uuid4().hex[:24],
        "object": "chat.completion",
        "created": now_ts(),
        "model": MODEL["name"],
        "choices": [{"index": 0, "message": {"role": "assistant", "content": text}, "finish_reason": "stop"}],
        "usage": {"prompt_tokens": 0, "completion_tokens": 0, "total_tokens": 0},
    }


def pick_device():
    if torch.cuda.is_available():
        return "cuda"
    if getattr(torch.backends, "mps", None) is not None and torch.backends.mps.is_available():
        return "mps"
    return "cpu"


def load_model(model_id, dtype_name):
    device = pick_device()
    # fp16 is only reliable on CUDA; MPS/CPU use fp32 to avoid numerics issues.
    dtype = {"float16": torch.float16, "bfloat16": torch.bfloat16, "float32": torch.float32}[dtype_name]
    print(f"[enhancer] device={device} model={model_id} dtype={dtype if device == 'cuda' else 'fp32(auto)'}")
    tok = AutoTokenizer.from_pretrained(model_id)
    kwargs = {"torch_dtype": dtype} if device == "cuda" else {}
    model = AutoModelForCausalLM.from_pretrained(model_id, **kwargs)
    model.to(device).eval()
    MODEL["name"] = model_id
    MODEL["tokenizer"] = tok
    MODEL["model"] = model
    MODEL["device"] = device


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=8000)
    parser.add_argument("--model", default=DEFAULT_MODEL)
    parser.add_argument("--dtype", default="float16", choices=["float16", "bfloat16", "float32"])
    args = parser.parse_args()
    load_model(args.model, args.dtype)
    uvicorn.run(app, host=args.host, port=args.port)
