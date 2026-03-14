<p align="center">
  <img src="Winui/Resources/app.png" width="160" alt="Pelicano mascot" />
</p>

# Pelicano

[Overview](./README.md) · [English](./README.en.md)

Pelicano는 Windows 10/11용 WinUI 3 기반 클립보드 히스토리 앱입니다. 텍스트, 이미지, 파일 복사 기록을 사용자 로컬 영역에 저장하고, 검색, 미리보기, 전역 단축키로 다시 빠르게 꺼내 쓸 수 있습니다.

## 왜 Pelicano인가요?

- 반복적인 복사 및 붙여넣기 흐름을 끊지 않도록 설계했습니다.
- 텍스트뿐 아니라 이미지와 파일 드롭까지 한곳에서 관리합니다.
- 시스템 트레이와 `Ctrl+Shift+V` 중심으로 빠르게 접근할 수 있습니다.
- 원격 동기화보다 로컬 우선 저장 구조에 집중했습니다.

## 핵심 기능

- 텍스트, 이미지, 파일 드롭 히스토리 저장
- 검색, 미리보기, 다중 선택 재복사
- 시스템 트레이 상주 및 백그라운드 실행
- 테마 설정, 자동 시작, 감사 로그
- 매니페스트 URL 기반 업데이트 확인 및 설치 파일 다운로드

## 버전 노트

### v0.2

- Net SDK 기반 프로젝트에서 WinUI 3 데스크톱 앱 구조로 전환
- 원격 매니페스트 기반 업데이트 확인 기능 추가
- Windows 데스크톱 사용 흐름에 맞게 메인 경험 정리

### v0.1

- 반복적인 복사 및 붙여넣기 작업의 번거로움을 줄이기 위한 초기 버전

## 기술 스택

- .NET 8
- WinUI 3
- LiteDB
- Inno Setup

## 빠른 시작

```powershell
dotnet restore .\Pelicano.sln --configfile .\NuGet.Config
dotnet build .\Pelicano.sln
```

설치 파일까지 만들려면 `build.bat`를 실행하면 됩니다. 이 스크립트는 아이콘 변환, 복원, publish, Inno Setup 패키징까지 한 번에 처리합니다.

## 프로젝트 구조

- `Winui/`: 메인 WinUI 3 앱과 런타임 로직
- `Installer/`: Inno Setup 설치 스크립트
- `docs/`: 개발자/보안 문서
- `scripts/`: 빌드 보조 스크립트

## 로컬 데이터와 보안

Pelicano는 기본적으로 사용자 로컬 경로 `%APPDATA%\Pelicano` 아래에 데이터를 저장합니다.

- 설정: `%APPDATA%\Pelicano\settings.json`
- 히스토리 DB: `%APPDATA%\Pelicano\history.db`
- 이미지 캐시: `%APPDATA%\Pelicano\images`
- 로그: `%APPDATA%\Pelicano\logs`
- 업데이트 다운로드: `%APPDATA%\Pelicano\updates`

추가 보안/운영 정보는 아래 문서를 참고할 수 있습니다.

- [개발자 가이드](./docs/개발자_가이드.md)
- [보안팀 설명서](./docs/보안팀_설명서.md)
