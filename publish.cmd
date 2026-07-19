@echo off
rem Builds a single self-contained MicFX.exe into .\dist (no .NET needed on target PC)
dotnet publish src\MicFX\MicFX.csproj -c Release -r win-x64 --self-contained ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist
echo.
echo Done. Your app is at dist\MicFX.exe
pause
