# Client B 배포 스크립트
# 사용법: PowerShell에서 .\deploy.ps1 실행

Write-Host "=== Client B 배포 스크립트 ===" -ForegroundColor Green

# 배포 폴더 경로
$deployPath = ".\Deploy"
$projectPath = ".\ClientInternalPC"
$csprojFile = "$projectPath\ClientInternalPC.csproj"

# 기존 배포 폴더 삭제
if (Test-Path $deployPath) {
    Write-Host "기존 배포 폴더 삭제 중..." -ForegroundColor Yellow
    Remove-Item $deployPath -Recurse -Force
}

# 배포 폴더 생성
New-Item -ItemType Directory -Path $deployPath | Out-Null
Write-Host "배포 폴더 생성: $deployPath" -ForegroundColor Green

# MSBuild 경로 찾기
$msbuildPath = ""
$vsPaths = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2019\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2017\Community\MSBuild\15.0\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2017\Professional\MSBuild\15.0\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2017\Enterprise\MSBuild\15.0\Bin\MSBuild.exe"
)

foreach ($path in $vsPaths) {
    if (Test-Path $path) {
        $msbuildPath = $path
        break
    }
}

# MSBuild를 찾지 못한 경우
if (-not $msbuildPath) {
    # PATH에서 찾기
    $msbuildPath = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($msbuildPath) {
        $msbuildPath = $msbuildPath.Source
    }
}

if (-not $msbuildPath -or -not (Test-Path $msbuildPath)) {
    Write-Host "오류: MSBuild를 찾을 수 없습니다." -ForegroundColor Red
    Write-Host "Visual Studio를 설치하거나 MSBuild를 PATH에 추가하세요." -ForegroundColor Red
    exit 1
}

Write-Host "MSBuild 경로: $msbuildPath" -ForegroundColor Cyan

# Release 모드로 빌드
Write-Host "`nRelease 모드로 빌드 중..." -ForegroundColor Yellow
$buildArgs = @(
    $csprojFile,
    "/p:Configuration=Release",
    "/p:Platform=AnyCPU",
    "/t:Rebuild",
    "/v:minimal"
)

$buildResult = & $msbuildPath $buildArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "빌드 실패!" -ForegroundColor Red
    exit 1
}

Write-Host "빌드 완료!" -ForegroundColor Green

# 빌드 출력 폴더
$releasePath = "$projectPath\bin\Release"

if (-not (Test-Path $releasePath)) {
    Write-Host "오류: 빌드 출력 폴더를 찾을 수 없습니다: $releasePath" -ForegroundColor Red
    exit 1
}

# 파일 복사
Write-Host "`n배포 파일 복사 중..." -ForegroundColor Yellow

# 필수 파일 복사
$filesToCopy = @(
    "ClientInternalPC.exe",
    "ClientInternalPC.exe.config",
    "Newtonsoft.Json.dll"
)

foreach ($file in $filesToCopy) {
    $sourceFile = Join-Path $releasePath $file
    if (Test-Path $sourceFile) {
        Copy-Item $sourceFile -Destination $deployPath -Force
        Write-Host "  ✓ $file" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $file (찾을 수 없음)" -ForegroundColor Red
    }
}

# 기타 DLL 파일들도 복사 (필요한 경우)
Get-ChildItem -Path $releasePath -Filter "*.dll" | ForEach-Object {
    $destFile = Join-Path $deployPath $_.Name
    if (-not (Test-Path $destFile)) {
        Copy-Item $_.FullName -Destination $deployPath -Force
        Write-Host "  ✓ $($_.Name)" -ForegroundColor Cyan
    }
}

# README 파일 생성
$readmeContent = @"
# Client B - 내부망 에이전트

## 실행 방법

1. `ClientInternalPC.exe` 더블클릭하여 실행
2. 시스템 트레이에 아이콘이 표시됩니다
3. 트레이 아이콘 우클릭 → "시작" 선택

## 설정

`ClientInternalPC.exe.config` 파일을 메모장으로 열어 설정을 수정할 수 있습니다.

## 요구사항

- Windows 7 이상
- .NET Framework 4.5.2 이상

## 문제 해결

프로그램이 실행되지 않으면:
1. .NET Framework 4.5.2 이상이 설치되어 있는지 확인
2. 관리자 권한으로 실행 시도
3. 방화벽에서 Outbound 연결이 허용되는지 확인
"@

$readmePath = Join-Path $deployPath "README.txt"
Set-Content -Path $readmePath -Value $readmeContent -Encoding UTF8
Write-Host "  ✓ README.txt" -ForegroundColor Green

# 완료 메시지
Write-Host "`n=== 배포 완료 ===" -ForegroundColor Green
Write-Host "배포 폴더: $deployPath" -ForegroundColor Cyan
Write-Host "`n배포할 파일들:" -ForegroundColor Yellow
Get-ChildItem -Path $deployPath | ForEach-Object {
    Write-Host "  - $($_.Name)" -ForegroundColor White
}

Write-Host "`n이 폴더의 모든 파일을 사용자 PC에 복사하여 배포하세요." -ForegroundColor Cyan

