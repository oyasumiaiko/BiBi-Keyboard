# 火山引擎 ASR 上下文增强字段速查

适用范围：豆包流式语音识别 2.0（OpenSpeech v3 / `bigmodel_async`）。

目标：只整理**能提升上下文理解与识别准确率**的字段、格式与限制。

---

## 1. 上下文 / 热词 / 纠错相关字段（`request.corpus`）

### 1.1 `context`（位于 `request.corpus.context`）

**用途**：将上下文或热词直接随请求发送，用于提升识别准确率。

**结构**：

```
context: {
  context_type: "hotwords" | "dialog_ctx",
  context_data: [...]
}
```

**`context_type=hotwords`**
- `context_data` 为热词数组（文本）。

**`context_type=dialog_ctx`**
- `context_data` 支持文本或图片：
  - 文本：`{"text": "..."}` 或 `{"type": "text", "text": "..."}`  
  - 图片：`{"image_url": "..."}`（多模态）
- **长度限制**：
  - 流式请求：最多约 **100 tokens**
  - 二遍识别（`enable_nonstream=true`）：最多约 **5000 汉字**

> 适合把“窗口级摘要 / 近期输入”注入到识别上下文中。

**推荐的 dialog_ctx 内容模板（LLM 生成，单行）**：

```
领域摘要:...；关键词:...；易错纠正:...；格式/约束:...
```

说明：
- 关键词为高置信度词语列表（逗号分隔）
- 易错纠正使用“错词->正词”
- 无内容的段落可省略

### 1.2 热词表：`boosting_table_id` / `boosting_table_name`

**用途**：提高指定热词被识别的概率。

**限制与格式**：
- 每次请求仅支持 **1 个热词表**
- 单应用最多 **500 个热词表**
- 每个热词表最多 **2000 条**
- 每条热词长度 **< 10 字符**
- 权重范围 **1~10**
- 热词文件格式：`txt`（UTF-8），每行 `词语 权重`（空格分隔）
- 主要支持中文 / 英文

### 1.3 替换词表：`correct_table_id` / `correct_table_name`

**用途**：将特定词条强制替换为目标词（纠错）。

**说明**：
- 字段可用，但具体格式/限制需参考官方“替换词”文档。
- 目前未找到可直接访问的静态文档页，后续补充。

---

## 2. 识别准确率 / 语义理解相关开关（`request`）

这些字段会影响“最终结果准确率”，属于上下文理解的“隐性提升”：

- `enable_nonstream`：开启二遍识别（收尾阶段更准）
- `enable_ddc`：语义顺滑（更自然的文本）
- `enable_itn`：逆文本归一化（数字/单位等）
- `enable_punc`：自动标点
- `enable_poi_fc`：POI / 地名识别增强
- `enable_music_fc`：音乐识别增强
- `language`：语言代码（如 `zh-CN`、`en-US`）

---

## 3. 实践建议（基于官方限制）

- **流式识别 `dialog_ctx` 建议保持短**：100 tokens 上限很容易触达，建议摘要化再发送。
- **热词固定场景优先用热词表**：稳定且可复用；临时热词才用 `context_type=hotwords`。
- **对准确率要求高时开启 `enable_nonstream`**：收尾会更准确但延迟略高。

---

## 参考（官方文档）

- OpenSpeech v3 大模型流式接口文档（含 `context`、`enable_nonstream`、`enable_ddc` 等字段）
  - https://www.volcengine.com/docs/6561/1354869
- 热词文档（热词表格式与限制）
  - https://www.volcengine.com/docs/6561/155739
