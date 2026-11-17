# Rebuilds ispc_texcomp.dll (Intel's ISPC Texture Compressor, MIT) next to this script: the BC1 encoder of the
# textures (src\OrthoStreamXP\Textures\Dds\IspcBc1.cs). Needs the Visual Studio C++ build tools (x64); downloads the
# pinned compressor sources and ISPC compiler into a temporary folder.
#
#   .\native\ispc_texcomp\build.ps1
$ErrorActionPreference = 'Stop'

$sourceCommit = '79ddbc90334fc31edd438e68ccb0fe99b4e15aab' # GameTechDev/ISPCTextureCompressor, 2024-09-23
$ispcVersion = 'v1.31.0'
# Chosen at run time by the CPU; AVX-512 is about a fifth faster than AVX2 where the CPU has it.
$targets = 'sse2,sse4,avx,avx2,avx512skx-x16'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw 'Visual Studio C++ build tools (x64) not found' }
$vcvars = Join-Path $vs 'VC\Auxiliary\Build\vcvars64.bat'

$work = Join-Path ([IO.Path]::GetTempPath()) "ispc_texcomp-build-$PID"
New-Item -ItemType Directory -Force $work | Out-Null
try {
    Invoke-WebRequest "https://github.com/GameTechDev/ISPCTextureCompressor/archive/$sourceCommit.zip" -OutFile "$work\src.zip"
    Invoke-WebRequest "https://github.com/ispc/ispc/releases/download/$ispcVersion/ispc-$ispcVersion-windows.zip" -OutFile "$work\ispc.zip"
    Expand-Archive "$work\src.zip" "$work\src"
    Expand-Archive "$work\ispc.zip" "$work\ispc"
    $src = Join-Path $work "src\ISPCTextureCompressor-$sourceCommit\ispc_texcomp"
    $ispc = (Get-ChildItem "$work\ispc" -Recurse -Filter ispc.exe | Select-Object -First 1).FullName
    $out = Join-Path $work 'out'
    New-Item -ItemType Directory -Force $out | Out-Null

    # The flags of Intel's own project (ispc_texcomp.vcxproj), with the AVX-512 target added. /MT: the C runtime is
    # inside the DLL, so it needs nothing but KERNEL32 (no Visual C++ redistributable on users' PCs).
    $script = @"
@echo off
call "$vcvars" >nul || exit /b 1
cd /d "$out" || exit /b 1
"$ispc" -O2 "$src\kernel.ispc" -o kernel.obj -h kernel_ispc.h --target=$targets --opt=fast-math --woff || exit /b 1
"$ispc" -O2 "$src\kernel_astc.ispc" -o kernel_astc.obj -h kernel_astc_ispc.h --target=$targets --opt=fast-math --woff || exit /b 1
cl /nologo /O2 /EHsc /MT /LD /I. "$src\ispc_texcomp.cpp" "$src\ispc_texcomp_astc.cpp" kernel*.obj /Fe:ispc_texcomp.dll /link /DEF:"$src\ispc_texcomp.def" || exit /b 1
"@
    Set-Content "$work\build.bat" $script -Encoding ascii
    # stderr merged inside cmd: vcvars and ISPC write notes there, which Windows PowerShell would raise as errors.
    $ErrorActionPreference = 'Continue'
    cmd /c "`"$work\build.bat`" 2>&1"
    $exit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    if ($exit -ne 0) { throw 'ispc_texcomp build failed' }
    Copy-Item "$out\ispc_texcomp.dll" $PSScriptRoot -Force
    Write-Host "Built $PSScriptRoot\ispc_texcomp.dll"
}
finally {
    Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
