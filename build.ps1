param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist'))
$ErrorActionPreference = 'Stop'
$agCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $agCompiler)) {
    $agCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $agCompiler)) { throw 'The .NET Framework C# compiler was not found.' }
$agSource = Join-Path $PSScriptRoot 'src'
$agDestination = Join-Path $OutputDirectory 'Antigravity中文伴侣'
New-Item -ItemType Directory -Path $agDestination -Force | Out-Null
& $agCompiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll "/resource:$agSource\dictionary.json,dictionary.json" "/resource:$agSource\ui-translator.js,ui-translator.js" "/out:$agDestination\Antigravity中文伴侣.exe" "$agSource\ChineseCompanion.cs"
if ($LASTEXITCODE -ne 0) { throw 'Companion compilation failed.' }
& $agCompiler /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 "/out:$agDestination\恢复英文并退出.exe" "$agSource\StopCompanion.cs"
if ($LASTEXITCODE -ne 0) { throw 'Restore tool compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs/使用说明.md') -Destination (Join-Path $agDestination '使用说明.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'LICENSE') -Destination $agDestination
Write-Output $agDestination
