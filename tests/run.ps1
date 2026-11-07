$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $project
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$arguments = @('/nologo', '/target:exe', '/platform:x86', '/optimize+', '/utf8output', ('/out:' + (Join-Path $PSScriptRoot 'LicenseTests.exe')), '/reference:System.Core.dll', '/reference:System.Security.dll', '/reference:System.Net.Http.dll', '/reference:System.Web.Extensions.dll', ('/reference:' + (Join-Path $project 'stage\BouncyCastle.Crypto.dll')), (Join-Path $project 'OnlineLicense.cs'), (Join-Path $project 'GgSafeStorage.cs'), (Join-Path $PSScriptRoot 'LicenseTests.cs'))
& $compiler @arguments
if ($LASTEXITCODE -ne 0) { throw 'License test compilation failed' }
Copy-Item -LiteralPath stage/BouncyCastle.Crypto.dll -Destination tests/BouncyCastle.Crypto.dll -Force
& ./tests/LicenseTests.exe tests/protocol-capture.json
if ($LASTEXITCODE -ne 0) { throw 'License tests failed' }
