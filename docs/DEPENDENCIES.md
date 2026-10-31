# Required dependencies / 必需依赖

This bridge build requires the original Koikatu or Koikatsu Party (After Party DLC and official patch), BepInEx 5, and the official **KK** Modding API 1.48.1 or newer. Install the dependencies before the bridge.

本构建需要日版 Koikatu 或 Koikatsu Party（After Party DLC 及官网补丁）、BepInEx 5 和官方 **KK** Modding API 1.48.1 或更新版本。请先安装依赖，再安装 Bridge。

1. Install [BepisPlugins](https://github.com/IllusionMods/BepisPlugins/releases) for Koikatu/Koikatsu Party, including ExtensibleSaveFormat 19.4.0 or newer, which KKAPI requires.
2. Install [KK_ModdingAPI](https://github.com/IllusionMods/IllusionModdingAPI/releases/tag/v1.48.1). Use the **KK** archive, not KKS, AI, HS2, EC or PH. Extract into the game directory, preserving its folders; keep one `KKAPI.dll` under `BepInEx/plugins`.
3. Put `KK_StoryNPCBridge.dll` in `BepInEx/plugins` or extract the bridge installation ZIP into the game directory.

1. 安装适用于 Koikatu/Koikatsu Party 的 [BepisPlugins](https://github.com/IllusionMods/BepisPlugins/releases)，包含 KKAPI 必需的 ExtensibleSaveFormat 19.4.0 或更新版本。
2. 安装 [KK_ModdingAPI](https://github.com/IllusionMods/IllusionModdingAPI/releases/tag/v1.48.1)，选择 **KK** 安装包，不要使用 KKS、AI、HS2、EC 或 PH 版。保留目录结构解压到游戏目录，确保 `BepInEx/plugins` 下只有一份 `KKAPI.dll`。
3. 将 `KK_StoryNPCBridge.dll` 放入 `BepInEx/plugins`，或将 Bridge 安装 ZIP 解压到游戏目录。

BepInEx enforces the hard dependency `marco.kkapi >= 1.48.1`, loading KKAPI before the bridge. Missing or outdated KKAPI prevents the bridge from loading; check `BepInEx/LogOutput.log`. KKAPI must also load its own required dependencies successfully. CheatTools is not required.

BepInEx 根据硬依赖 `marco.kkapi >= 1.48.1` 先加载 KKAPI。KKAPI 缺失或版本过低时不会加载 Bridge，请查看 `BepInEx/LogOutput.log`。KKAPI 自身的必需依赖也须加载成功。CheatTools 非必需。

This ZIP contains only the bridge DLL, its MIT license and these instructions. KKAPI, BepisPlugins, BepInEx, game DLLs and compile-time references are not bundled; obtain them from their official distributions. They retain their own licenses.

本 ZIP 仅包含 Bridge DLL、MIT 许可证和本说明；不附带 KKAPI、BepisPlugins、BepInEx、游戏 DLL 或编译引用，请从其官方渠道获取。这些依赖保留各自的许可证。
