# Magical Princess 原生 Android 移植：交接

GitHub：私有仓库 https://github.com/chankamman-dev/princess-android（main）。仓库里有游戏图标、启动页背景（art/）和游戏截图（shots/），只能保持私有；构建产物（cloud/build、ptool/obj、pctest/out、apk）不入库，工作目录里的游戏数据也不在仓库里。

**目录布局（2026-11-07 整理后）**
- 源码（本仓库）：`G:\ai\android-recompile\princess-android`（原 `G:\ai liabary\princess-android`）。
- 工作目录：`E:\android-recompile-work\princess-android-work`，脚本里写作 `/e/android-recompile-work/princess-android-work`。原来分在两处：E 盘的 `E:\princess-android-work`（dl 安装包、depottest、il 反编译、ptool-bin*、清点报告）和 F 盘的 `F:\princess-android-work`（unity、il2cpp、aa、apk、data、stage、port、签名等），现在合并到一起。两边重名的 `shader-report-bundles.txt`、`shtmp\`：E 盘原有的留在顶层，F 盘那份放在 `from-f\`。
- 整理时删掉了：`il2cpp-x64`（模拟器 x86_64 版 il2cpp 输出）、`apk-x64`、`apk-emu`，以及 `apk\` 里的打包中间文件（root、res、res-flat、gen、java、src、activity.jar、base/aligned/unaligned.apk，build-apk.sh 每次都会重新生成）。保留了 `apk\princess-android.apk`（11-07 15:42，Thor 上装的就是这一版）、`princess-debug.keystore`、arm64 的 `il2cpp\`。要再出模拟器版，跑 `ABI=x86_64 bash rebuild.sh` 会重新生成 il2cpp-x64（约 9.5 GB）。
- E 盘是 SATA SSD，持续大量写入会卡住整机（11-07 整理时多线程复制又卡过一次）。在 E 盘上做大量写入时用单线程并限速（例如 `robocopy /IPG:4`，不能和 `/MT` 同用），完整重编 il2cpp 时也可能变慢。

## 现状（2026-11-06 20:50）

- **模拟器上能玩**：到标题、走菜单、新游戏、剧情；画面颜色全屏正常，触屏和手柄（虚拟 Xbox 手柄）都能操作。截图在 `shots/`。
- **第三阶段（11-06 下午）新增**，详见下面「第三阶段」：
  - 启动页重做成正式版样式（标题美术背景、Steam/云存档/游戏数据三个状态、开始游戏、账户、设置，手柄可操作）。
  - **首次启动从 Steam 下载游戏数据**（6.7 GB，设备上转换），断点续传；以后 Steam 上的版本只要没改游戏程序，也能在设备上直接更新。
  - 性能：帧率上限（默认 60）、渲染分辨率、资源同步加载（启动从约 110 秒降到约 23 秒，模拟器测），游戏日志默认关闭。
  - 20:50：触屏适配（双指轻点返回、画廊拖动、战斗选区松手确定、修点同一位置被吞），见文末「触屏适配」。
  - 18:30 后：应用图标（游戏原版图标）和中文名「梦幻魔法公主」；设置加了「画面后处理」和「Steam 连接（离线模式）」；贴图精度查明做不了通用开关，没做（原因见「设置项」一节）。
- **Thor 上还是旧包**（11-06 中午装的那版，手柄不可用，没有新启动页）。新包 `E:\android-recompile-work\princess-android-work\apk\princess-android.apk` 要用户同意后再 `install -r`（已推的 6.3 GB 数据会被识别为已安装，不用重下）。
- 没验证（没有真机）：Thor 上的手柄、性能、真机驱动下的着色器；**真实 Steam 账号下载**（下载链路用匿名账号 + 免费 depot 在 PC 上验证过，本游戏要用户扫码后才能测）；云同步、成就；游戏进度存档与读档（设置存档已验证能写入）。
- 包名 `dev.kmprincess.magical`，可调试（`run-as` 推数据要用）。

## 第一阶段结论（静态分析，保留备查）

**能做，走活侠传路线（Unity Mono → IL2CPP arm64 + GLES3）。** 游戏是 Mono 后端，`Managed/` 里有完整的托管 DLL，不属于 StarVaders/梦灯花那种卡死的 IL2CPP 情况。

和活侠传相比，阻碍是：

1. **本机没有 Unity 6000.3.20f1。** 要下载官方 Editor（4.17 GB）和 Android 模块（1.54 GB），这一步要用户同意。活侠传那套 2020.3.49 用不了：libunity.so 必须和游戏的序列化版本、引擎版本完全一致。
2. **lomtool 是按 2020.3 写的。** 着色器转换、bundle 重定向、Addressables 这几块要改成 Unity 6 的格式，Addressables 也从 JSON 目录换成了二进制目录（2.9.1 版，`catalog.bin`）。
3. **数据量大。** 22853 个 bundle，共 6.4 GB。单个 OBB 装不下，要换成「所有文件访问权限 + 外部数据目录」，或者拆成 main 和 patch 两个 OBB。

反过来，有几处比活侠传简单：没有 Wwise（Unity 原生音频，安卓直接能用），只有一个成就接口，存档是 JSON 加自定义加密，和平台无关。

## 事实（全部从文件里确认）

| 项 | 值 |
| --- | --- |
| 游戏 / AppID | Magical Princess，3562120；Windows depot 3562121，本机 build 25478627（Steam 显示有更新，目标 26372585，63 MB，还没下） |
| 开发商 | Neotro Inc.（`app.info`） |
| 引擎 | Unity **6000.3.20f1**（changeset `c9ba695d4f07`），**Mono**（有 `MonoBleedingEdge`，`Managed/` 里 140 多个 DLL） |
| 渲染 | URP 2D（`Unity.RenderPipelines.Universal.2D.Runtime`），BuildSettings 里图形 API 是 `[18, 2]`，即 D3D12 和 D3D11 |
| 着色器 | 只有平台 4（D3D，DXBC）。和活侠传一样，要走 DXBC → HLSLcc → GLES3 |
| 资源清点 | `tools/ptool inventory` 全量结果在 `E:\android-recompile-work\princess-android-work\inventory.txt`：AudioClip 17093 个（Unity 原生音频，安卓直接能用），MonoBehaviour 48291 个，Sprite 4970 个，Texture2D 2305 个，**没有 VideoClip**。Shader 实例 449 个，因为每个 bundle 各带一份，实际约 60 种：Sprite Shaders Ultimate（Standard / 2D Lit URP / Additive 等）、URP 2D Sprite-Unlit、URP Particles、AllIn1SpriteShader、TextMeshPro、URP Terrain 一套（可能没用到）。SSU 和 AllIn1 是商店付费资源，没有源码，只能从 DXBC 转。只有 1 个 bundle（`882a7ab5…`）AssetsTools 读不了，原因是字符串解码出错，属于工具问题，不是游戏的问题 |
| 纹理格式 | RGBA32 963、DXT5 635、DXT1 438、RGB24 201、BC7 39、DXT1/DXT5 Crunched 8、Alpha8 13、ARGB32 8。Thor 的 GLES 支持 S3TC；BC7 如果不支持，Unity 会在 CPU 上解压，数量少，影响不大 |
| Unity 6 Shader 结构 | 和 2020.3 不一样：SerializedProgram 多了 `m_PlayerSubPrograms`、`m_ParameterBlobIndices`、`m_SerializedKeywordStateMask`，参数放在单独的 blob 里（字段树见 `E:\android-recompile-work\princess-android-work\shader-fields.txt`）。lomtool 的 shdump、rt-bundles 里读写 Shader 的部分要按这个结构重写 |
| 资源 | Addressables 2.9.1，二进制目录 `catalog.bin`（14 MB），22853 个 bundle，LZ4HC，6.4 GB。内部路径是 `{RuntimePath}\StandaloneWindows64\<hash>.bundle`（反斜杠） |
| 主要插件 | Spine、DOTween、Rewired、TextMeshPro + Febucci TextAnimator、AllIn1SpriteShader、Sprite Shaders Ultimate、Coffee SoftMask、Cinemachine、uLipSync、zxing、Newtonsoft.Json、Unity Services Analytics |
| 原生 DLL | `steam_api64.dll`（Steamworks.NET 2025.162.1 / SDK 1.62）、`lib_burst_generated.dll`、`libonigwrap.dll`、Rewired 的 DirectInput / WindowsGamingInput |
| Burst | 有 `lib_burst_generated.dll`，但 Assembly-CSharp 里没有 `[BurstCompile]`，只是 Unity 包（Collections、2D Animation 等）自带的。安卓上不带 Burst 库时会退回托管代码，IL2CPP 会一起编进去 |
| libonigwrap | 没有任何托管 DLL 引用它，可以丢掉 |
| Rewired | `Rewired_Core` 按名字找平台模块（含 `Rewired_Android`）；Assembly-CSharp 里有 `UnityInputManagerProxy`，安卓上应该退回 Unity 输入。`Rewired_Windows.dll` 是托管的，可以照样编译，只是别调用它 |
| Steam DRM | 没有加壳。但 `InitLoader.<Start>b__13_0` 里，`RestartAppIfNecessary` 抛 DllNotFound 时会走 `Application.Quit()`，要打补丁 |
| Steam 接口 | 只有这些：`SteamAPI.Init` / `RestartAppIfNecessary`（`SteamManager.Awake`、InitLoader）；`SteamUserStats.SetAchievement` + `StoreStats`（`AchievementController.Unlock` / `Update`）；`SteamApps.GetCurrentGameLanguage`（`ConsoleUtil.GetDeviceLanguage`）；`SteamFriends.GetPersonaName`、`SteamUtils.GetAppID`（只用来打日志）；`SteamInput.Init` / `RunFrame` / `GetControllerForGamepadIndex` / `GetInputTypeForHandle`（判断手柄类型） |
| 成就 | `AchievementController.Unlock(int id)`：Steam 已初始化时调 `SetAchievement("ACV_" + id)`；已解锁的 id 存在 `MyData.gstatus.acvUnlockedIds`（List<int>），启动时补报就读这个 |
| 存档 | `SaveController.getSavePath` = `Application.persistentDataPath` + 文件名（试玩版会加 `_trial`）。文件：`v10_configdata.dat`、`v10_indexdata.dat`、`v10_userdata%N.dat`，另有 `RewiredSaveData.json`。内容是 Newtonsoft JSON 经 `Crypt.Encrypt` 加密，和平台无关 |
| Steam 云 | appinfo 的 `ufs`：根 `WinAppDataLocalLow`，路径 `Neotro Inc_/MagicalPrincess`，模式 `*.dat`，配额 50 MB / 1000 个文件。Mac 用 `MacAppSupport` 同路径，Linux 换成 `unity3d` |
| 其他平台 | appinfo 里的 oslist 是 `windows,macos`，有 Mac depot 3562122（6.99 GB），**没有 Linux depot**。Mac 版是 Mono 还是 IL2CPP 要下载 depot 才知道；Windows 版已经是 Mono，所以用不上 Mac 版 |
| 本机存档 | `%USERPROFILE%\AppData\LocalLow\Neotro Inc_\MagicalPrincess` 不存在，Steam userdata 里也没有 3562120（appmanifest 的 LastPlayed=0，用户没在 PC 上玩过） |

查法：appinfo 用 `scratchpad/appinfo.js` 解析 `F:\game software\appcache\appinfo.vdf`（v29 格式）；IL 用 `lomtool il-dump`，全量输出在 `E:\android-recompile-work\princess-android-work\il\acs.txt`；资源清点用本目录的 `tools/ptool`。

## 移植步骤（第二阶段）

1. 下载并静默安装 Unity 6000.3.20f1 Editor 和 Android 模块，放到 `E:\android-recompile-work\princess-android-work\unity`（需要用户同意）。
   - Editor：`https://download.unity3d.com/download_unity/c9ba695d4f07/Windows64EditorInstaller/UnitySetup64-6000.3.20f1.exe`
   - Android：`https://download.unity3d.com/download_unity/c9ba695d4f07/TargetSupportInstaller/UnitySetup-Android-Support-for-Editor-6000.3.20f1.exe`
   - 两个地址 HEAD 请求都返回 200。NDK 先试本机的 26.3 / 28.2；il2cpp 坚持要 r27c 的话，再问用户要不要下载。
2. il2cpp：`Managed/*.dll` + Android 版 UnityEngine 模块 → `libil2cpp.so`。织入补丁：
   - Steam 初始化失败不退出，`SteamManager.Initialized` 返回 true 或换成假实现。
   - `AchievementController.Unlock` 开头转到 Java 的成就上报。
   - 存档写完通知云同步；`AssetBundleRequestOptions.Crc` 改成 0。
   - 加 `Addressables.InternalIdTransformFunc`，把反斜杠换成 `/`，`RuntimePath` 指到数据目录。
3. 资源：所有序列化文件的目标平台从 19 改成 13，DXBC 着色器转 GLES3，再按 LZ4 重新打包。lomtool 的 rt-data / rt-bundles 要按 Unity 6 的 Shader 结构重新核对。
4. 打包：APK 装代码；6.4 GB 数据走外部目录。
5. Steam：复用 `lom-android/cloud`，AppID 改成 3562120，云端路径改成 `%WinAppDataLocalLow%Neotro Inc_/MagicalPrincess/`，只同步 `*.dat`。

## 工作量估计

- 下载安装 Unity，跑通 il2cpp：半天到 1 天，大部分是等待。
- 把 lomtool 改到 Unity 6（Shader 结构、二进制目录、重定向）：2–4 天，最不确定的就是着色器。
- 跑到标题画面：在上面两项之后再 1 天。
- 云存档、时长、成就：1 天（cloud 模块现成，只换参数和钩子）。
- 手柄（Rewired 在安卓上的表现）、触屏、分辨率、性能：1–2 天。

合计约 1–1.5 周。最大的风险是 Unity 6 着色器的 GLES3 转换，以及 Rewired 在安卓上能不能识别 Thor 的手柄。

## 2026-11-06 第二阶段（用户同意下载后，夜间自行推进）

### 环境

- Unity 6000.3.20f1 Editor + Android 模块装在 `E:\android-recompile-work\princess-android-work\unity`（当初装在 F 盘 NVMe 上，11-07 搬到 E 盘）。E 盘是 SATA SSD，持续写入会卡死（安装时磁盘忙碌度 600%+），大量写入要限速。安装包在 `E:\android-recompile-work\princess-android-work\dl`，签名已验证。
- 安装坑：
  - Android 模块要等 Editor 装完再装，否则静默安装会停在隐藏的提示框上。
  - Editor 的静默安装写完文件后会停在一个提示上（非管理员，加不了防火墙规则），文件齐了（有 `Editor\Uninstall.exe`）就可以结束进程。
  - 下载不要用 `&` 后台跑 curl：两个进程追加写同一个文件，包会坏。用后台任务 + `curl -C -` 循环续传。
- NDK 用本机已有的 28.2（路径写在不入库的 `tools/local.env`，模板见 `tools/local.env.example`），il2cpp 没有挑版本。Android 模块不带 NDK。Android SDK、adb、设备序列号也在 `local.env` 里。
- il2cpp 要 `--dotnetprofile=unityaot-linux`（Unity 6 没有 `unityaot`）；baselib.a 要从 `AndroidPlayer/Variations/il2cpp/Release/StaticLibs/arm64-v8a` 复制到 `Editor/Data/il2cpp/build/baselib/release_android_aarch64/`。libil2cpp.so 185 MB，本身已无调试信息。

### 工具（`tools/`）

| 文件 | 作用 |
| --- | --- |
| `ptool/`（`dotnet build -c Release -o E:\android-recompile-work\princess-android-work\ptool-bin3`） | `stage` 织入；`rt-data` / `rt-bundles` 重定向和着色器转换；`shdump` / `shrec` / `shtest` / `tplcmp` 调试 |
| `princessandroid/Bridge.cs` → `build-princessandroid.sh` | 游戏侧桥接 PrincessAndroid.dll |
| `build-il2cpp.sh`、`build-apk.sh`、`rebuild.sh` | 构建；`EMU=1` 出模拟器版（`nop-wfe.js` 把 libunity 里 128 处 sevl/wfe 换成 nop，只能用于模拟器） |
| `push-bundles.sh <serial>` | 把 `E:\android-recompile-work\princess-android-work\aa`（6.4 GB）分批用 `adb exec-in run-as … tar` 推到 `/data/data/dev.kmprincess.magical/files/aa`；一次推完整流会在约 1.6 GB 处断，所以分批、可续传 |
| `../cloud` | 云存档模块（从活侠传复制，包名 dev.kmprincess.cloud，AppID 3562120，同步 persistentDataPath 下的 `*.dat`） |

### 织入（ptool stage）

- Steamworks.NET 981 个 P/Invoke 换成空桩：`SteamAPI.Init` 失败、`RestartAppIfNecessary` 返回 false，游戏走无 Steam 分支，不会退出。
- `AchievementController.Unlock` 开头 → `Bridge.Achievement(id)`（`ACV_<id>`）。
- `MyData.ApplySaveDataUserMain` 结尾 → `Bridge.Unlocked(gstatus.acvUnlockedIds)`：首次触发时开始上报时长，并补报已得成就。
- `SaveController.FileSave` / `FileDelete` 结尾 → `Bridge.Saved()`（云上传）。
- `AddressablesImpl.ResolveInternalId` 返回值经 `Bridge.MapId`：`…\StandaloneWindows64\<hash>.bundle` → 应用内部 `files/aa/<hash>.bundle`。
- `AssetBundleRequestOptions.get_Crc` 恒为 0。
- 游戏会关掉 Unity 日志（`ILogger.set_logEnabled(false)`），织入时改成恒为 true；`PRINCESS_QUIET=1` 可保留原样。

### 着色器（Unity 6 格式，已弄清）

- 平台切片 = 表（数量 + 每项 off/len/seg）+ 条目。每个子程序指向两个条目：代码记录（版本、类型、统计、关键字字符串、代码、SourceMap、通道绑定）和参数记录（常量缓冲组 + 资源绑定）。两种记录都能逐字节回写。
- `m_PlayerSubPrograms[档位][i]`：D3D 版全在第 3 格；GLES 版第 0–2 格各放一份同样的列表，GLES3 和 Vulkan 的子程序混在同一格，按 `m_GpuProgramType` 区分（GLES3=4，Vulkan=25，D3D 15–18）。
- GLES3 写法照 Android 模块自带的 `unity default resources`：VS+FS 合成一个 GLSL 文本放在 progVertex，progFragment 清空；参数记录只有 `$Globals` 组 + 纹理绑定 + 一条 `$Globals` 绑定；`stageCounts=[1]`；顶点阶段的关键字掩码取 VS 和 FS 的并集。
- **classdata.tpk 的 Shader 结构缺最后的 `m_AssetLocalIdentifierInFile`（8 字节）**。播放器数据没有类型树，用 tpk 写回后每个着色器短 8 字节，Unity 报 `globalgamemanagers.assets is corrupted / Position out of bounds`。现在从游戏自己的 bundle 里取 Shader 类型树当模板（`Templates.cs`）。
- RDEF 重建沿用活侠传的代码，HLSLcc 也是活侠传编好的 `E:\lom-android-work\hlslcc-build\hlslcc-cli.exe`。
- 掩码修正：`T(U(cond) * 0xFFFFFFFFu)` 改写成 `(-T(cond))`（模拟器上 uint→int 会饱和），播放器数据里 208 处。
- 结果：播放器数据 1299 个变体失败 1 个（URP UberPost 的一个变体）；bundle 里 298 个含着色器，非缓冲变体全部成功。跳过 5308 个用 StructuredBuffer 的变体：主要是 `SKINNED_SPRITE`（`_SpriteBoneTransforms`，2D Animation 的 GPU 蒙皮）和 GPU 实例化，GLES 3.0 无法表示。如果游戏在 GLES3.1+ 设备上启用 GPU 精灵蒙皮，要么强制 CPU 蒙皮，要么改出 GLES 3.1 程序。
- 1 个 bundle（`882a7ab5…`）AssetsTools 解析失败，只在原始字节上改了平台号，里面的着色器没转换。

### 模拟器

- lime-sim 当时被 mistria 会话占用，而且只剩 1.4 GB，所以用 G:\AndroidLab 的现成镜像新建了 AVD `princess-sim`（`H:\android-emu\avd`，数据分区 24 GB，`-gpu swiftshader_indirect`，端口原为 5610，11-06 起改用 5660（5610 也被 isaac-sim 用，两个会撞端口，adb 会连到别人的模拟器），序列号 emulator-5660）。不涉及下载。
- 启动命令：`ANDROID_SDK_ROOT='G:\AndroidLab\sdk' ANDROID_AVD_HOME='H:\android-emu\avd' emulator -avd princess-sim -port 5660 -no-window -no-audio -no-boot-anim -no-snapshot -gpu swiftshader_indirect`
- 已过的坑：Unity 6 的 res 有 AppCompat 主题（GameActivity 用）要换掉；`values/unityLibrary`、`values/launcher` 嵌套目录 aapt2 不认，要摊平，否则 Java 层 `String resource ID #0x0` 崩溃。
- **模拟器要用 `-gpu host`**：`swiftshader_indirect` 下渲染线程卡在等宿主回应 `glGenFramebuffers`，几分钟都出不了第一帧。`-gpu host` 下显存约多占 1–2 GB。
- 模拟器刚启动时屏幕是锁着的，Unity 不进前台，要先 `input keyevent KEYCODE_WAKEUP; wm dismiss-keyguard`。
- **x86_64 测试包**（`ABI=x86_64 bash tools/rebuild.sh`）在模拟器上原生运行，启动 7 秒到标题；arm64 包经转译 14 秒。日常调试用 x86_64，arm64 只做最后核对。
- `-gpu host` 下我的模拟器崩过一次（qemu 退出码 139），重启后正常；崩溃时正在安装的 APK 会丢，要重装。

### 从黑屏到正常画面的几处修复

1. **黑屏**：URP 默认管线资源 `URP-HighFidelity` 开着 4 倍 MSAA，而 GLES 窗口表面是单采样，每个渲染通道都失败（`Attachment 0 was created with 1 samples but 4 samples were requested`）。`Bridge.Boot()`（织入 `InitLoader.Awake` 开头）把三档管线资源的 `msaaSampleCount` 都设为 1。只改当前档位不够：当前档是 Performant（本来就是 1），默认管线是 HighFidelity。
2. **颜色乱、关掉后处理后画面倒置**：全屏三角形的 Blit 着色器是按 D3D 的 `UNITY_UV_STARTS_AT_TOP` 编的（`texcoord.y = 1 - (vertexID & 2)`），URP 的 C# 端在 GLES 上按 GL 约定处理翻转，所以每次全屏 Blit 都多翻一次：开后处理时两次翻转抵消，画面是正的，但色彩分级 LUT 是用 Blit 生成的，LUT 倒了，所有颜色都乱掉。`Fixups6.FlipBlitUv` 在顶点代码里识别全屏三角形（有 `uint(gl_VertexID) & 2u`，且 `gl_Position.xy = V.xy * vec2(2.0, 2.0) + vec2(-1.0, -1.0)`），把 `= (-V.y) + 1.0;` 改回 `= V.y;`，播放器数据里 625 处。
3. **状态栏**：PC 的窗口设置让安卓播放器保留了系统栏。`patch-activity.js` 在打包时给 `UnityPlayerActivity.java` 加沉浸式全屏（`WindowInsetsController` 隐藏 + 旧式 flag），并在 0.6/2/5 秒再执行一次、系统栏被重新显示时 1.5 秒后再隐藏，因为 Unity 会在表面建好后自己再设一次（转译下启动慢，0.6 秒那次会被盖掉）。
4. 游戏的 `Screen.SetResolution(w, h, 全屏)` 全部转到 `Bridge.SetResolution`：始终全屏、用面板原生尺寸（`Display.main.systemWidth/Height`；`Screen.currentResolution` 拿到的是窗口大小）。
5. 临时目录的旧文件会骗过 HLSLcc 的崩溃检测（看 `.meta` 是否存在），导致别的着色器的 GLSL 混进来。`Retarget6.Convert` 每批开始前先清空临时目录。

### 其他织入（第二阶段新增）

- 2D Animation 的 `SpriteSkinUtility.CanUseGpuDeformation` 恒为 false（Thor 支持计算着色器，会走 GPU 蒙皮，需要没转换的 `SKINNED_SPRITE` 变体）。
- `Bridge.Boot` 在启动时调 `onGameStarted`：新游戏不会经过读档，原来只在读档时开始上报时长。
- 普通日志和警告不再带调用栈（游戏日志量很大）。调试开关：在 `<persistentDataPath>/princess-debug.txt` 写 `nopost` 可关掉所有相机的后处理。

### 手柄（2026-11-06 修好，模拟器验证）

- 现象：Thor 上手柄完全没反应。原因：Rewired 的 `Rewired.InputManager.DetectPlatform()` 和 `Rewired.Utils.ExternalTools.GetPlatformInitializer()` 是源码形式编进 Assembly-CSharp 的，PC 版编译时带的是 Windows 宏，所以运行时写死 `platform = Windows`，并把 Windows 原生输入（RawInput/XInput，依赖 user32.dll）交给 Rewired。安卓上初始化失败，日志里是 `Rewired: [ERROR] Windows platform could not be initialized!`。
- 修法（`Stage.cs` 织入）：`DetectPlatform` 改成 `platform = 7`（Android）、`scriptingBackend = 2`（IL2CPP）；`GetPlatformInitializer` 直接返回 null。Rewired 在安卓上只能用 Unity 旧输入管理器，游戏的 InputManager 里本来就有 Rewired 需要的 `Joy*Axis*` 轴（464 个）。
- 诊断：`PadWatch`（PrincessAndroid.dll）每 2 秒检查一次，手柄变化时在 logcat 打 `[PrincessPad] Unity joysticks: [...]` 和 `[PrincessPad] Rewired joysticks: [...]`。Unity 看不到 = 系统层问题；Unity 看到但 Rewired 没有 = 识别问题；两边都有但按键不对 = 映射问题。
- 模拟器测法：shell 用户在 uhid 组里，能用系统自带的 `uinput` 工具造一个虚拟 Xbox 手柄：`adb push tools/pad-test.json /data/local/tmp/` 后 `adb shell uinput /data/local/tmp/pad-test.json`（注册后 5 秒按 A、A、十字键下）。配置里的类型要写数字（100/101/103），不能写 `UI_SET_EVBIT` 这种名字。结果：Rewired 识别为 `Xbox Controller`，A 键进菜单、选新游戏，十字键能移动焦点，按键提示显示 Xbox 的 A/B 图标（`shots/emu-x64-gamepad-newgame.png`）。

### 后续工作（11-06 下午更新）

1. **Thor 已装 11-06 23:14 的新包**（手柄修复、正式版启动页、Steam 下载、图标、后处理/离线、触屏适配；已推的 6.3 GB 数据还在，22853 个文件）。**等用户实测**，重点看：启动页手柄操作、游戏里手柄、双指轻点返回、画廊拖动、战斗选区松手确定、启动时间、帧率（设置里开「开发者日志」，logcat 标签 `Princess` 每 5 秒打 `fps:`）、颜色和后处理。以后再装：`adb install -r -g E:\android-recompile-work\princess-android-work\apk\princess-android.apk`（要用户同意）。
2. 用户扫码登录后：云同步（`v10_*.dat`）、游戏时长、成就（`ACV_<id>`）；以及**真实的 Steam 下载**（可以在设置里点「检查游戏数据」验证下载链路，或在别的设备上全新安装）。
3. 还没覆盖的着色器：URP UberPost 1 个变体、GPU 实例化和 SKINNED_SPRITE 变体。出现粉色/空白先查这些。
4. 跟进 Steam 更新（build 26372585，改了代码）：电脑上 Steam 更新游戏后按「一次完整重建」走一遍，设备上只增量下载约 150 MB。
5. 「快速加载」如果在游戏中途造成明显卡顿，在设置里切「兼容」，或者以后改成只在启动阶段同步加载。
## 第三阶段（2026-11-06 下午）：Steam 下载与更新、启动页、性能、复查

### Steam 下载游戏数据（`cloud/.../GameData.java`、`SteamDepot.java`、`BundleStamp.java`）

- 思路：APK 只带代码和核心数据（约 400 MB），22853 个 Addressables bundle（6.7 GB）在设备上从 Steam 的 Windows depot（3562121）下载，再在设备上转换。
- **转换**：没有着色器的 bundle 只差一个字段，序列化文件头里版本串后面的目标平台 19→13。UnityFS 是 128 KB 一块的 LZ4，只解压改写含文件头的那一块（不压缩存回），其余块原样复制，块表不压缩写回（`BundleStamp`）。PC 上对全部 22853 个原版 bundle 验证过：完全解包后与原版只差平台字段，输出比原版大 4%，PC 上 12 秒全部转完。
- **带着色器的 299 个 bundle**（189 MB）没法在设备上转，用 PC 转好的版本放进 APK（`assets/port/shaders/`），安装时直接从 APK 拷。
- **下载**：JavaSteam 1.8.0 没有现成的 DepotDownloader，只有底层接口；`SteamDepot` 按 DepotDownloader 的流程实现：PICS 拿 depot 的 manifest id → depot 密钥 → manifest request code → CDN 服务器列表 → 下载并解密 manifest → 按块下载、解密解压、写入，文件 SHA-1 校验。8 个文件并行，状态写在 `files/aa/.princess-state`（manifest、所属 APK、每个文件原版的 SHA-1），中断后续传。
- JavaSteam 解压块要 zstd-jni 和 XZ for Java，本机都没有，**自己写了纯 Java 实现**：`cloud/src/main/java/com/github/luben/zstd/Zstd.java`（RFC 8878 解码器）和 `org/tukaani/xz/LZMAInputStream.java`（LZMA 解码器），只实现 JavaSteam 用到的接口。
- **PC 验证**：`tools/pctest/DepotTest.java` 用匿名账号下载免费的 Steamworks SDK Redist（app 1007 / depot 1006）4 个文件 111 MB，SHA-1 全对，zstd 和 LZMA 两种块都走到了。**本游戏的真实下载要用户账号，没测**；理论上同一条路径。
- **模拟器验证**（`tools/push-src.sh` 把原版 bundle 放到应用外部目录，启动页用隐藏参数 `--es localSource <目录>` 从这里安装）：清空数据后从原版安装 22853 个文件用时约 65 秒，全部转换成功，游戏正常进标题。
- **固定版本**：APK 记录它基于的 depot manifest（`assets/port/port.txt`，`tools/make-port.sh` 生成，`tools/pctest/PortVerify.java` 对照 Steam 客户端缓存的 manifest 校验过 248 个基础文件、299 个着色器包、22853 个 bundle 全部吻合）。首次下载永远下这个版本，保证和 APK 里的代码一致。
- 从 adb 推过数据的设备（Thor）：没有状态文件但文件数够，就算已就绪；登录后第一次检查会把它记成该版本的完整安装。

### Steam 更新（可行性结论）

- 用 Steam 客户端缓存的两份 manifest（`F:\game software\depotcache\3562121_*.manifest`）对比了本机版本（3167287020227237367，build 25478627）和待更新版本（3349508740461970556，build 26372585）：改了 **Assembly-CSharp 和几乎所有 Managed DLL**、globalgamemanagers、resources.assets、catalog，新增 74 个、修改 387 个、删除 1 个 bundle，共约 150 MB。
- 结论：**这类更新必须在电脑上重新生成 APK**（代码要重新 IL2CPP 编译）。设备端做的是：检测到 Steam 有新版本时，比较新版本里 APK 自带的文件是否变了——变了就提示「需要新版移植包」，继续玩当前版本；没变（只改资源）就直接在设备上增量更新。装上新 APK 后，设备只增量下载变化的 bundle（约 150 MB），不用重下 6.7 GB。
- 电脑上更新移植包：Steam 更新游戏 → `rt-data` → `rt-bundles`（只为了着色器包）→ `make-port.sh` → `rebuild.sh`。

### 启动页（`LauncherActivity.java`）

- 标题美术（`art/hero.jpg`，从标题截图缩出）全屏背景，底部渐变上放标题、三个状态（Steam 账户、云存档、游戏数据）、主按钮和账户/设置按钮；登录二维码、设置、提示都是居中卡片。手柄：十字键移动焦点，A 确认（没有焦点时 A 就是主按钮），B 返回/关卡片。
- 状态流转：未登录且没数据 →「登录 Steam 下载游戏」；已登录没数据 →「下载游戏数据 6.7 GB」（进度条、速度、剩余时间、暂停）；有数据 →「开始游戏」（已登录时先同步云存档再进）。已登录时后台查 Steam 版本，有设备可装的更新显示「更新 xx MB」，需要新 APK 时显示提示。
- 设置：帧率上限（30/60/90/120，默认 60）、渲染分辨率（100/85/70%）、资源加载（快速/兼容）、开发者日志；底部显示数据版本、可用空间；「检查游戏数据」补全缺失文件。设置存在 `princess_settings`，游戏侧通过 `CloudBridge.intSetting` 读。

### 性能

- **启动慢的真正原因**（模拟器上测）：InitLoader 的 LoadDefault 一次请求约 1100 个 bundle 和几百个资源，Unity 的异步加载每帧只推进一步，60 帧下要 70 多秒（PC 上显示器 144–240 帧，所以看不出来）；之前以为是显存、着色器编译都不对（帧率一直是 60）。修法：在 Addressables 里请求发出后立刻取结果（`AssetBundleCreateRequest.assetBundle` / `AssetBundleRequest.asset`），让 Unity 同步完成。LoadDefault 从 77 秒降到 0.8 秒，热启动到标题约 23 秒（之前约 110 秒），内存正常（约 1 GB PSS）。代价：游戏中途加载大资源时可能顿一下，所以做成设置项（资源加载：快速/兼容）。
- 帧率：游戏会把 targetFrameRate 设成屏幕刷新率（Thor 120 Hz），改成走启动页的上限（默认 60）。
- 渲染分辨率：设置 URP 管线的 renderScale。
- 日志：游戏日志恢复成游戏自己的开关（正式版是关的），以前启动时会刷几千行；我们自己的诊断走 logcat 标签 `Princess`。开发者日志打开后每 5 秒打一次帧率和最慢一帧（`fps: …`），Thor 上测性能用。
- `Application.backgroundLoadingPriority = High`（影响不大，保留）。

### 应用图标与名称（18:30）
- 图标取自游戏 exe 自带的 256 px 图标（7-Zip 解出 `.rsrc/ICON/9.ico`，原件存 `art/game-icon-256.ico`），`tools/pctest/Icon.java` 生成 `art/icon/mipmap-*`：旧式圆角方形和圆形，以及自适应图标的前景层（满幅插画缩放到 108 dp 的 0.70，刚好盖住可见区域）和背景层（放大的插画）。`build-apk.sh` 用它们替换 Unity 默认图标。
- 应用名改为资源字符串 `princess_label`（`art/label`）：系统中文时显示「梦幻魔法公主」，否则 Magical Princess。`aapt2 dump badging` 确认图标和中文名称已打进包，还没在真机桌面上看过。
- 游戏自带「画质」三档设置（`UI_CONFIG_QUALITY_SET0..2`、`ApplyConfigQuality`），启动页不要重复做画质档位。

### 设置项：后处理、离线模式；贴图精度不做（18:45）
- **画面后处理**（`post`，默认开）：`Bridge.NoPost` 挂在 `RenderPipelineManager.beginCameraRendering` 上，每个相机开始渲染时把 URP 相机数据的 `renderPostProcessing` 关掉（反射，类型和属性只找一次），后建的相机、游戏脚本再打开的也覆盖。`princess-debug.txt` 的 `nopost` 走同一条路。模拟器上：关掉后画面方向和颜色都正常；标题画面开关前后几乎一样（`pctest/ImgDiff`：平均差 1.1/255，没有像素差超过 8），这游戏的后处理很轻，省多少 GPU 要上 Thor 看。
- **Steam 连接 / 离线模式**（`offline`，默认在线）：启动页不同步、不查游戏数据（`goOnline()` 统一入口），「开始游戏」直接进；Steam 条显示「离线模式 · 账号」，云存档条显示「云存档暂停（离线）」；数据没装好时主按钮变「退出离线模式以下载」；「检查游戏数据」被拦下。游戏里 `CloudBridge.onSaved/onGameStarted/onAchievements` 离线时都不动 Steam。切回在线立刻 `goOnline()`。离线时的存档：CloudSync 发现只有本地变了就上传；成就靠读档时 `Bridge.Unlocked` 重新报。模拟器上用假账号验证：启动页和游戏全程 `PrincessCloud` 日志 0 行。
- 设置卡片现在固定高度（屏高 − 210 dp），行在里面滚动，按钮不会被挤出屏幕。
- **贴图精度没做**：`ptool texstat` 抽样（每 5 个 bundle 取 1 个）显示贴图 95% 是没有 mipmap 的未压缩 RGBA32/RGB24 大图（CG、立绘，2048² 一张 16 MB），有 mipmap 的只占约 5%。Unity 的 `globalTextureMipmapLimit` 只作用于有 mipmap 的贴图，所以开关基本无效。真要省，只能在设备端安装时改写 bundle（降分辨率会破坏 Sprite 的像素坐标；压成 ETC2/ASTC 保持尺寸、省 4 倍显存，但要在 Java 里解析序列化对象并写编码器，安装时间也会变长）。目前内存约 1 GB，Thor 内存够，先不做，等 Thor 上真有内存或卡顿问题再说。

### 复查结论

- 成就名：游戏用 `ACV_` + 数字，和桥接一致。成就位号 bug（brotato/活侠传那个）在本项目的 SteamPlay 里已经是修好的版本。
- `882a7ab5…` bundle：只有贴图，没有着色器，原来的「着色器没转换」不是问题。
- 新 APK 和旧数据不匹配：状态文件现在记录数据属于哪个 APK，换了新版本 APK 会要求先更新数据，避免新代码读旧资源。
- 三个 Steam 会话（云同步、时长/成就、游戏数据）各用不同的 login id，不会互相顶掉。
- 卸载 APK 会删掉 `files/aa`（6.7 GB）和外部目录里的存档；存档靠云同步保护，提醒用户别卸载、用覆盖安装。
- 存档写入：改设置后 `v10_configdata.dat`、`v10_devicedata.cfg` 正常写入外部目录，保存钩子触发 `onSaved`（未登录时云同步跳过）。游戏进度存档走同一个 SaveController，没专门走到存档点。

## 目录

- 本目录：`G:\ai\android-recompile\princess-android`（`tools/` 工具和构建脚本，`cloud/` 云存档模块，`shots/` 截图）
- 大文件：`E:\android-recompile-work\princess-android-work`（Unity 6 Editor + Android 模块、stage、il2cpp / il2cpp-x64、data、aa（6.4 GB 转好的 bundle）、apk / apk-x64 / apk-emu）
- `E:\android-recompile-work\princess-android-work`：安装包（`dl`）、ptool 编译输出（`ptool-bin3`）、IL 导出、清点结果
- 模拟器：`H:\android-emu\avd\princess-sim.avd`（bundle 已推进去，应用数据在里面）

## 一次完整重建

```
cd "G:/ai/android-recompile/princess-android/tools"
# 资源（游戏更新后才需要）：
/e/android-recompile-work/princess-android-work/ptool-bin3/ptool.exe rt-data 'H:\SteamLibrary\steamapps\common\Magical Princess\MagicalPrincess_Data' 'E:\android-recompile-work\princess-android-work\data' 'E:\android-recompile-work\princess-android-work\shtmp' 'E:\lom-android-work\hlslcc-build\hlslcc-cli.exe' ptool/classdata.tpk
/e/android-recompile-work/princess-android-work/ptool-bin3/ptool.exe rt-bundles '…\StreamingAssets\aa\StandaloneWindows64' 'E:\android-recompile-work\princess-android-work\aa' 'E:\android-recompile-work\princess-android-work\shtmp-b' 'E:\lom-android-work\hlslcc-build\hlslcc-cli.exe' ptool/classdata.tpk 8
# 移植包描述和着色器包（游戏版本变了才需要；也会复制 299 个着色器包）：
bash make-port.sh
# 代码 + 打包：
bash rebuild.sh                       # Thor 用 arm64 → E:\android-recompile-work\princess-android-work\apk
ABI=x86_64 INSTALL=emulator-5660 bash rebuild.sh   # 模拟器测试包
bash push-bundles.sh emulator-5660    # 开发用：直接推转好的 bundle（可续传）
bash push-src.sh emulator-5660        # 开发用：推原版 bundle，配合 am start ... --es localSource /sdcard/Android/data/dev.kmprincess.magical/files/mp-src 测设备端安装
# PC 测试（tools/pctest）：StampCheck（转换器）、DepotTest（匿名下载）、ManifestDiff / PortVerify（版本对比）
```

## 触屏适配（11-06 20:50）

游戏本来就能纯鼠标玩，Unity 把第一根手指当鼠标左键，所以点按一直能用。补的是手指自己做不到的几件事，代码在 `tools/princessandroid/TouchAssist.cs`，`Stage.cs` 把游戏里几处读鼠标的调用改成走它（编译时会打印 `touch hooks 1+3 pointer, 1 gallery scroll, 1 battle area`，数不对会直接报错）：

| 改动 | 原来 | 现在 |
|---|---|---|
| 返回 | 只能鼠标右键 / ESC / 手柄 B；安卓返回键（= ESC）本来就能用 | 另加**双指轻点 = 返回**（两指同时点下、0.6 秒内抬起、没怎么移动）。第二根手指落下时暂停 UI 输入模块，第一根手指按住的按钮不会在抬起时被点中 |
| 点同一位置被吞 | 游戏要鼠标移动超过 0.5 像素才承认“鼠标在用”；用过手柄/按键后，点和上次完全同一位置会被吞一次 | 新按下的手指把读到的鼠标位置挪 1 像素，总算作移动 |
| 画廊长图 | `GalleryViewer.Update` 只认滚轮 | 单指上下拖动换算成滚轮量（按 1080 高画布，拖多少移多少） |
| 战斗选区 | 区域跟着鼠标走，按下左键立刻确定 | 有触摸时改成：手指按住拖着瞄准，**松手才确定**；只认区域出现之后才按下的手指，打开选区那一下的松手不算 |

模拟器实测（princess-sim，`tools/touch-gen.js` 生成 uinput 脚本造多点触屏，见文件头注释）：
- 设置页双指点面板空白处 → 回主菜单；主菜单双指点、其中一指正按在 Gallery 上 → 回标题，画廊没被打开。
- 单指点设置面板外面不会关页面（排除误判）。
- 用安卓返回键（键盘输入，会让游戏退出鼠标模式）后，再点同一位置的 Settings，一次就打开。
- **未实测**：画廊长图拖动（新存档画廊全锁）、战斗选区（要玩到战斗）。两处都靠读 IL 写的，Thor 上第一次遇到要看一眼。

其他没改：按键提示仍显示键盘图标（回车/ESC）；设置里的 ◀ ▶ 小箭头在 6 寸屏上偏小。

## Steam 成就弹窗（11-07 15:40）

- 新文件 `cloud/.../AchievementToast.java`：Steam 确认解锁后（`SteamPlay.flushAchievements` 写入成功），在右下角弹一张类似 Steam 桌面版的卡片：成就图标 + 「成就已解锁」+ 名称 + 描述，从屏幕下沿滑入，停 4.5 秒，再原路滑出。
- 名称和描述取自 Steam 发来的成就模式（`getUserStats` 附带的 schema，`stats/<id>/bits/<n>/display/name|desc|icon`），按系统语言选 schinese / tchinese / japanese / koreana / english。图标从 `cdn.cloudflare.steamstatic.com/steamcommunity/public/images/apps/3562120/<hash>` 下载，缓存在 `cache/achievement-icons`；下载失败显示粉紫渐变星形占位。
- 一次解锁多个（首次登录补报已有成就）：最多 3 张卡片，其余合成一张「另有 N 个成就已解锁 · 已同步到你的 Steam 账号」。
- 用独立 PopupWindow（不可聚焦、不可触摸，不影响手柄和触屏）。最早版本把卡片直接加进游戏窗口 decorView，后来改成独立窗口，免得动画期间整个游戏窗口跟着重绘。
- 只在 Steam 真正写入成功时弹：离线模式、Steam 上早已解锁的都不弹。游戏自己也有成就提示（`GameController.ShowAchievementGet`，游戏内成就点数系统），两者会同时出现，PC 上开 Steam 覆盖层也是这样。
- 挂接点：`CloudBridge.onGameStarted` 和 `LauncherActivity.onCreate` 调 `AchievementToast.attach(activity)`。
- 测试钩子（不用 Steam 账号）：`run-as dev.kmprincess.magical` 写 `files/achievement-test.txt`，每行 `名称<TAB>描述<TAB>图标hash`，下次进启动页或游戏时弹出并删除该文件。
- 模拟器结果：游戏内（Unity 画面上）卡片和图标显示正常（`shots/achievement-ingame.png`，这是缩小前的版本）；在启动页上测了 5 条：3 张卡片 + 1 张「+2」汇总，间隔约 5 秒，图标 404 时显示星形（`shots/achievement-card.png`、`achievement-summary.png`）。
- 模拟器的坑：princess-sim 进游戏约 30 秒后整个系统会卡住（graphics allocator 和 systemui 占满 CPU、system_server 出 watchdog 警告、screencap 超时）。不弹卡片的基线测试也一样，是模拟器本身的问题，不是卡片造成的，所以多张卡片的顺序是在启动页上测的。
- **没验证**：真实 Steam 解锁时弹出（要扫码登录）；Thor 上的观感。arm64 包 `E:\android-recompile-work\princess-android-work\apk\princess-android.apk`（11-07 15:42）已含此功能，**11-07 15:47 已装 Thor**（用户同意），待实测。

## 城镇广场缺建筑和地面（11-07 21:00）

- 现象（用户在 Thor 上报告）：学园广场（LocationMainField）只剩灰天空、云和小人，建筑、雕像、地面全没有，下半截发黑；和弹窗无关，模拟器上用同一个存档也一样。
- 原因：广场的建筑和地面是 3D 网格，材质用 `Universal Render Pipeline/Lit`。D3D 版按 `MAX_VISIBLE_LIGHTS = 256` 编译，HLSLcc 转出来的 GLSL 里是普通 uniform：`_AdditionalLights{Position,Color,Attenuation,SpotDir}[256]`、`hlslcc_mtx4x4_AdditionalLightsWorldToShadow[1024]` 等，基础变体就要约 1050 个 vec4，超过 GPU 上限，链接失败（模拟器日志：`GLSL link error ... Constant register limit exceeded ... more than 1024 registers`），整个材质不画。Unity 的日志在模拟器上能看到（logcat 标签 Unity），Thor 上没有。
- 修法（`ptool` 的 `Fixups6.ClampLights`）：GLSL 里名字以 `_Additional` 开头、长度 256 的数组改成 32，对应的 `hlslcc_mtx4x4_Additional*[1024]` 改成 128，参数记录里的 ArraySize 同步改成 32（`Convert6.Build`）。URP 安卓版自己就是 32（C# 端在安卓上最多也只传 32 个）。基础变体降到约 160 个 vec4。
- 还没解决的隐患：带附加光阴影 / 灯光 cookie 的变体裁完仍有 330–410 个 vec4（报告里记为 `N uniform vectors`，播放器数据 63 个、着色器包 1504 个程序）。Adreno 的默认 uniform 上限大概是 256，这些变体在 Thor 上可能仍然链接失败。如果 Thor 上还有 3D 物体缺失，下一步是给这些程序开 HLSLcc 的 UBO（`HLSLCC_FLAG_UNIFORM_BUFFER_OBJECT = 0x1`，现有的 `0x8000 WRAP_UBO` 已经会包 `#if HLSLCC_ENABLE_UNIFORM_BUFFERS`），并在 `Build` 里为每个 cbuffer 写一组参数和 type=1 的绑定。
- 重新转换：`rt-data` 的输出现在就是 `data\`（旧的改名为 `data-old-1107`），`light-clamps=100 over-budget=63`，失败数仍是原来那 1 个 UberPost 变体；299 个着色器包从 Steam 原版重转（其中 15 个含光源数组），放进 `port\shaders`（旧的改名为 `port-old-1107`），也写回了 `aa\`（make-port.sh 从这里取）。Thor 上确认正常后，两个 `-old-1107` 目录可以删。
- 设备端：装新 APK 不会重新下载数据，安装状态只认 Windows 原版的 SHA-1，所以 APK 里换了着色器包，设备不会知道。现在 `port.txt` 多了一行 `shaderset <sha1>`（make-port.sh 生成），设备上记录在 `files/aa/.princess-shaders`；不一致时，点「开始游戏」会先显示「正在更新着色器…」，把 APK 里的着色器包拷过去（`GameData.refreshShaders`），已经被 Steam 新版替换的包不动。模拟器上实测：`shader bundles refreshed from the APK: 299`。
- 整理时留下的坑（已修）：顶层 `shader-report-bundles.txt` 是 E 盘旧的那份（只列 1 个包），make-port.sh 用它会漏掉 298 个着色器包。完整的新报告现在在顶层，旧的那份移到 `from-e\shader-report-bundles.stale.txt`。
- 模拟器跑 arm64 包要用 `EMU=1` 打包（把 SEVL/WFE 换成 NOP），否则加载线程会 SIGILL；这个包绝不能装真机。
- 新 arm64 正式包：`E:\android-recompile-work\princess-android-work\apk\princess-android.apk`（11-07 22:41），**11-07 23:47 已装 Thor**（用户同意），待实测。对比截图 `shots/field-broken.png`（修复前，模拟器）、`shots/field-fixed.png`（修复后）。

## 进行中：材质关键字效果不生效（11-08 凌晨，未修完）

用户在 Thor 上报了三个现象：夜晚中央广场天空发白（应为深蓝夜空）、少了绿色萤火虫、过场漫画格的背景不变灰变暗。模拟器上用 Thor 存档（`读取游戏` → 第 1 格，夜晚帕奈特利亚中央广场）都能复现。

已经确认的：
- 天空背景是 `FieldSky.bgSprite`，材质 `LocationSkyBackGround`（包 `5e3156d4743423312d59fdce1b9a5fb7`，AllIn1SpriteShader，`m_ValidKeywords = GRADIENT2COL_ON, GRADIENT_ON`）。该着色器有 4 个变体：`[]`→无渐变程序，`[17,18]`→渐变程序，`[12]`、`[12,17,18]`（12=FOG_EXP2）同理。
- 把无渐变程序改成输出纯红，天空变红；把两个程序对调，夜空变成正确的深蓝渐变，顶部 UI 也不再发白。**结论：渐变程序本身没问题，是 Unity 选变体时没算材质上的关键字。**
- 转换前后对比过：变体关键字列表、`m_SerializedKeywordStateMask`（0–76 全部）、SubShader/Pass、`m_KeywordNames`（77 个）、`m_KeywordFlags`（都为空）完全一致。和安卓自带着色器（`unity default resources`）比，子程序记录字段也一样，只是 `m_ShaderRequirements` 不同（我们 33，自带 1）。
- 不是天空盒（`Skybox/Procedural` 改红后没变化），也不是雾。
- 过场漫画格变灰/变暗走 SSU 的关键字效果，萤火虫多半也是关键字或粒子着色器问题，推测是同一个根因，未逐个验证。

调试手段（都还在）：
- `ptool shdump` 现在会打印关键字名、所有 SubShader/Pass 的变体列表、`m_KeywordFlags`。
- `ptool matfind <bundle 目录> <tpk> <属性名> [线程]`：找带某属性的材质，打印材质名、关键字和引用的着色器。扫全部 22853 个原版包约 5 分半。
- 临时调试版 ptool（源码里已撤掉）用环境变量 `PTOOL_DEBUG_RED` / `PTOOL_DEBUG_RED_NOT`（GLSL 含/不含某串的程序输出红色）和 `PTOOL_DEBUG_SWAP`（对调两个程序）。要用时照 HANDOFF 这段在 `Convert6.Finish` 里临时加回。
- 单包快速试验：从 Steam 原版拷一个包到临时目录，`rt-bundles` 只转它，`adb push` 到 `/data/local/tmp`，再 `run-as dev.kmprincess.magical` 覆盖 `files/aa/<包>`（先留 `.orig` 备份），重启游戏读档。模拟器上原包已全部还原（`files/aa/*.orig` 为 0 个）。
- `tools/princessandroid/Diag.cs`：调试开关里加 `diag` 后，往 `persistentDataPath/diag-now` 放文件，就把场景里所有渲染器的材质、着色器、材质要求的关键字和 Unity 实际解析出的关键字打到 logcat（标签 Princess）。已编译通过，**还没在设备上跑过**；下一步就是用它看 `LocationSkyBackGround` 运行时的关键字状态，再决定修法。
- 模拟器包是 `EMU=1` 的 arm64 包（打包输出放 C 盘临时目录 3 分钟左右；E 盘要 40 分钟）。
