# 启动报错修复与重新发布

## 0.5.5 的首次运行错误

表现：双击后出现 Microsoft .NET 弹窗，提示“系统找不到指定的文件。 (0x80070002)”，托盘或悬浮栏未出现。

原因：创建托盘右键菜单时，程序会读取当前用户的 `CodexMeter.AutoFollow-<SID>` 计划任务，以确定“登录后自动跟随 Codex”是否勾选。开发电脑上已存在这个任务，另一台电脑首次运行时通常不存在。Windows 的 COM 互操作会把 `0x80070002` 映射为 `System.IO.FileNotFoundException`；0.5.5 只捕获了 `COMException`，异常便会中断初始化。

这里缺少的是可选的计划任务，不是发布包里的程序文件。安装 SDK、以管理员身份运行或反复解压都不能修复这段异常处理逻辑。

## 在使用电脑上解决

1. 关闭旧版的错误窗口和 CodexMeter；如果托盘仍在，从右键菜单选择“完全退出”。
2. 将 0.5.9 或更新的 Windows x64 便携包解压到固定目录（已包含 0.5.6 的首次启动修复）。
3. 保持 Codex 桌面窗口打开，运行新目录的 `dist\CodexMeter.exe`。约两秒后初始化托盘和悬浮栏；切到其他应用时悬浮栏会隐藏。
4. 自动跟随属于可选功能，不需要先启用它才能运行程序。需要登录后跟随时，再在托盘菜单中勾选，或运行新目录的 `scripts\Enable-AutoFollow.ps1`。

如果旧版本已经注册过自动启动任务，升级或移动目录后，使用新目录的启用脚本重新注册，以更新任务中的 exe 路径。偏好及本机校准数据保存在本地应用数据目录，无需删除。

如果弹窗明确要求安装 .NET，则另行安装 **.NET 8 Desktop Runtime x64**：

- 官方下载页面：<https://dotnet.microsoft.com/download/dotnet/8.0>
- `dotnet --list-runtimes` 应能列出 `Microsoft.WindowsDesktop.App 8.x`。
- 只有 .NET Runtime、ASP.NET Core Runtime 或 x86 运行时不满足本程序的 Windows x64 桌面依赖。

## 在开发电脑上重新发布

使用修复后的完整源码，或在原仓库应用本次补丁后执行以下步骤。需要 .NET 8 SDK；使用者电脑只需要 Desktop Runtime。

```powershell
# 在仓库根目录执行；检查失败会停止打包。
.\scripts\Package.ps1

# SDK 不在 PATH 时可指定路径：
.\scripts\Package.ps1 -DotnetPath '<SDK目录>\dotnet.exe'
```

产物：

- `artifacts\CodexMeter-v0.5.9-win-x64.zip`：使用者下载的便携包。
- `artifacts\CodexMeter-v0.5.9-source.zip`：完整源码及构建、打包脚本，包含首次启动和新版布局修复。
- `artifacts\SHA256SUMS.txt`：两个压缩包的校验值。

检查修改后提交源码，创建 `v0.5.9` 标签和 GitHub Release，附上这三个产物。不要把开发目录、现有 dist、诊断输出或本机设置目录直接压缩上传；打包脚本会从独立构建目录按白名单生成发布文件。

## 发布前验证

- 自动化检查覆盖不存在的任务、两种异常表示、旧启动项回退，以及重复取消任务。权限不足等真正的删除失败仍应报告。
- 在没有 `CodexMeter.AutoFollow-<SID>` 任务的 Windows 用户环境中，保持 Codex 打开并启动程序，确认不再弹出 .NET 错误，托盘及悬浮栏可以使用。
- 运行便携包的 `scripts\Disable-AutoFollow.ps1`，任务已不存在时也应成功。该操作会取消本用户的自动跟随，因此不要在需要保留自动启动的环境中随意测试。
- 可运行 `dist\CodexMeter.exe --probe --online` 进行只读额度诊断；输出的 `probe.json` 仅留在本机，不应作为发布附件。
- 运行 `dist\CodexMeter.exe --preview`，检查合成数据预览和 `layout-preview.json` 中的笔记本、高 DPI、窄窗口布局。`--attachment-check` 在独立测试窗口中验证竖排尺寸、右侧间距、底部锚点、面板非置顶及失焦收起。

0.5.9 使用共享的 .NET 8 桌面运行时，不包含开发电脑的账号、登录凭据或计划任务。
