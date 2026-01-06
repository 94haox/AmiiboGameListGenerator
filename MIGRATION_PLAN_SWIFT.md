# Swift 迁移文档（功能点全量对齐版）

本文档用于将当前 C#/.NET 8 的 `AmiiboGameListGenerator` 重写为 Swift CLI，并确保**功能点、错误策略与输出确定性**与现有实现一致（可作为验收清单）。

## 1. 迁移目标与范围

**目标产物**：一个基于 Swift Package Manager 的可执行程序，能生成与现有程序一致语义的 `games_info.json`。

**建议支持平台**：macOS（优先）+ Linux（CI/容器可构建）。

**“一致”定义（建议）**：
- JSON 语义一致（字段、平台分类、ID 列表、usage 内容）。
- 输出稳定（同输入环境多次运行哈希一致，满足仓库的确定性检查工作流）。
- CLI 参数、退出码与错误处理策略一致。

## 2. 功能点清单（必须全部覆盖）

### 2.1 CLI 与运行参数

必须支持并保持兼容的参数：
- `-h | -help`：输出帮助并退出（退出码 0）。
- `-i | -input {filepath}`：从本地读取 `amiibo.json`，未指定则走网络下载。
- `-o | -output {filepath}`：输出文件路径（默认 `games_info.json`）。
- `-p | -parallelism {value}`：最大并行度（默认 4）。
- `-l | -log {value}`：日志级别：`verbose/info/warn/error` 或 `0..3`。

### 2.2 数据源与加载逻辑

必须按现有实现加载/解析下列数据源：
- Amiibo 清单（JSON）：`https://raw.githubusercontent.com/N3evin/AmiiboAPI/master/database/amiibo.json`（或 `-i` 本地文件）。
- Switch/Switch2 标题库（JSON）：`https://raw.githubusercontent.com/blawar/titledb/master/US.en.json`。
- 3DS 标题库（XML）：`http://3dsdb.com/xml.php`。
- Wii U 标题库（JSON）：来自内置资源（现实现为 `Resources.resx` 内嵌 `WiiU.json`）。
- Amiibo 用法（HTML）：`amiibo.life`，每个 Amiibo 页面抓取 games panel 与 features。

加载失败策略必须对齐：
- 下载失败：记录 Error 并退出 `InternetError(-2)`。
- 解析/反序列化失败：记录 Error 并退出 `DatabaseLoadingError(-3)`。

### 2.3 Amiibo 名称规范化与 URL 生成

必须复刻 `AmiiboGameList/Amiibo.cs` 中的规则（这是抓取成功率的关键）：
- `DBAmiibo.Name` 的特殊映射与字符清洗（附录 A）。
- `amiiboSeries` 的特殊映射（附录 B）。
- `characterName` 的特殊映射（如 `Spork/Crackle`、`OHare`）。
- `DBAmiibo.URL` 生成逻辑：
  - Animal Crossing 卡片：先访问搜索页 `https://amiibo.life/search?q=...`，从结果中过滤 `cards` 链接；若搜索页 404 则回退到默认 URL 形式并记录 Warn。
  - 特例 URL：`super mario cereal`、`solaire of astora`。
  - series slug 清洗：移除 `!` `.`，将 `'` 与空格替换为 `-`，并处理 `street-fighter-6 -> street-fighter-6-starter-set`。
  - “cat” 结尾 URL：需要插入 `cat-` 前缀。

### 2.4 HTML 解析与用法提取

必须解析并复刻以下语义：
- games panel XPath：`//*[@class='games panel']/a`（Swift 端可用等价 CSS selector）。
- 游戏名提取：`.//*[@class='name']/text()[normalize-space()]`，并应用额外的字符串替换：
  - `Poochy & ` 移除
  - `Ace Combat Assault Horizon Legacy +` -> `...Legacy+`
  - `Power Pros` -> `Jikkyou Powerful Pro Baseball`
- 用法列表：`.//*[@class='features']/li`
  - `Usage` = 直接文本
  - `write` = 是否存在 `em` 且文本为 `(Read+Write)`
- 用法排序：按 `Usage` 忽略大小写排序，确保稳定输出。
- 特例：当 Amiibo 名为 `Shadow Mewtwo`，强制将游戏名设为 `Pokkén Tournament`。

### 2.5 平台识别与 TitleID 匹配

平台标签来自游戏节点中的平台文本（`switch` / `switch 2` / `wii u` / `3ds`），必须分类到 `Games` 的四个平台列表中。

匹配与兜底策略必须对齐：
- Switch：通过 “清洗后的游戏名小写” 在 Lookup 中找 ID 列表；若为空走硬编码补丁；仍失败写入 `missingGames`（附录 C）。
- Switch2：同上（附录 D）。
- Wii U：在内置表中用 `Name.Contains(gameName, OrdinalIgnoreCase)` 查找；取 `Ids` 的前 16 位；失败走硬编码补丁（附录 E）。
- 3DS：对比时去除非字母数字（`[^a-zA-Z0-9 -]`），用 `Contains` 做模糊匹配；取 `titleid[..16]`；失败走硬编码补丁（附录 F）。

成功时必须对 `gameID` 做：
- `Order` + `Distinct`（排序去重）。

每个平台列表必须按 `gameName`（忽略大小写）排序，确保稳定输出。

### 2.6 缺失游戏记录与退出码

必须维持缺失记录语义：
- `missingGames` 收集 “`{gameName} ({platform})`” 字符串。
- 程序结束时：
  - 若缺失列表非空：打印去重后的缺失项，并返回 `SuccessWithErrors(1)`。
  - 否则返回 `0`。

必须保留并对齐退出码含义（来自 `AmiiboGameList/Debugger.cs`）：
- `0`：成功且无缺失
- `1`：成功但存在缺失
- `-2`：网络错误
- `-3`：数据库加载/解析错误
- `-1`：未知错误

### 2.7 网络重试与 404 策略（关键错误管理）

`amiibo.life` 页面抓取必须复刻以下策略（源自 `GetAmiilifeStringAsync`）：
- 总尝试次数：5（默认）。
- 退避：第 N 次失败后等待 `(N+1)*5s` 再重试。
- 不重试：HTTP 404（直接抛错）。
- 重试：超时、WebException、HTTP 5xx。

并行处理对错误的处理需对齐：
- 对 404：记录 Warn，跳过该 Amiibo（输出空 Games），不终止整体。
- 对网络异常（如 WebException）：记录 Error 并以 `InternetError` 退出。
- 其他未知异常：记录 Error 并以 `UnknownError` 退出。

### 2.8 输出格式与确定性（Deterministic Output）

必须满足仓库 CI 的确定性输出检查（`.github/workflows/check_deterministic_output.yml`）：
- export 必须按 Amiibo ID（数值意义的 Hex）排序后输出。
- 各处集合排序/去重与现有实现一致（见上）。
- 输出 JSON 的缩进风格：现实现为 pretty print 后将双空格替换为 `\t`（tab）。

## 3. Swift 技术选型与映射

| 能力 | 现有实现 | Swift 建议 |
|---|---|---|
| 构建 | .NET SDK | Swift Package Manager |
| CLI | 手写解析 | `swift-argument-parser` |
| JSON | Newtonsoft.Json | `Codable` + `JSONEncoder/JSONSerialization` |
| XML | XmlSerializer | `Foundation.XMLParser`（零依赖）或 `XMLCoder` |
| HTML | HtmlAgilityPack | `SwiftSoup` |
| 并发 | `Parallel.ForEach` + `lock` | `TaskGroup` + `actor`/`AsyncSemaphore` |
| 日志 | Debugger + ConsoleColor | 自定义 Logger（可选 ANSI 颜色） |

说明：
- 若要最大化跨平台与可维护性，XML 优先选 `XMLParser`；若追求开发效率可选 `XMLCoder`。
- 为保证输出 key 的顺序与数值排序一致，建议使用“有序字典/有序键值数组”进行序列化（避免纯 `Dictionary` 的不确定性风险）。

## 4. Swift 目标架构（推荐）

建议分层（利于单测与对齐验收）：
- `CLI/`：参数解析与入口。
- `DataSources/`：AmiiboAPI、TitleDB、3DSDB、WiiUResource、AmiiboLife。
- `Parsers/`：JSON/XML/HTML 解析。
- `Normalization/`：名称/series/url/游戏名修正规则与硬编码映射表（单独文件，便于维护）。
- `Matcher/`：平台识别与 TitleID 匹配。
- `Output/`：排序、序列化、写文件、missing 输出。

并发与状态：
- 用 `TaskGroup` 并发处理每个 Amiibo。
- 用 `actor ExportStore` 管理 `export` 与 `missingGames` 的并发写入。
- 用一个并发限制器（如 `AsyncSemaphore`）将并发度映射到 `-p`。

## 5. 迁移步骤（建议按“可验收里程碑”推进）

1) **建立 Swift 工程骨架**：SPM 可执行 target + 依赖（ArgumentParser/SwiftSoup/可选 XMLCoder），并能输出 `-h`。

2) **实现基础模型与 Hex 表示**：
- Hex 的内部排序必须按数值（UInt64），输出 key 字符串必须为 `0x` + 16 位小写十六进制。

3) **实现数据源加载**：
- 先完成 `-i` 本地读取 + WiiU 资源读取（离线可测）。
- 再接入 Switch/3DS/AmiiboAPI 网络下载与解析（失败策略对齐退出码）。

4) **实现 URL 生成与 HTML 抓取**：
- 复刻 AC 卡片搜索逻辑与 404 回退。
- 复刻 5 次重试/退避/404 不重试。

5) **实现 HTML 解析、平台分流与 TitleID 匹配**：
- 复刻 gameName 替换、Shadow Mewtwo 特例、usage 解析与排序。
- 复刻四个平台匹配策略与硬编码补丁表。

6) **实现输出稳定化与格式对齐**：
- 排序、去重、tab 缩进、missingGames 去重打印与退出码。

7) **验收与对比**：
- 在同一输入下对比 Swift 与 C# 输出（建议使用哈希与 JSON 规范化后 diff）。

## 6. 验收标准（建议写入 CI）

最低验收：
- `-h`/参数行为/退出码对齐。
- 运行两次输出哈希一致（确定性）。
- “缺页 404 可跳过、网络失败会失败退出”的错误策略对齐。

建议增强：
- 固定 `-i` 输入（同一份 amiibo.json）时 Swift 与 C# 输出完全一致（含 key 顺序与缩进）。

## 7. 附录：必须迁移的硬编码与修正规则清单

### 附录 A：Amiibo 名称规范化（DBAmiibo.OriginalName -> Name）

- `8-Bit Link` -> `Link The Legend of Zelda`
- `8-Bit Mario Classic Color` -> `Mario Classic Colors`
- `8-Bit Mario Modern Color` -> `Mario Modern Colors`
- `Midna & Wolf Link` -> `Wolf Link`
- `Toon Zelda - The Wind Waker` -> `Zelda The Wind Waker`
- `Rosalina & Luma` -> `Rosalina`
- `Zelda & Loftwing` -> `Zelda & Loftwing - Skyward Sword`
- `Samus (Metroid Dread)` -> `Samus`
- `E.M.M.I.` -> `E M M I`
- `Tatsuhisa “Luke” Kamijō` -> `Tatsuhisa Luke kamijo`
- `Gakuto Sōgetsu` -> `Gakuto Sogetsu`
- `E.Honda` -> `E Honda`
- `A.K.I` -> `A K I`

并叠加通用清洗：
- 去掉 `Slider`，`R.O.B.` -> `R O B`
- 去掉 `.`，将 `'` 与 `"` 处理为空格/移除
- 将 ` & `、` - ` 替换为空格
- `Trim()`

### 附录 B：Amiibo 系列规范化（amiibo_series -> amiiboSeries）

- `8-bit Mario` -> `Super Mario Bros 30th Anniversary`
- `Legend Of Zelda` -> `The Legend Of Zelda`
- `Monster Hunter` -> `Monster Hunter Stories`
- `Monster Sunter Stories Rise` -> `Monster Hunter Rise`
- `Skylanders` -> `Skylanders Superchargers`
- `Super Mario Bros.` -> `Super Mario`
- `Xenoblade Chronicles 3` -> `Xenoblade Chronicles`
- `Yu-Gi-Oh!` -> `Yu-Gi-Oh! Rush Duel Saikyo Battle Royale`

### 附录 C：Game.gameName -> sanatizedGameName 特例

- `Mario + Rabbids: Kingdom Battle` -> `Mario + Rabbids Kingdom Battle`
- `Shovel Knight` -> `Shovel Knight: Treasure Trove`
- `Little Nightmares: Complete Edition` -> `Little Nightmares Complete Edition`

### 附录 D：Switch 兜底 TitleID（当 Lookup 为空）

- `Cyber Shadow` -> `0100C1F0141AA000`
- `Jikkyou Powerful Pro Baseball` -> `0100E9C00BF28000`
- `Shovel Knight Pocket Dungeon` -> `01006B00126EC000`
- `Shovel Knight Showdown` -> `0100B380022AE000`
- `Super Kirby Clash` -> `01003FB00C5A8000`
- `The Legend of Zelda: Echoes of Wisdom` -> `01008CF01BAAC000`
- `The Legend of Zelda: Skyward Sword HD` -> `01002DA013484000`
- `Yu-Gi-Oh! Rush Duel Saikyo Battle Royale` -> `01003C101454A000`

### 附录 E：Switch2 兜底 TitleID（当 Lookup 为空）

- `Donkey Kong Bananza` -> `70010000096809`

### 附录 F：Wii U 兜底 TitleID（当内置表匹配失败）

- `Shovel Knight Showdown` -> `000500001016E100`, `0005000010178F00`, `0005000E1016E100`, `0005000E10178F00`, `0005000E101D9300`

### 附录 G：3DS 兜底 TitleID（当模糊匹配失败）

- `Style Savvy: Styling Star` -> `00040000001C2500`
- `Metroid Prime: Blast Ball` -> `0004000000175300`
- `Mini Mario & Friends amiibo Challenge` -> `000400000016C300`, `000400000016C200`
- `Team Kirby Clash Deluxe` -> `00040000001AB900`, `00040000001AB800`
- `Kirby's Extra Epic Yarn` -> `00040000001D1F00`
- `Kirby's Blowout Blast` -> `0004000000196F00`
- `BYE-BYE BOXBOY!` -> `00040000001B5400`, `00040000001B5300`
- `Azure Striker Gunvolt 2` -> `00040000001A6E00`
- `niconico app` -> `0005000010116400`
