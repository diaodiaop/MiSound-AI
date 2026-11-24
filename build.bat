@echo off
setlocal
where msbuild >nul 2>nul
if errorlevel 1 (
  echo 未找到 MSBuild。请使用 Visual Studio Developer Command Prompt 运行此脚本。
  pause
  exit /b 1
)
nuget restore MiGPT.Local.sln
if errorlevel 1 (
  echo NuGet 还原失败，请在 Visual Studio 中右键解决方案 - 还原 NuGet 程序包。
  pause
  exit /b 1
)
msbuild MiGPT.Local.sln /p:Configuration=Release /m
if errorlevel 1 pause
endlocal
