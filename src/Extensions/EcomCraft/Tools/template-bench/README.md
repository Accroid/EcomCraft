# 模板测试台 / Template Bench

把一组测试商品图批量送入指定模板，保存结果并生成带人工评分列的报告，用于评估模板质量和调整参数前后的对比（F20）。中英文说明见下 / Chinese and English notes below.

## 中文

### 运行

```bash
node src/Extensions/EcomCraft/Tools/template-bench/bench.cjs \
  --url http://127.0.0.1:7801 \
  --products <测试商品图目录> \
  --templates white-main,scene \
  --out <报告输出目录> \
  --user <账号> --pass <密码>        # 服务器开启登录时需要
```

| 参数 | 说明 |
|---|---|
| `--url` | 平台服务器地址，默认 `http://127.0.0.1:7801` |
| `--products` | 测试商品图目录（PNG / JPEG / WebP） |
| `--templates` | 逗号分隔的模板 ID，可用模板见工作台或 `EcomCraftStudioTemplates` 接口 |
| `--out` | 输出目录：每次运行生成 `run-<时间>/images/`、`report.csv` 和 `report.html` |
| `--style` | 风格 ID，默认取服务端第一个 |
| `--count` | 每个商品 × 模板的候选张数，默认 1 |
| `--timeout` | 单个任务等待秒数，默认 600 |
| `--compare` | 上次的 `report.csv`，其中的评分会作为“上次”列带入新报告 |

### 报告与评分

- `report.csv`：每张候选一行，`verdict`（合格 / 不合格）和 `notes`（问题说明）留空供人工填写。
- `report.html`：并排对比页，直接用浏览器打开。
- 调整模板或参数后重新运行并带 `--compare 上次/report.csv`，即可对比两轮结果。

### 注意

- 工具只调用公开 HTTP 接口并下载结果，不会修改服务器的 `Data/`、`Models/` 目录。
- 保留原商品像素的模板会先调用平台抠图；抠图失败的商品会跳过并提示。
- 结果保存在运行工具的机器上，不占用服务器输出目录。

## English

Run the bench against a running EcomCraft server with a folder of product photos and a list of template IDs. Each run writes `run-<stamp>/images/`, a `report.csv` with empty `verdict` / `notes` columns for manual scoring, and a side-by-side `report.html`. Pass `--compare <previous>/report.csv` to carry previous scores into the new report when comparing before and after a change.

The tool only uses the public HTTP API and downloads results; it never writes to the server's `Data/` or `Models/` folders. Templates that preserve the product pixels are cut out through the platform first; products whose cut-out fails are skipped with a notice.
