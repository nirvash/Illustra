# GitHub Personal Access Tokenを環境変数に設定
$env:GITHUB_TOKEN = "<your_personal_access_token>"

# 最新のDockerイメージを使用してactを実行
act push -P windows-latest=ghcr.io/catthehacker/ubuntu:act-latest