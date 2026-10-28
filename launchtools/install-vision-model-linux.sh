#!/usr/bin/env bash
# EcomCraft: one-click setup of the local vision model used by "Follow a reference" (F69).
# Installs Ollama with its official install script when it is missing, starts it, and pulls the vision model.
# Usage: bash launchtools/install-vision-model-linux.sh [model]   (default llama3.2-vision:latest)

set -u
MODEL="${1:-llama3.2-vision:latest}"

say() { echo "$1 / $2"; }
fail() {
    echo ""
    echo "失败：$1"
    echo "Failed: $2"
    echo "手动步骤 / Manual steps: https://ollama.com/download , then: ollama pull $MODEL"
    exit 1
}

# The model name goes to the ollama command line; allow only the characters model names use.
if ! [[ "$MODEL" =~ ^[A-Za-z0-9][A-Za-z0-9._:/-]{0,127}$ ]]; then
    fail "模型名称不合法。" "The model name is not valid."
fi

server_up() { curl -fs --max-time 3 http://127.0.0.1:11434/ > /dev/null 2>&1; }

# 1. Install Ollama when it is missing.
if ! command -v ollama > /dev/null 2>&1; then
    if [ "$(uname -s)" = "Darwin" ]; then
        fail "macOS 请从 https://ollama.com/download 下载 Ollama 应用并打开，然后重新运行本脚本。" "On macOS, download and open the Ollama app from https://ollama.com/download, then run this script again."
    fi
    command -v curl > /dev/null 2>&1 || fail "需要 curl，请先安装。" "curl is required; install it first."
    say "没有找到 Ollama，正在用官方安装脚本安装…" "Ollama not found, installing with the official install script..."
    curl -fsSL https://ollama.com/install.sh | sh || fail "Ollama 安装失败，请检查网络和权限（可能需要 sudo）。" "Installing Ollama failed; check the network and permissions (sudo may be needed)."
    command -v ollama > /dev/null 2>&1 || fail "安装后仍找不到 Ollama。" "Ollama is still missing after installing."
    say "Ollama 已安装。" "Ollama installed."
else
    say "已找到 Ollama：$(command -v ollama)" "Found Ollama: $(command -v ollama)"
fi

# 2. Make sure the Ollama service answers.
if ! server_up; then
    say "正在启动 Ollama 服务…" "Starting the Ollama service..."
    nohup ollama serve > /dev/null 2>&1 &
    for _ in $(seq 1 30); do
        sleep 1
        server_up && break
    done
    server_up || fail "Ollama 服务 30 秒内没有启动。" "The Ollama service did not start within 30 seconds."
fi

# 3. Pull the vision model.
say "正在下载视觉模型 $MODEL（较大，需要一些时间）…" "Pulling the vision model $MODEL (large, this takes a while)..."
ollama pull "$MODEL" || fail "下载视觉模型 $MODEL 失败，请检查网络和模型名称。" "Pulling the vision model $MODEL failed; check the network and the model name."

echo ""
echo "完成。回到 EcomCraft 的“照参考图做”，点“重新检查”。"
echo "Done. Go back to \"Follow a reference\" in EcomCraft and click \"Check again\"."
