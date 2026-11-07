# 野火音频编辑

Windows 本地音频编辑工具，当前版本 **2.0.2 GG 小程序卡密版**。支持批量变速、变调、试听及 WAV、MP3、OGG、M4A 导出，同名歌词随结果复制，原音频保持不变。

## 工程内容

- `Program.cs`、`MainForm.cs`、`Dialogs.cs`：程序入口、界面和批处理。
- `AudioEngine.cs`：音频处理、试听、格式导出和元数据。
- `OnlineLicense.cs`、`GgSafeStorage.cs`、`LicenseDialog.cs`：GG 授权和现有设备身份兼容。
- `stage/`：运行依赖、用户说明和第三方许可；主程序在本地编译生成。
- `tests/`、`SelfTest.cs`：授权测试与独立音频回归，测试输出不提交。
- `setup.iss`：安装包脚本；`GG后台待配置说明.md`：后台待配置项。

## 编译与运行

需要 Windows 10/11 和系统 .NET Framework 4.x 编译器。源码使用 C# 和 WinForms，编译为 x86，保持与音频库一致。

在工程文件夹打开 PowerShell：

```powershell
.\build.ps1
& '.\stage\野火音频编辑.exe'
```

`stage` 文件夹中的依赖需要保留，不能只复制主程序 EXE。

## 生成安装包

安装 Inno Setup 后，在工程根目录执行（按实际安装位置调整）：

```powershell
& 'C:\Program Files\Inno Setup 7\ISCC.exe' '.\setup.iss'
```

安装包写入 `artifacts/`，该文件夹不提交到 Git。

## 测试

```powershell
.\tests\run.ps1
```

独立授权测试采用临时设备身份和模拟服务，不使用个人卡密。`tests/verify-protocol.mjs` 可用 Node.js 配合单独的卡密接入工具包交叉验签；第一个参数为工具包中 `源码/licenseCore.js` 的本机路径，该云端源码不包含在本仓库中。

音频回归在 `tests/AudioTests.cs` 和 `SelfTest.cs` 中，生产程序不包含自测命令入口。历史验证范围见 `接入与验证说明.md`。

## GG 授权

沿用产品 `yuansheng-workshop-v7` 和 `GG-XXXX-XXXX-XXXX` 格式，与现有野火音乐台共用当前 Windows 用户的设备身份。授权期限由服务端返回，客户端不自行生成或延长授权。

客户端支持体验卡、日卡、月卡、年卡、永久卡。体验卡要求首次激活起 24 小时；体验卡与日卡的后台配置尚未部署，见 `GG后台待配置说明.md`。现有 GG 永久卡已完成真实云端校验。

本仓库不保存个人卡密、设备私钥、账号资料、设置、日志、音频和测试生成文件。第三方运行组件及其许可保存在 `stage/Licenses/`；其各自使用条件仍然适用。
