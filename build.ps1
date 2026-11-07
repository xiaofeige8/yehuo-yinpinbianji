$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /platform:x86 /optimize+ /utf8output /win32icon:app.ico /out:stage/野火音频编辑.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Xml.dll /reference:System.Security.dll /reference:System.Net.Http.dll /reference:System.Web.Extensions.dll /reference:stage/BouncyCastle.Crypto.dll Program.cs MainForm.cs Dialogs.cs AudioEngine.cs OnlineLicense.cs GgSafeStorage.cs LicenseDialog.cs
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Output '编译完成：stage/野火音频编辑.exe'
