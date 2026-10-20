# SwarmUI 用户设置指南

> **语言：** [English 原文](User%20Settings.md) · 简体中文

本页记录用户设置中的输出路径命名模板。更多设置项的解释可在 UI 中点击参数旁的 `?` 查看。

## 输出路径格式

`User → OutpathBuilder → Format` 支持下列占位符：

| 占位符 | 含义 |
|---|---|
| `[year]` | 四位年份，例如 `2023` |
| `[month]` | 两位月份，例如 `07` |
| `[month_name]` | 月份英文全名，例如 `July` |
| `[day]` | 两位日期，例如 `29` |
| `[day_name]` | 星期英文全名，例如 `Saturday` |
| `[hour]` | 两位小时，例如 `12` |
| `[minute]` | 两位分钟，例如 `04` |
| `[second]` | 两位秒数，例如 `30` |
| `[millisecond]` | 三位毫秒，例如 `057` |
| `[request_time_inc]` | 每个请求递增的序号。可与小时、分钟组合，例如 `[hour][minute][request_time_inc]`，生成便于排序且尽量唯一的前缀 |
| `[prompt]` | 正向提示词，可能受 `MaxLenPerPart` 限制而截断 |
| `[negative_prompt]` | 负向提示词，可能受 `MaxLenPerPart` 限制而截断 |
| `[prompthash]` | 正向提示词的 SHA-256 前 8 位 |
| `[negativeprompthash]` | 负向提示词的 SHA-256 前 8 位 |
| `[seed]` | Seed 参数值 |
| `[cfg_scale]` | CFG Scale 参数值 |
| `[width]` | 图像宽度参数值 |
| `[height]` | 图像高度参数值 |
| `[steps]` | Steps 参数值 |
| `[model]` | 模型文件名 |
| `[model_title]` | 模型元数据中的标题 |
| `[user_name]` | 用户名 |
| `[batch_id]` | 批次中该图像的索引 |
| `[参数名称]` | 读取对应参数的值；参数名必须准确匹配。例如 `[refinermodel]` 会填入细化模型名称 |

如果生成的文件名已经存在，SwarmUI 会在名称末尾追加数字索引。例如已有 `123-a cat.jpg` 时，新文件可能命名为 `123-a cat-1.jpg`。