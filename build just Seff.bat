
@REM mode con:cols=200 lines=90

rmdir /s /q  "C:/GitHub/Seff/binStandalone"
dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x64 --configuration Release
@REM dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x86 --configuration Release

rmdir /s /q  "C:/GitHub/Seff/binHosting"
dotnet build "C:/GitHub/Seff/SeffHosting.fsproj" -p:Platform=x64 --configuration Release

@REM :: Rhino7
@REM rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh7"
@REM dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino7.fsproj" -p:Platform=x64 --configuration Release

:: Rhino6
:: first delete obj folder so that ther reference really goes to rhino 6 and not 7
@REM rmdir /s /q  "C:/GitHub/Seff.Rhino/obj" 
@REM rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh6"
@REM dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino6.fsproj" -p:Platform=x64 --configuration Release

:: Revit
@REM rmdir /s /q "C:/GitHub/Seff.Revit/bin"
@REM dotnet build "C:/GitHub/Seff.Revit/Seff.Revit.2019.fsproj" --configuration Release
@REM dotnet build "C:/GitHub/Seff.Revit/Seff.Revit.2021.fsproj" --configuration Release


PAUSE