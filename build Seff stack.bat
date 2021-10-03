
@REM mode con:cols=200 lines=90

rmdir /s /q  "C:/GitHub/AvalonEditB/AvalonEditB/bin"
dotnet build "C:/GitHub/AvalonEditB/AvalonEditB/AvalonEditB.csproj" --configuration Release 

rmdir /s /q  "C:/GitHub/AvalonLog/bin"
dotnet build "C:/GitHub/AvalonLog/AvalonLog.fsproj" --configuration Release 

@REM rmdir /s /q  "C:/GitHub/FsEx.Wpf/bin"
@REM dotnet build "C:/GitHub/FsEx.Wpf/FsEx.Wpf.fsproj" --configuration Release 

rmdir /s /q  "C:/GitHub/Seff/binStandalone"
dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x64 --configuration Release
@REM dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x86 --configuration Release

@REM rmdir /s /q  "C:/GitHub/Seff/binHosting"
@REM dotnet build "C:/GitHub/Seff/SeffHosting.fsproj" -p:Platform=x64 --configuration Release

@REM :: Rhino7
@REM rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh7"
@REM dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino7.fsproj" -p:Platform=x64 --configuration Release


@REM Wait 10 sekonds then exit
timeout /T 10


@REM wait for any key input to exit
@REM PAUSE