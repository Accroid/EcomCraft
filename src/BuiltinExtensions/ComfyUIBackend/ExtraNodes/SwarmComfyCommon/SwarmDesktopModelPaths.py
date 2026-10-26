"""Register the shared model library belonging to a Comfy Desktop installation."""

import logging
from pathlib import Path


def register_shared_models(folder_paths):
    """Append existing shared model folders without changing files or path precedence."""
    try:
        install = Path(folder_paths.__file__).resolve().parent
        if not (install / "main.py").is_file():
            return []
        # Only the enclosing Desktop installation layout is trusted; never scan other drives.
        installs = next((parent for parent in list(install.parents)[:3]
                         if parent.name.casefold() == "comfyui-installs"), None)
        if installs is None:
            return []
        shared = installs.parent / "ComfyUI-Shared" / "models"
        if not shared.is_dir():
            return []
        folders = {
            "checkpoints": ("checkpoints",),
            "vae": ("vae",),
            "loras": ("loras",),
            "text_encoders": ("text_encoders", "clip"),
            "diffusion_models": ("diffusion_models", "unet"),
            "clip_vision": ("clip_vision",),
            "embeddings": ("embeddings",),
            "controlnet": ("controlnet", "t2i_adapter"),
            "upscale_models": ("upscale_models",),
        }
        added = []
        for category, names in folders.items():
            if category not in folder_paths.folder_names_and_paths:
                continue
            for name in names:
                path = shared / name
                if path.is_dir():
                    folder_paths.add_model_folder_path(category, str(path))
                    added.append((category, str(path)))
        if added:
            logging.info("SwarmUI: using Comfy Desktop shared model library (%d folders).", len(added))
        return added
    except (OSError, RuntimeError) as error:
        logging.warning("SwarmUI: could not inspect Comfy Desktop shared model library (%s).", type(error).__name__)
        return []
