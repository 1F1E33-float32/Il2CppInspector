# 后续开发规范

本文件是仓库的根规范，适用于所有目录。`.editorconfig`、`.csharpierrc.json` 和 `.gitattributes` 是格式的最终依据，`Directory.Build.props` 统一编译选项和产物路径；本文件与它们冲突时，以这些配置文件为准，并同步修正本文件。

## 1. 环境与构建

- `Il2CppInspector.slnx`：.NET 10 解决方案；`src/Common/VersionedSerialization.Generator` 是 Roslyn 源生成器，目标为 `netstandard2.0`，只能使用该框架可用的 API。
- `Directory.Build.props` 统一设置 `TargetFramework=net10.0`、`ImplicitUsings=enable`、`Nullable=disable`、`TreatWarningsAsErrors=true`、`AnalysisLevel=latest`、`DebugType=embedded` 和 `SatelliteResourceLanguages=en`。两个 `VersionedSerialization*` 项目保留 `Nullable=enable`，源生成器另覆盖目标框架为 `netstandard2.0`。项目文件只声明差异；不要在禁用可空的项目里零散添加 `?` 注解，也不要重复引入隐式命名空间。
- 原生模拟运行库使用 `UnicornEngine.Unicorn` 预编译 NuGet 包，版本由 `UnicornPackageVersion` 统一声明；NuGet 还原时自动从 GitHub Release 下载至仓库根目录的 `packages/`，由 `RestoreAdditionalProjectSources` 提供本地源，已有包直接复用。`dotnet clean` 保留该依赖源。
- Release 构建必须 0 警告、0 错误；不通过 `#pragma`、`!` 或 `NoWarn` 规避警告。`src/Common/VersionedSerialization.Generator` 新增或修改诊断 ID 时，同步更新 `AnalyzerReleases.Unshipped.md`（Roslyn 发布跟踪，缺失时报 RS2000/RS2008）。
- .NET 使用 SDK 的 `UseArtifactsOutput`：编译输出在 `artifacts/bin/<项目名>/<配置>/`，中间文件与 NuGet 还原文件在 `artifacts/obj/<项目名>/`；构建配置目录为小写，显式 RID 时追加 `_<rid>`，不再追加单一目标框架。`dotnet publish` 只发布最终成品到 `artifacts/publish/`，不按项目、配置或 RID 再分层；GUI、CLI 与公共依赖位于该目录根部，库、插件和测试项目不单独发布。再次发布会替换 `ArtifactsPath` 内的目标成品目录。CLI Release 入口为 `artifacts/bin/Il2CppInspector.CLI/release/Il2CppInspector.dll`，通过 `dotnet` 启动。CLI 的 build / publish 目录中，插件 DLL、依赖清单和插件专属托管依赖统一平铺到 `plugins/`；CLI 已提供的公共依赖不重复复制，native 库集中放在 `runtimes/<rid>/native/`。可用 `-p:ArtifactsPath=<绝对路径>` 覆盖产物根目录。
- `dotnet clean Il2CppInspector.slnx` 清理全部配置与 RID 的 build / publish 产物；传入 `-c` 不缩小清理范围。保留 NuGet 还原文件与 `packages/` 依赖源。根 `Directory.Build.targets` 只递归删除所选 `ArtifactsPath` 内的产物目录。
- `src/GUI` 是 C# + Avalonia 12.2 桌面应用，项目为 `Il2CppInspector.GUI.csproj`，启用编译绑定。GUI 和 CLI 直接调用 Common 的 `AnalysisRunner`，不启动分析子进程、不使用 IPC。GUI 通过 `GamePlugins.Installed` 读取插件及输入要求。
- CLI 引用 GUI 项目但不引用其程序集；解决方案构建会生成两个入口。发布 CLI 或解决方案时，由 CLI 先准备成品目录，再调用 GUI 发布到同一目录。GUI 不单独参与解决方案发布，避免重复清空目录；`--no-build` 复用同配置/RID 的构建结果。`src/GamePlugins.targets` 为两个宿主复制相同插件布局。GUI 入口为 `Il2CppInspector.GUI.exe`（或 `dotnet Il2CppInspector.GUI.dll`），CLI 入口保持 `Il2CppInspector`。
- 源码放入 `src/CLI`、`src/GUI`、`src/Common/<项目名>` 或 `src/Plugins`，测试放入 `tests/<项目名>`；解决方案文件夹与磁盘目录对应。依赖中的 native runtime assets 只保留 Windows、Linux、macOS 的 x64 / ARM64 变体。
- 新增插件项目时：
  - 目录 `src/Plugins/<GameId>/`，项目名 `Il2CppInspector.Plugin.<GameId>.csproj`（即程序集名），设置 `EnableDynamicLoading=true`、引用 Common，并显式设置 `RootNamespace` 为 `Il2CppInspector.Plugin.<game>`；
  - 加入 `Il2CppInspector.slnx` 的 `/src/Plugins/<game>/` 解决方案文件夹，同一游戏的各版本放在同一文件夹下；
  - 打包由 `src/Plugins/Directory.Build.targets` 自动处理，不需要改 CLI；
  - Common 的 `internal` 成员已通过 `InternalsVisibleTo` 对 CLI、Tests 和所有插件项目开放，不要为插件或测试把成员改为 `public`。

## 2. 目录与模块职责

| 路径 | 职责 |
| --- | --- |
| `src/CLI` | 命令行解析、终端进度显示；无参数显示帮助。 |
| `src/GUI` | C# + Avalonia 桌面界面，后台直接调用 Common；任务进度、诊断与取消相互独立。 |
| `src/Common/Il2CppInspector.Common/Analysis` | GUI/CLI 共用请求与加载、建模、导出流程。 |
| `src/Common/Il2CppInspector.Common/IL2CPP` | 元数据与二进制加载、版本判断、注册结构扫描。 |
| `src/Common/Il2CppInspector.Common/Next` | 版本化元数据与二进制记录定义。 |
| `src/Common/Il2CppInspector.Common/FileFormatStreams` | PE/ELF/Mach-O/NSO 等文件格式读取。 |
| `src/Common/Il2CppInspector.Common/Architectures` | 各架构的注册结构定位。 |
| `src/Common/Il2CppInspector.Common/Reflection` | 仿 System.Reflection 的类型模型。 |
| `src/Common/Il2CppInspector.Common/Cpp` | C/C++ 类型、声明生成与 Unity 头文件。 |
| `src/Common/Il2CppInspector.Common/Model` | 应用模型与原生布局分析。 |
| `src/Common/Il2CppInspector.Common/Outputs` | DummyDll、C#、C++、JSON、脚本、PDB、DWARF 输出。 |
| `src/Common/Il2CppInspector.Common/Plugins` | 插件契约：`GamePlugin`、`GameMetadataAdapter`、`GameNativeModel` 等。 |
| `src/Plugins/<GameId>` | 单个游戏版本的插件。 |
| `src/Plugins/Shared/<family>` | 多个游戏共用的插件源码，由各插件项目通过 `.props` 编入。 |
| `tests/Il2CppInspector.Tests` | 集成测试控制台程序。 |
| `src/Common/Bin2Object`、`src/Common/VersionedSerialization*` | 二进制对象读写与版本化序列化。 |

## 3. 模块规范

模块之间的分层规则：

- 游戏专属逻辑留在对应的插件项目；同一引擎分支多个游戏共用的逻辑下沉到 `src/Plugins/Shared/<family>/`，与游戏无关的通用能力下沉到 `src/Common/Il2CppInspector.Common`。
- Common 不引用任何游戏项目，也不硬编码某个游戏的地址、密钥、魔数或运行时布局。

### 3.1 `src/CLI`

- 只负责命令行解析、插件列表与终端阶段进度；加载、建模与输出调度调用 Common 的 `AnalysisRunner`。
- 自动识别插件有歧义（多个插件同时匹配）时列出候选并要求显式传入 `--game <GameId>`；显式指定 `--game` 但无匹配或文件不匹配时直接报错退出。
- 输出目录布局：`DummyDll/`；每个脚本目标一个 `Script_<target>/`（`il2cpp.py`、`il2cpp.json`，未选 Debug 时还有 `il2cpp.h`），头文件与 JSON 只生成一次，其余目标目录复制；Debug 在根目录写 `<binary>.pdb` 或嵌入 DWARF 的二进制。未选目标的流式导出插件仍在根目录写 `il2cpp.h` 与 `il2cpp.json`。
- `-t IDA` 在 `Script_IDA/` 中同时输出 GUI 版 `il2cpp.py` 和 idat 终端版 `il2cpp_t.py`。终端版依赖 tqdm，支持双行进度、按 q 保存退出，以及通过脚本旁的 `.progress.json` 恢复匹配数据库中的顺序进度。
- `--json-progress` 保留为命令行自动化输出：每行一个 progress/status JSON；`--list-games` 输出 GameId，与该选项同用时附带输入需求。GUI 不依赖这些命令行选项。

### 3.2 `src/Common/Il2CppInspector.Common`

- 负责元数据与二进制加载（`IL2CPP`、`Next`、`FileFormatStreams`、`Architectures`）、反射与 C++ 类型系统（`Reflection`、`Cpp`）、应用模型与原生布局分析（`Model`）、所有导出器（`Outputs`）以及插件契约（`Plugins`）。
- 二进制与元数据处理：
  - 校验签名、魔数、版本字段、长度、偏移、计数和索引范围；
  - 异常数据抛出 `InvalidDataException`，不支持的版本或变体抛出 `NotSupportedException`，错误信息写明格式或对象名称及期望值。

### 3.3 `src/Plugins/<GameId>` 与 `Shared/<family>`

- `GameId` 格式为 `NAME_REGION_PLATFORM_VERSION`，与插件目录名完全一致；版本可以是数字版本号或构建时间戳，`x.x.x` 表示支持多个游戏版本。
- 插件实现公开、无参构造的 `GamePlugin` 子类。`Matches` 必须能区分同一游戏的不同版本。
- `GamePlugin.RequiredInputs` 使用 `GamePluginInputs` 声明所需文件；默认需要 Binary 和 Metadata，嵌入式元数据插件只需要 Binary，另有启动元数据的插件可追加 StartupMetadata。加载入口在读取文件前忽略未声明的输入；StartupMetadata 可按 `StartupMetadataFileName` 从元数据旁自动查找。
- 插件只负责解码、规范化记录和经二进制验证的运行时布局差异；反射模型、声明生成、依赖排序和所有导出复用 Common，不另写 DLL、头文件或 JSON 输出器。
- 加密或重排的记录先规范化为 stock 记录：仅需规范化元数据、后续走 stock 管线的插件直接构造 `Metadata`；需要自定义运行时布局的插件通过 `Metadata.CreateForPlugin` 挂接 `GameMetadataAdapter`。
- 运行时布局：只提供不同于 stock 的布局钩子；`NativeLayoutAnalysisModel` 负责方法遍历与声明命名。字段宽度、缓存记录或 vtable 表示的改动须有二进制证据，并在注释中写明逆向地址。
- 导出命名沿用 stock 规则：保留元数据中的类型、方法和字段名，不加游戏前缀。
- 同一引擎分支的多个游戏共用 `src/Plugins/Shared/<family>/` 下的基类与辅助代码，项目通过 `<Import Project="../Shared/<family>/<family>.props" />` 编入；游戏目录只放密钥、偏移、记录读取器和确有差异的布局。
- 插件目录统一为：`<Game>Plugin.cs`、`<Game>Build.cs`（版本常量，可选）、`IL2CPP/`（适配器、二进制、默认值、运行时头）、`Model/`、`Next/Metadata/<Game>Records.cs`、`Reflection/`，不再按游戏名嵌套子目录。
- 同一游戏的多个版本共享相同源码时，用 `<Compile Include="../<GameId>/..." Link="..." />` 链接，不复制文件；某版本需要不同实现时，再把该文件复制到本版本目录并去掉链接。
- 嵌入资源的 `LogicalName` 固定为 `Il2CppInspector.Plugin.<game>.<文件名>`；共享资源沿用源版本或共享族的 `LogicalName`（如 `Il2CppInspector.Plugin.<family>.<文件名>`）。
- 每个插件在 `tests/Il2CppInspector.Tests/Plugin/GameSampleTests.cs` 的 `Samples` 表中加一行（二进制文件名和基线）。

### 3.4 `tests/Il2CppInspector.Tests`

- `tests/Il2CppInspector.Tests` 是控制台程序，按模式分发（`Program.cs`）。新增模式须同时更新 `Program.cs` 的用法说明和 `tests/Il2CppInspector.Tests/README.md`。
- 目录与生产代码对应：通用逻辑放 `Common/`，CLI 流程放 `CLI/`，插件测试放 `Plugin/`。
- 所有游戏走同一套测试，不为单个游戏写专属测试或模式：`--plugins` 对每个已安装插件做相同的选择与拒绝检查；`--game <GameId> <样本目录> [输出目录]` 在真实样本上核对解码表大小和运行时布局基线，给出输出目录时再写 DummyDll 并验证引用解析。解码或布局变化导致基线改变时，须在提交说明中附逆向证据。
- 断言使用 `TestAssert.Check`，只记录通过时用 `TestAssert.Pass`；仓库路径、fixture 路径和外部进程使用 `TestEnvironment`。测试中不捕获异常，失败直接终止。
- 只通过公共契约（`GamePlugin`、`Il2CppInspector`、`TypeModel`、`AppModel`）测试插件，不访问插件内部成员，也不使用反射。
- 以下测试不要写：
  - 重复断言同一输入的同一结果；
  - 同义反复或 `Check(true, ...)`；
  - 只验证刚构造的对象非 null；
  - 被更强断言覆盖的格式细节；
  - 简单 getter；
  - 只测试 .NET 或第三方库自身行为的测试。

### 3.5 `src/GUI`

- 布局为三列：640px 启动器（游戏选择、路径、输出目标）、280px 任务列表、剩余宽度的任务详情；其余选项放在 560px 可拖动的 Settings 浮层，Escape 关闭。
- 保留深色渐变、Segoe UI 13px、28px 控件、四级游戏选择表及每列独立滚动；选择列展开为 140ms ease-out-quint，连续展开错开 35ms。动画在窗口帧时钟上运行，结束后落到精确宽度。
- 游戏选择按 GameId 的四段逐列展开；只有一个选项时自动选中。Auto-detect 不指定 Game。插件列表通过 `GamePlugins.Installed` 获取，按 `RequiredInputs` 禁用无关文件输入与 Select 按钮，并从请求中省略这些输入。Startup metadata 留空时由核心自动查找。
- `AnalysisJob` 在后台线程调用 `AnalysisRunner`，通过线程安全队列将进度和日志交回 UI；任务列表及详情每 33ms 消费更新。详情只显示参数、阶段、最终状态和异常，其余诊断包含在 Copy 日志中。详情在底部时自动跟随，上滚暂停跟随。
- 禁止并发任务写入相同或包含关系的输出目录（含多镜像的 output-N 目录）。取消通过 CancellationToken 与进度/导出检查点协作完成；窗口关闭时等待任务停止后退出。
- 输入框使用 Avalonia 文本编辑控件，支持 IME、选择、剪切复制粘贴及 Home/End。可见文案沿用英文。

## 4. 进度与错误处理

- Common 的 `AnalysisRunner` 通过 `OperationProgress` 回调报告加载、建模和导出阶段；CLI 的 `ProgressBar` 与 GUI 分别呈现。
- 库项目和插件不直接读写控制台。普通诊断使用按执行上下文隔离的 `AnalysisLog`，状态与进度使用回调；错误向宿主抛出。不得通过 `Console.SetOut` 修改进程全局输出。
- 用户可见的错误信息使用英文，并指明出错文件、记录或参数。

## 5. 代码风格

### 格式

- C# 使用 4 空格缩进、LF 换行、文件末尾换行，CSharpier 行宽 150；XML、项目文件、`.slnx`、`.props`、`.targets` 和 JSON 使用 2 空格。
- 所有文本文件为无 BOM 的 UTF-8 和 LF，不留行尾空白（Markdown 除外）。
- 仅对本次改动文件运行 `csharpier format <改动路径>`，避免重排无关文件。`dotnet format` 可能写入 CRLF，运行后须再执行一次 CSharpier。
- Python 脚本资源使用 4 空格缩进；`RuntimeCaches.py` 是插入 `shared_base.py` 占位符的片段，单独编译会报缩进错误，属于正常情况。

### 语言特性

- 类型明显或冗长时使用 `var`。
- 使用目标类型 `new()`、集合表达式 `[]` 和模式匹配（`is not null`、属性模式、`switch` 表达式）。
- 只取字典的值时遍历 `.Values`，不要写 `foreach (var (_, value) in dictionary)`。
- 新文件使用文件作用域命名空间；既有文件不为此单独改动。
- `using` 放在命名空间外，System 优先，不在导入组之间插入空行，删除未使用的 `using`；不写 `this.`。
- 只在构造时赋值的字段标记 `readonly`；无状态的局部函数和 lambda 标记 `static`。
- 单行语句体可以省略花括号，多行语句体必须带花括号；修改已有代码时保持该文件的写法。
- 表达式体只用于属性、索引器、访问器和单表达式的简短成员。

### 命名

| 符号 | 规则 |
| --- | --- |
| 类型、命名空间、方法、属性、事件、公共字段、常量 | PascalCase |
| 接口 / 类型参数 | `I` 前缀 / `T` 前缀 |
| 参数、局部变量 | camelCase |
| 私有实例字段 | `_camelCase` |
| 私有静态字段 | `s_camelCase` |
| 私有 `static readonly` 字段、私有常量 | PascalCase |

- 既有文件的私有字段多为无前缀 camelCase：修改时沿用该文件的风格，不顺带批量重命名；新文件按上表。
- 既有的公共名称（如 `JSONMetadata`）保持不变，避免破坏插件 API。

### 注释

- 注释说明原因、格式依据和不明显的约束，不复述代码。二进制格式、偏移、魔数和版本判断写明对应的 IL2CPP/Unity/游戏版本、类名/方法名/字段名或逆向地址（如 `0x19F266500`）。
- 公共契约使用 `/// <summary>`；私有实现不强制，不写空洞的文档注释。
- 不在新增代码、注释、测试或文档中引用本仓库以外的辅助项目、私人脚本或仓库中不存在的文件（如分析报告、反编译 dump、验证样本路径）；只写类名、方法名、字段名或逆向地址。既有文件头的版权声明与目标反编译工具说明保持不变。
- 注释使用该文件已有的语言。

## 6. 简洁性：不写与要删除的代码

以下写法不得新增；发现时删除或修改，前提是输出不变。

- **冗余代码与无调用方成员**：
  - 未使用的私有成员、参数、局部变量和 `using`；
  - 不可达分支、注释掉的代码、描述已完成工作的 TODO；
  - 全仓库没有调用方的 `public`/`internal` 成员。删除非私有成员前，用 grep 核对 Common、CLI、所有插件和 Tests（包括按字符串名称的反射调用）。
- **无用封装**：
  - 只转发调用、不增加语义的薄方法或薄类；
  - 只做一对一字段拷贝的中间类型；
  - 只有一个实现、也没有替换需求的新接口；
  - 与基类实现完全相同的重写。
- **无意义赋值**：
  - 字段显式初始化为默认值；读取前就被覆盖的赋值；
  - 只为立即 `return` 而存在的临时变量；只写不读的字段或属性；
  - 先建临时集合再拷贝到另一个集合；
  - 解析时读取却从不使用的值，只需跳过的字段用 `_ = reader.Read...()` 读过。
- **过度防御**：
  - 对不可能为 null 的值判空，或对非空值使用 `?.`/`??`；
  - 调用方和被调用方重复校验；重复检查已保证的条件（如 `Initialize` 已拒绝的版本）；
  - 吞掉异常或原样重抛的 `try/catch`。
- **冗余转换与重复实现**：
  - 多余的强制转换和括号；对 `ulong` 做 `& ulong.MaxValue` 之类无效运算；
  - 与现有公共帮助方法重复的逻辑。

以下内容属于必要校验，**必须保留**：

- 签名、魔数、版本字段、长度、偏移、计数、索引范围的校验；
- `MetadataUsages ?? []` 等对低版本元数据（< 19）缺失数据的处理；
- 不同目标工具或工具版本之间的 Python 兼容分支；
- 结构体字段顺序、`[Version]` 特性、偏移量、密钥、白盒表和版本识别阈值——这些由二进制证据决定，改动须在提交说明中附证据；
- 记录格式来源的注释。

## 7. 验证

在仓库根目录执行：

```bash
csharpier format <改动路径>
csharpier check <改动路径>
dotnet build Il2CppInspector.slnx -c Release
# 不需要本地游戏文件的测试
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --plugins
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --serialization
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --progress
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --cli-targets artifacts/bin/Il2CppInspector.CLI/release/Il2CppInspector.dll
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --cpp-layout
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --unity-headers
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --metadata-version
python -I tests/Il2CppInspector.Tests/Common/Outputs/ScriptResourcesTests.py
# 验证 GUI/CLI 联合发布与插件布局
dotnet publish Il2CppInspector.slnx -c Release -r win-x64 --self-contained false
# 修改插件时在真实样本上运行
dotnet run --project tests/Il2CppInspector.Tests -c Release --no-build -- --game <GameId> <样本 Native 目录> [输出目录]
```

- 修改输出或解码逻辑时，用修改前后的 CLI 在真实样本上对比输出：PDB/DWARF 应逐字节相同，`il2cpp.json` 与 `dump.cs` 应相同。C# 输出与 DummyDll 受并行导出顺序和 MVID 影响，每次运行都会不同，不能用来判断是否回归。

## 8. 文档与交付

- 保留工作树中已有的未提交改动：不执行 `git checkout`/`restore`/`reset --hard`/`stash`/`clean` 等会丢弃改动的命令；除非明确要求，不要提交。
- 不提交 `artifacts/`、`bin/`、`obj/` 和 `output/` 等生成内容。
- 支持的游戏、`GameId` 或 CLI 选项变化时，同步更新 `tests/Il2CppInspector.Tests/Plugin/GameSampleTests.cs` 的 `Samples` 表和 CLI 帮助文本。
- 提交信息使用 Conventional Commits 格式：`type(scope): summary`。
  - `type` 取 `feat`、`fix`、`refactor`、`perf`、`test`、`docs`、`chore`；
  - `scope` 用模块（如 `outputs`、`cli`、`plugins`）或游戏代号（小写）；
  - `summary` 用英文祈使句，小写开头。
- 格式化、重构与行为变更分开提交。
- 交付时说明变更内容、验证结果（构建、测试计数）以及未完成的检查。
