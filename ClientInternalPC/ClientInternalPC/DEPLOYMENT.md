# Client B 배포 가이드

## 📦 배포 방법

### 방법 1: Visual Studio에서 Release 빌드 (권장)

1. **Visual Studio에서 프로젝트 열기**
   - `ClientInternalPC.sln` 파일 열기

2. **빌드 구성 변경**
   - 상단 메뉴: `빌드` → `구성 관리자`
   - `활성 솔루션 구성`을 `Debug`에서 `Release`로 변경
   - 확인 클릭

3. **프로젝트 빌드**
   - 상단 메뉴: `빌드` → `솔루션 빌드` (또는 `Ctrl+Shift+B`)
   - 또는 프로젝트 우클릭 → `빌드`

4. **출력 파일 확인**
   - 빌드 완료 후 다음 폴더 확인:
   ```
   ClientInternalPC\bin\Release\
   ```

5. **배포할 파일들**
   - `ClientInternalPC.exe` - 메인 실행 파일
   - `ClientInternalPC.exe.config` - 설정 파일 (App.config가 자동 복사됨)
   - `Newtonsoft.Json.dll` - NuGet 패키지 DLL
   - 기타 필요한 DLL 파일들

### 방법 2: 명령줄에서 빌드

```powershell
# 프로젝트 폴더로 이동
cd ClientInternalPC

# Release 모드로 빌드
msbuild ClientInternalPC.csproj /p:Configuration=Release /p:Platform=AnyCPU
```

빌드된 파일은 `bin\Release\` 폴더에 생성됩니다.

---

## 📁 배포 패키지 구성

### 필수 파일

배포 시 다음 파일들을 함께 배포해야 합니다:

```
배포폴더/
├── ClientInternalPC.exe          (메인 실행 파일)
├── ClientInternalPC.exe.config   (설정 파일)
├── Newtonsoft.Json.dll           (JSON 라이브러리)
└── (기타 필요한 .NET Framework DLL들)
```

### 배포 스크립트 예시

**배포 폴더 자동 생성 스크립트** (`deploy.ps1`):

```powershell
# 배포 폴더 생성
$deployPath = ".\Deploy"
if (Test-Path $deployPath) {
    Remove-Item $deployPath -Recurse -Force
}
New-Item -ItemType Directory -Path $deployPath

# Release 빌드
msbuild ClientInternalPC\ClientInternalPC.csproj /p:Configuration=Release /p:Platform=AnyCPU

# 파일 복사
Copy-Item "ClientInternalPC\bin\Release\*" -Destination $deployPath -Recurse

Write-Host "배포 파일이 $deployPath 폴더에 생성되었습니다."
```

---

## ⚙️ 설정 파일 수정

배포 전에 `App.config` (또는 `ClientInternalPC.exe.config`) 파일을 수정하세요:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
    <startup> 
        <supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.5.2" />
    </startup>
    <appSettings>
        <!-- Relay Server 설정 -->
        <add key="RelayHost" value="your-relay-server.com" />
        <add key="RelayPort" value="8080" />
        <add key="RelayToken" value="your-secure-token" />
        <add key="UseSecure" value="true" />
        
        <!-- 허용된 도메인 목록 -->
        <add key="AllowedDomains" value="internal-api.company.com,internal-service.company.com" />
    </appSettings>
</configuration>
```

---

## 🚀 단일 EXE 파일로 만들기 (선택사항)

여러 DLL 파일을 하나의 EXE로 합치려면 다음 도구를 사용할 수 있습니다:

### 옵션 1: ILMerge (무료)

1. [ILMerge 다운로드](https://www.microsoft.com/en-us/download/details.aspx?id=17630)
2. 명령 실행:
```powershell
ilmerge /out:ClientInternalPC_Merged.exe ClientInternalPC.exe Newtonsoft.Json.dll
```

### 옵션 2: Costura.Fody (NuGet 패키지)

프로젝트에 NuGet 패키지 추가:
```
Install-Package Costura.Fody
```

빌드 시 자동으로 DLL이 EXE에 포함됩니다.

### 옵션 3: .NET Core의 단일 파일 배포 (마이그레이션 필요)

.NET Core/.NET 5+로 마이그레이션하면 단일 파일 배포가 가능합니다.

---

## 📋 배포 체크리스트

- [ ] Release 모드로 빌드 완료
- [ ] `App.config` 파일 설정 확인 (Relay Server 주소, 토큰 등)
- [ ] 필요한 DLL 파일 모두 포함 확인
- [ ] 테스트 PC에서 실행 테스트
- [ ] 방화벽 설정 확인 (WebSocket 연결 허용)
- [ ] .NET Framework 4.5.2 이상 설치 확인

---

## 🔧 실행 환경 요구사항

### 필수 요구사항

- **운영체제**: Windows 7 이상
- **.NET Framework**: 4.5.2 이상
- **네트워크**: Relay Server로의 Outbound 연결 가능

### .NET Framework 확인 방법

```powershell
# PowerShell에서 확인
Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\" | Select-Object Version
```

또는 사용자에게 다음 링크에서 다운로드 안내:
- [.NET Framework 4.5.2 다운로드](https://www.microsoft.com/ko-kr/download/details.aspx?id=42642)

---

## 💡 배포 팁

1. **설정 파일 보호**
   - `App.config`에 민감한 정보(토큰 등)가 있으면 암호화 고려
   - 또는 환경 변수 사용

2. **자동 시작 설정**
   - Windows 시작 프로그램에 추가하려면:
   ```
   %APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup
   ```
   - 또는 작업 스케줄러 사용

3. **Windows Service로 변환** (선택사항)
   - 백그라운드 실행이 필요하면 Windows Service로 변환 가능
   - `Topshelf` NuGet 패키지 사용 권장

---

## 📞 문제 해결

### "DLL을 찾을 수 없습니다" 오류

- `Newtonsoft.Json.dll`이 exe와 같은 폴더에 있는지 확인
- 또는 ILMerge로 단일 파일로 병합

### "애플리케이션을 시작할 수 없습니다" 오류

- .NET Framework 4.5.2 이상 설치 확인
- Visual C++ 재배포 가능 패키지 설치 필요할 수 있음

### WebSocket 연결 실패

- 방화벽에서 Outbound 연결 허용 확인
- Relay Server 주소 및 포트 확인
- `App.config`의 설정 확인

