
mode con:cols=200 lines=60

rmdir /s /q  "D:/Git/FsEx/bin"
dotnet build "D:/Git/FsEx/FsEx.fsproj" --configuration Release


rmdir /s /q  "D:/Git/FsEx.Wpf/bin"
dotnet build "D:/Git/FsEx.Wpf/FsEx.Wpf.fsproj" --configuration Release 
  
rmdir /s /q  "D:/Git/AvalonEditB/AvalonEditB/bin"
dotnet build "D:/Git/AvalonEditB/AvalonEditB/AvalonEditB.csproj" --configuration Release --framework net472 

rmdir /s /q  "D:/Git/AvalonLog/bin"
dotnet build "D:/Git/AvalonLog/AvalonLog.fsproj" --configuration Release 


rmdir /s /q  "D:/Git/Seff/binStandalone"
dotnet build "D:/Git/Seff/SeffStandalone.fsproj" -p:Platform=x64 --configuration Release
@REM dotnet build "D:/Git/Seff/SeffStandalone.fsproj" -p:Platform=x86 --configuration Release


rmdir /s /q  "D:/Git/Seff/binHosting"
dotnet build "D:/Git/Seff/SeffHosting.fsproj" -p:Platform=x64 --configuration Release

:: Rhino7
rmdir /s /q  "D:/Git/Seff.Rhino/binRh7"
dotnet build "D:/Git/Seff.Rhino/Seff.Rhino7.fsproj" -p:Platform=x64 --configuration Release
rmdir /s /q  "D:/Git/Rhino.Scripting/binRh7"
dotnet build "D:/Git/Rhino.Scripting/Rhino.Scripting7.fsproj" -p:Platform=x64 --configuration Release


:: Rhino6
:: first delete obj folder so that ther reference really goes to rhino 6 and not 7
rmdir /s /q  "D:/Git/Seff.Rhino/obj" 
rmdir /s /q  "D:/Git/Seff.Rhino/binRh6"
dotnet build "D:/Git/Seff.Rhino/Seff.Rhino6.fsproj" -p:Platform=x64 --configuration Release

rmdir /s /q  "D:/Git/Rhino.Scripting/obj"
rmdir /s /q  "D:/Git/Rhino.Scripting/binRh6"
dotnet build "D:/Git/Rhino.Scripting/Rhino.Scripting6.fsproj" -p:Platform=x64 --configuration Release


rmdir /s /q "D:/Git/Seff.Revit/bin"
dotnet build "D:/Git/Seff.Revit/Seff.Revit.2019.fsproj" --configuration Release
dotnet build "D:/Git/Seff.Revit/Seff.Revit.2021.fsproj" --configuration Release

@REM :: Revit 2018
@REM :: first delete obj folder to resolve version conflicts of FSharp.Core 5.0.0 vs 4.6.2
@REM rmdir /s /q "D:/Git/Seff/obj" 
@REM rmdir /s /q "D:/Git/Seff/binHostingRevit"
@REM dotnet build "D:/Git/Seff/SeffHostingRevit.fsproj" --configuration Release
@REM rmdir /s /q "D:/Git/Seff.Revit/bin2018"
@REM dotnet build "D:/Git/Seff.Revit/Seff.Revit2018.fsproj" --configuration Release



PAUSE