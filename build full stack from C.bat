
mode con:cols=200 lines=60

rmdir /s /q  "C:/GitHub/FsEx/bin"
dotnet build "C:/GitHub/FsEx/FsEx.fsproj" --configuration Release

  
rmdir /s /q  "C:/GitHub/AvalonEditB/AvalonEditB/bin"
dotnet build "C:/GitHub/AvalonEditB/AvalonEditB/AvalonEditB.csproj" --configuration Release --framework net472 


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





@REM :: Rhino7Full to include FsEx and Rhino.Scripting
@REM rmdir /s /q "C:\GitHub\Seff.Rhino\binRh7Full"
@REM dotnet build "C:\GitHub\Seff.Rhino\Seff.Rhino7.Full.fsproj" 


:: Rhino6
:: first delete obj folder so that ther reference really goes to rhino 6 and not 7
rmdir /s /q  "C:/GitHub/Seff.Rhino/obj" 
rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh6"
dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino6.fsproj" --configuration Release
@REM rmdir /s /q  "C:/GitHub/Rhino.Scripting/obj"
@REM rmdir /s /q  "C:/GitHub/Rhino.Scripting/binRh6"
@REM dotnet build "C:/GitHub/Rhino.Scripting/Rhino.Scripting6.fsproj" --configuration Release

@REM :: Rhino6Full to include FsEx and Rhino.Scripting
@REM rmdir /s /q  "C:\GitHub\Seff.Rhino\binRh6Full"
@REM dotnet build "C:\GitHub\Seff.Rhino\Seff.Rhino6.Full.fsproj" --configuration Release


@REM :: Revit 2018
@REM :: first delete obj folder to resolve version conflicts of FSharp.Core 5.0.0 vs 4.6.2
@REM rmdir /s /q "C:/GitHub/Seff/obj" 
@REM rmdir /s /q "C:/GitHub/Seff/binHostingRevit"
@REM dotnet build "C:/GitHub/Seff/SeffHostingRevit.fsproj" --configuration Release
@REM rmdir /s /q "C:/GitHub/Seff.Revit/bin2018"
@REM dotnet build "C:/GitHub/Seff.Revit/Seff.Revit2018.fsproj" --configuration Release


@REM :: and again to have a distributable stabndalone version that does not show erroes even if a ref to rhino is there
@REM :: to include FsEx and Rhino.Scripting
@REM rmdir /s /q  "C:/GitHub/Seff/binStandaloneFsEx"
@REM dotnet build "C:/GitHub/Seff/SeffStandaloneFsEx.fsproj" --configuration Release

PAUSE