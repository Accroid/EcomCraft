# EcomCraft: one-click setup of the local vision model used by "Follow a reference" (F69).
# Installs Ollama when it is missing (winget, or the official installer after checking its signature), starts it, and pulls the vision model.
# Usage: powershell -ExecutionPolicy Bypass -File launchtools\install-vision-model-windows.ps1 [-Model llama3.2-vision:latest]
param(
    [string]$Model = 'llama3.2-vision:latest'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
# UTF-8 output (without a byte order mark), so the settings page install that reads this output shows the Chinese messages correctly.
try {
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
}
catch {
    # Without a console the default encoding stays; the messages are bilingual anyway.
}

function Say([string]$zh, [string]$en) {
    Write-Host "$zh / $en"
}

function Fail([string]$zh, [string]$en) {
    Write-Host ''
    Write-Host "失败：$zh" -ForegroundColor Red
    Write-Host "Failed: $en" -ForegroundColor Red
    Write-Host '手动步骤 / Manual steps: https://ollama.com/download , then: ollama pull ' $Model
    exit 1
}

# The model name goes to the ollama command line; allow only the characters model names use.
if ($Model -notmatch '^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$') {
    Fail '模型名称不合法。' 'The model name is not valid.'
}

function Find-Ollama {
    $command = Get-Command ollama -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    $default = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
    if (Test-Path $default) {
        return $default
    }
    return $null
}

function Test-OllamaServer {
    try {
        Invoke-WebRequest -Uri 'http://127.0.0.1:11434/' -UseBasicParsing -TimeoutSec 3 | Out-Null
        return $true
    }
    catch {
        return $false
    }
}

# 1. Install Ollama when it is missing.
$ollama = Find-Ollama
if (-not $ollama) {
    Say '没有找到 Ollama，正在安装…' 'Ollama not found, installing...'
    if (Get-Command winget -ErrorAction SilentlyContinue) {
        & winget install --id Ollama.Ollama -e --silent --accept-source-agreements --accept-package-agreements
        if ($LASTEXITCODE -ne 0) {
            Say 'winget 安装没有成功，改用官方安装包。' 'winget did not succeed, using the official installer.'
        }
    }
    $ollama = Find-Ollama
    if (-not $ollama) {
        $installer = Join-Path $env:TEMP 'OllamaSetup.exe'
        try {
            Invoke-WebRequest -Uri 'https://ollama.com/download/OllamaSetup.exe' -OutFile $installer -UseBasicParsing
        }
        catch {
            Fail '下载 Ollama 安装包失败，请检查网络。' 'Downloading the Ollama installer failed; check the network.'
        }
        # Run only an installer with a valid code signature.
        $signature = Get-AuthenticodeSignature $installer
        if ($signature.Status -ne 'Valid') {
            Remove-Item $installer -Force -ErrorAction SilentlyContinue
            Fail '安装包签名无效，已删除，没有运行。' 'The installer signature is not valid; it was deleted without running.'
        }
        Say "安装包签名：$($signature.SignerCertificate.Subject)" "Installer signed by: $($signature.SignerCertificate.Subject)"
        $process = Start-Process -FilePath $installer -ArgumentList '/VERYSILENT', '/NORESTART', '/SUPPRESSMSGBOXES' -Wait -PassThru
        Remove-Item $installer -Force -ErrorAction SilentlyContinue
        if ($process.ExitCode -ne 0) {
            Fail "Ollama 安装程序返回错误（$($process.ExitCode)）。" "The Ollama installer returned an error ($($process.ExitCode))."
        }
        $ollama = Find-Ollama
    }
    if (-not $ollama) {
        Fail '安装后仍找不到 Ollama，请重新打开命令行再运行本脚本。' 'Ollama is still missing after installing; open a new terminal and run this script again.'
    }
    Say 'Ollama 已安装。' 'Ollama installed.'
}
else {
    Say "已找到 Ollama：$ollama" "Found Ollama: $ollama"
}

# 2. Make sure the Ollama service answers.
if (-not (Test-OllamaServer)) {
    Say '正在启动 Ollama 服务…' 'Starting the Ollama service...'
    Start-Process -FilePath $ollama -ArgumentList 'serve' -WindowStyle Hidden | Out-Null
    $ready = $false
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Seconds 1
        if (Test-OllamaServer) {
            $ready = $true
            break
        }
    }
    if (-not $ready) {
        Fail 'Ollama 服务 30 秒内没有启动。' 'The Ollama service did not start within 30 seconds.'
    }
}

# 3. Pull the vision model.
Say "正在下载视觉模型 $Model（较大，需要一些时间）…" "Pulling the vision model $Model (large, this takes a while)..."
& $ollama pull $Model
if ($LASTEXITCODE -ne 0) {
    Fail "下载视觉模型 $Model 失败，请检查网络和模型名称。" "Pulling the vision model $Model failed; check the network and the model name."
}

Write-Host ''
Write-Host '完成。回到 EcomCraft 的“照参考图做”，点“重新检查”。' -ForegroundColor Green
Write-Host 'Done. Go back to "Follow a reference" in EcomCraft and click "Check again".' -ForegroundColor Green
