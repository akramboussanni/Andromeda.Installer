@echo off
setlocal

pushd "%~dp0"

echo Publishing Andromeda.Installer as non-single-file EXE...
dotnet publish Andromeda.Installer.csproj ^
  -c Release ^
  -r win-x64 ^
  --self-contained true ^
  /p:UseAppHost=true ^
  /p:PublishTrimmed=true ^
  /p:PublishSingleFile=false ^
  /p:IncludeNativeLibrariesForSelfExtract=false ^
  /p:EnableCompressionInSingleFile=false

if errorlevel 1 (
  echo.
  echo Publish failed.
  popd
  exit /b 1
)

echo.
echo Publish succeeded.
echo Output: %~dp0Output\win-x64\

popd
exit /b 0
