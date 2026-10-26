"""GPU-free checks: python -B SwarmUITests/ComfyDesktopModelPathsTests.py."""

import importlib.util
from pathlib import Path
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch


source = Path(__file__).resolve().parents[1] / "src/BuiltinExtensions/ComfyUIBackend/ExtraNodes/SwarmComfyCommon/SwarmDesktopModelPaths.py"
spec = importlib.util.spec_from_file_location("desktop_paths", source)
desktop_paths = importlib.util.module_from_spec(spec)
spec.loader.exec_module(desktop_paths)


class DesktopModelPathsTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="ecomcraft-shared-models-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.install = self.root / "ComfyUI-Installs/test/ComfyUI"
        self.install.mkdir(parents=True)
        (self.install / "main.py").touch()
        self.shared = self.root / "ComfyUI-Shared/models"
        self.calls = []
        self.paths = SimpleNamespace(
            __file__=str(self.install / "folder_paths.py"),
            folder_names_and_paths={name: ([], set()) for name in
                                    ("checkpoints", "vae", "text_encoders", "diffusion_models", "custom_nodes")},
            add_model_folder_path=lambda *args: self.calls.append(args),
        )

    def test_existing_shared_models_are_appended_without_loading_code(self):
        for name in ("checkpoints", "vae", "clip", "unet", "custom_nodes"):
            (self.shared / name).mkdir(parents=True)
        added = desktop_paths.register_shared_models(self.paths)
        self.assertEqual({category for category, _ in added}, {"checkpoints", "vae", "text_encoders", "diffusion_models"})
        self.assertEqual(self.calls, added)
        self.assertTrue(all(len(call) == 2 for call in self.calls))  # No is_default/priority override.
        self.assertIn(("checkpoints", str(self.shared / "checkpoints")), added)

    def test_missing_shared_directory_leaves_existing_paths_alone(self):
        self.assertEqual(desktop_paths.register_shared_models(self.paths), [])
        self.assertEqual(self.calls, [])

    def test_unrelated_install_does_not_discover_nearby_shared_models(self):
        (self.shared / "checkpoints").mkdir(parents=True)
        ordinary = self.root / "ordinary/ComfyUI"
        ordinary.mkdir(parents=True)
        (ordinary / "main.py").touch()
        self.paths.__file__ = str(ordinary / "folder_paths.py")
        self.assertEqual(desktop_paths.register_shared_models(self.paths), [])

    def test_inaccessible_directory_does_not_break_backend_startup(self):
        with patch.object(Path, "resolve", side_effect=PermissionError):
            self.assertEqual(desktop_paths.register_shared_models(self.paths), [])


if __name__ == "__main__":
    unittest.main()
