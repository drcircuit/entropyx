dotnet publish src/CodeEvo.Cli /p:PublishProfile=win-x64 -o out/win-x64
copy out\win-x64\entropyx.exe c:\tools\bin\ /y