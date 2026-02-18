# Opus-MT 翻译模型下载脚本
# 保存到项目根目录，右键 PowerShell 运行

$ModelDir = "Models\opus-mt-en-zh"
$BaseUrl = "https://hf-mirror.com/Helsinki-NLP/opus-mt-en-zh/resolve/main"

# 创建目录
if (!(Test-Path $ModelDir)) {
    New-Item -ItemType Directory -Path $ModelDir -Force | Out-Null
}

$Files = @(
    "model.onnx",    # 约90MB
    "vocab.json",    # 约1MB
    "config.json",   # 约1KB
    "source.spm",    # 约1MB
    "target.spm"     # 约1MB
)

Write-Host "开始下载 Opus-MT 翻译模型..." -ForegroundColor Green
Write-Host "模型将保存到: $ModelDir" -ForegroundColor Yellow

foreach ($File in $Files) {
    $Url = "$BaseUrl/$File"
    $Path = Join-Path $ModelDir $File
    
    if (Test-Path $Path) {
        Write-Host "已存在: $File" -ForegroundColor Gray
        continue
    }
    
    Write-Host "正在下载: $File..." -ForegroundColor Cyan -NoNewline
    try {
        Invoke-WebRequest -Uri $Url -OutFile $Path -MaximumRedirection 5
        $Size = (Get-Item $Path).Length / 1MB
        Write-Host " OK ($([math]::Round($Size, 2)) MB)" -ForegroundColor Green
    }
    catch {
        Write-Host " 失败! $_" -ForegroundColor Red
    }
}

Write-Host "`n下载完成! 文件列表:" -ForegroundColor Green
Get-ChildItem $ModelDir | ForEach-Object {
    $Size = $_.Length / 1MB
    Write-Host "  $($_.Name) - $([math]::Round($Size, 2)) MB" -ForegroundColor White
}

pause
