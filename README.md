# 轻读 QingDu

Windows 选区朗读工具，支持 MiMo 和 Fish Audio 云端语音合成。Cloud text-to-speech reader for Windows, with configurable global shortcuts and streaming playback.

## 安装与使用

下载发行页的 `QingDu-v0.2.6-windows-x64.zip`，完整解压后运行 `Install.cmd`。程序需要 Windows 10/11 x64、.NET Framework 4.7.2 或更新版本，以及可访问所选云服务的网络。运行包已包含音频组件，无需 Python、CUDA 或模型文件。安装程序无需管理员权限，安装到当前用户的 `%LOCALAPPDATA%\Programs\QingDu`。

打开“服务设置”，在本机填写自己的 API Key。默认 MiMo／冰糖，MiMo 提供默认音色、冰糖、茉莉、苏打、白桦、Mia、Chloe、Milo、Dean 共9个预设项。Fish 默认使用免费模型，音色列表为空，请添加官方音色页面链接或 ID。

在其他应用选中文字后按 **Ctrl+`** 朗读；**Ctrl+Alt+P** 暂停／继续，**Ctrl+Alt+S** 停止。三个快捷键均可在窗口独立修改，录制时按 Esc 取消。重复或被其他程序占用的绑定会被拒绝并保留原绑定。关闭窗口后程序驻留托盘；退出请用托盘菜单或窗口“退出”。受保护文本或更高权限应用中的选区可能需要手动粘贴。

默认语速1.00、音调0.0、音量100%，两个服务启动缓冲均为350毫秒，媒体降音关闭，MiMo风格指令为空。语速、音调、音色在下次朗读生效，音量可即时调整。暂停保留当前播放队列，不重新合成；后台生成可继续至队列上限。停止取消当前请求及后续分段。

MiMo 按完整句组划分，目标140字、上限220字，连续提交与播放，不添加固定静音，不在失败后自动重读。MiMo单次输入最多3000字，Fish最多20000字。实际延迟取决于网络、服务负载及缓冲；后台待机不会预热模型或占用GPU。变速／变调使用CPU音频处理；MiMo在1倍速、0半音时直接播放原PCM。

## 费用与数据

点击朗读会将该次文本发送至所选服务。Fish免费档政策可能包含内容留存及用于模型改进，请先阅读厂商条款。本版本免费档截至2026-11-30，之后会拒绝发送免费档请求，需更新应用或用户主动选择付费档。程序不自动充值、不在失败时切换服务或付费模型。价格和权限以官方实时信息为准。

API Key分别用Windows当前用户DPAPI加密，保存在 `%LOCALAPPDATA%\QingDu`。该目录也保存设置、运行日志及私有升级备份；请勿上传。加密文件不能作为跨电脑可移植凭证。日志只记录状态、字数、时间及结束原因，不记录原文或Key。程序无自建中转服务器、遥测或自动更新。

升级会迁移旧版Key并验证可解密，备份旧设置及个人音色列表；本机本次迁移按发行默认值重置其他参数。安装器只清理轻读安装目录内的旧本地后端、运行环境及参考音色，不处理外部模型缓存。卸载入口位于开始菜单，默认保留用户数据；`Uninstall.ps1 -RemoveUserData` 可明确删除用户数据。

## 构建与测试

`VERSION` 是唯一版本来源。Windows PowerShell执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1
```

音频组件使用官方原始源码，通过 MSYS2 MINGW64 构建。安装构建工具 `make mingw-w64-x86_64-gcc mingw-w64-x86_64-meson mingw-w64-x86_64-pkgconf mingw-w64-x86_64-nasm` 后，在MSYS2 bash执行：

```bash
bash scripts/build-media.sh "$PWD/build/media-work"
```

再执行 `scripts/Test.ps1` 和 `scripts/Package.ps1`。测试只用合成音频、虚拟Key和本机模拟HTTP接口，不需要云账号；DPAPI测试需正常加载Windows用户配置文件。构建工具中的Python仅由Meson使用，不是应用依赖。测试音频 `tests/tone.mp3` 是2秒440Hz合成正弦波，无语音内容，按CC0提供。

## 许可和版本

项目采用GPL-3.0，完整许可见LICENSE。随发行交付FFmpeg和Rubber Band的对应源码、构建脚本与第三方声明。未进行代码签名。验证范围见 `VALIDATION.md`。

版本采用SemVer：主版本表示不兼容变更，次版本表示兼容新功能，修订号表示兼容修复；alpha、beta、rc为预发布标识。0.2.5完成开发阶段的纯云端调整，0.2.6修复跨系统编码下的音频管道问题。后续小修复使用0.2.7，较大新增功能使用0.3.0，功能与兼容约定稳定后使用1.0.0。已公开版本不替换内容。规范：https://semver.org/lang/zh-CN/
