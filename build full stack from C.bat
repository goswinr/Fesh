
mode con:cols=200 lines=60

rmdir /s /q  "C:/GitHub/FsEx/bin"
dotnet build "C:/GitHub/FsEx/FsEx.fsproj" --configuration Release



@REM rmdir /s /q  "C:/GitHub/FsEx.Wpf/bin"
@REM dotnet build "C:/GitHub/FsEx.Wpf/FsEx.Wpf.fsproj" --configuration Release 
  
@REM rmdir /s /q  "C:/GitHub/AvalonEditB/AvalonEditB/bin"
@REM dotnet build "C:/GitHub/AvalonEditB/AvalonEditB/AvalonEditB.csproj" --configuration Release --framework net472 

@REM rmdir /s /q  "C:/GitHub/AvalonLog/bin"
@REM dotnet build "C:/GitHub/AvalonLog/AvalonLog.fsproj" --configuration Release 





rmdir /s /q  "C:/GitHub/Seff/binStandalone"
dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x64 --configuration Release
@REM dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x86 --configuration Release


rmdir /s /q  "C:/GitHub/Seff/binHosting"
dotnet build "C:/GitHub/Seff/SeffHosting.fsproj" -p:Platform=x64 --configuration Release

:: Rhino7
rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh7"
dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino7.fsproj" -p:Platform=x64 --configuration Release
rmdir /s /q  "C:/GitHub/Rhino.Scripting/binRh7"
dotnet build "C:/GitHub/Rhino.Scripting/Rhino.Scripting7.fsproj" -p:Platform=x64 --configuration Release


@REM :: Rhino6
@REM :: first delete obj folder so that ther reference really goes to rhino 6 and not 7
@REM rmdir /s /q  "C:/GitHub/Seff.Rhino/obj" 
@REM rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh6"
@REM dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino6.fsproj" -p:Platform=x64 --configuration Release

@REM rmdir /s /q  "C:/GitHub/Rhino.Scripting/obj"
@REM rmdir /s /q  "C:/GitHub/Rhino.Scripting/binRh6"
@REM dotnet build "C:/GitHub/Rhino.Scripting/Rhino.Scripting6.fsproj" -p:Platform=x64 --configuration Release


@REM :: Revit 2018
@REM :: first delete obj folder to resolve version conflicts of FSharp.Core 5.0.0 vs 4.6.2
@REM rmdir /s /q "C:/GitHub/Seff/obj" 
@REM rmdir /s /q "C:/GitHub/Seff/binHostingRevit"
@REM dotnet build "C:/GitHub/Seff/SeffHostingRevit.fsproj" --configuration Release
@REM rmdir /s /q "C:/GitHub/Seff.Revit/bin2018"
@REM dotnet build "C:/GitHub/Seff.Revit/Seff.Revit2018.fsproj" --configuration Release



PAUSE