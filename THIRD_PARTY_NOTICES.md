# 上游版权与第三方组件声明

EcomCraft Studio 基于 SwarmUI 二次开发。以下完整保留所基于的 SwarmUI 版本 README 中的 Legal 部分；组件是否安装或启用取决于实际配置，各组件与模型仍受各自许可约束。

来源：[SwarmUI README — Legal](https://github.com/mcmonkeyprojects/SwarmUI/blob/40375eb0bd7a4ff25cda731bfa2b70966356a272/README.md#legal)。

## Legal

This project:
- embeds a copy of [7-zip](https://7-zip.org/download.html) (LGPL).
- has the ability to auto-install [ComfyUI](https://github.com/comfyanonymous/ComfyUI) (GPL).
- has the option to use as a backend [AUTOMATIC1111/stable-diffusion-webui](https://github.com/AUTOMATIC1111/stable-diffusion-webui) (AGPL).
- can automatically install [christophschuhmann/improved-aesthetic-predictor](https://github.com/christophschuhmann/improved-aesthetic-predictor) (Apache2) and [yuvalkirstain/PickScore](https://github.com/yuvalkirstain/PickScore) (MIT).
- can automatically install [git-for-windows](https://git-scm.com/download/win) (GPLv2).
- can automatically install MIT/BSD/Apache2/PythonSoftwareFoundation pip packages: [spandrel](https://pypi.org/project/spandrel/), [dill](https://pypi.org/project/dill/), [imageio-ffmpeg](https://pypi.org/project/imageio-ffmpeg/), [opencv-python-headless](https://pypi.org/project/opencv-python-headless/), [matplotlib](https://pypi.org/project/matplotlib/), [rembg](https://pypi.org/project/rembg/), [kornia](https://pypi.org/project/kornia/), [Cython](https://pypi.org/project/Cython/)
- can automatically install [ultralytics](https://github.com/ultralytics/ultralytics) (AGPL) for `YOLOv8` face detection (ie `SwarmYoloDetection` node or `<segment:yolo-...>` syntax usage may become subject to AGPL terms),
- can automatically install [insightface](https://github.com/deepinsight/insightface) (MIT) for `IP Adapter - Face` support
- uses [JSON.NET](https://github.com/JamesNK/Newtonsoft.Json) (MIT), [FreneticUtilities](https://github.com/FreneticLLC/FreneticUtilities) (MIT), [LiteDB](https://github.com/mbdavid/LiteDB) (MIT), [ImageSharp](https://github.com/SixLabors/ImageSharp/) (Apache2 under open-source Split License), [ATL.NET](https://github.com/Zeugma440/atldotnet) (MIT)
- embeds copies of web assets from [BootStrap](https://getbootstrap.com/) (MIT), [Select2](https://select2.org/) (MIT), [JQuery](https://jquery.com/) (MIT), [exif-reader](https://github.com/mattiasw/ExifReader) (MPL-2.0), [music-metadata](https://github.com/Borewit/music-metadata) (MIT).
- contains some icons from [Cristian Munoz](https://www.figma.com/community/file/1311159026125960259/7000-free-ui-icons) (CC-BY-4.0), the font [inter by rsms](https://github.com/rsms/inter) (OFL), [Unifont by GNU](https://unifoundry.com/unifont/) (OFL), [Material Symbols Outlined by Google](https://fonts.google.com/icons) (Apache2).
- can be used to install some custom node packs, which have individual license notices for any non-pure-FOSS licenses before install.
- supports user-built extensions which may have their own licenses or legal conditions.

SwarmUI itself is under the MIT license, however some usages may be affected by the GPL variant licenses of connected projects list above, and note that any models used have their own licenses.

### Previous License

(For updates prior to June 2024)

The MIT License (MIT)
Copyright (c) 2024 Stability AI

### License

The MIT License (MIT)

Copyright (c) 2024-2026 Alex "mcmonkey" Goodwin

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
