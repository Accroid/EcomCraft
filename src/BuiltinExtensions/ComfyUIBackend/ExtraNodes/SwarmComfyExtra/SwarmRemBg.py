from PIL import Image
import numpy as np
import torch

# The rembg session holds the background-removal model (about 1 GB for rembg's default BRIA RMBG). Loading it took about 10 seconds on every call,
# so it is created once and kept for the life of the process.
_session_cache = {}

def get_rembg_session():
    if "session" not in _session_cache:
        from rembg import new_session
        try:
            # The model rembg's remove() picks when it is given no session.
            _session_cache["session"] = new_session("bria-rmbg")
        except Exception:
            # Older rembg releases know no bria-rmbg; their remove() default is new_session()'s default as well.
            _session_cache["session"] = new_session()
    return _session_cache["session"]

class SwarmRemBg:
    @classmethod
    def INPUT_TYPES(s):
        return {
            "required": {
                "images": ("IMAGE",),
            }
        }

    CATEGORY = "SwarmUI/images"
    RETURN_TYPES = ("IMAGE", "MASK",)
    FUNCTION = "rem"

    def rem(self, images):
        from rembg import remove

        session = get_rembg_session()
        output = []
        masks = []
        for image in images:
            i = 255.0 * image.cpu().numpy()
            img = Image.fromarray(np.clip(i, 0, 255).astype(np.uint8))
            img = img.convert("RGBA")
            img = remove(img, session=session, post_process_mask=True)
            output.append(np.array(img).astype(np.float32) / 255.0)
            if 'A' in img.getbands():
                mask = np.array(img.getchannel('A')).astype(np.float32) / 255.0
                masks.append(1. - mask)
            else:
                masks.append(np.zeros((64,64), dtype=np.float32))
        return (torch.from_numpy(np.array(output)), torch.from_numpy(np.array(masks)))

NODE_CLASS_MAPPINGS = {
    "SwarmRemBg": SwarmRemBg,
}
