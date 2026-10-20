"""Keep ComfyUI's CUDA probe from crashing on the bundled ROCm runtime.

The bundled PyTorch build is ROCm based on AMD systems.  Some versions of
comfy-kitchen still call ``torch.cuda.is_available`` while importing their
CUDA backend; on Windows that probe can terminate Python with an access
violation before ComfyUI starts.  ROCm is handled by comfy-kitchen's HIP
backend, so the CUDA probe is safe to report as unavailable in this case.
"""

try:
    import torch

    if getattr(torch.version, "hip", None) and torch.version.cuda is None:
        torch.cuda.is_available = lambda: False
except Exception:
    pass
