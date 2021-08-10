
mode con:cols=200 lines=60

@REM loaded via nuget now !
@REM rmdir /s /q  "C:/GitHub/AvalonEditB/AvalonEditB/bin"
@REM dotnet build "C:/GitHub/AvalonEditB/AvalonEditB/AvalonEditB.csproj" --configuration Release --framework net472 


rmdir /s /q  "C:/GitHub/FsEx/bin"
dotnet build "C:/GitHub/FsEx/FsEx.fsproj" --configuration Release

rmdir /s /q  "C:/GitHub/FsEx.Wpf/bin"
dotnet build "C:/GitHub/FsEx.Wpf/FsEx.Wpf.fsproj" --configuration Release 

rmdir /s /q  "C:/GitHub/FsEx.Wpf.Slider/bin"
dotnet build "C:/GitHub/FsEx.Wpf.Slider/FsEx.Wpf.Slider.fsproj" --configuration Release 
  

rmdir /s /q  "C:/GitHub/AvalonLog/bin"
dotnet build "C:/GitHub/AvalonLog/AvalonLog.fsproj" --configuration Release 


rmdir /s /q  "C:/GitHub/Seff/binStandalone"
dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x64 --configuration Release
@REM dotnet build "C:/GitHub/Seff/SeffStandalone.fsproj" -p:Platform=x86 --configuration Release


rmdir /s /q  "C:/GitHub/Seff/binHosting"
dotnet build "C:/GitHub/Seff/SeffHosting.fsproj" -p:Platform=x64 --configuration Release

:: Rhino7
rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh7"
dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino7.fsproj" -p:Platform=x64 --configuration Release
rmdir /s /q  "C:/GitHub/Rhino.Scripting/binRh7"
dotnet build "C:/GitHub/Rhino.Scripting/Rhino7.Scripting.fsproj" -p:Platform=x64 --configuration Release


:: Rhino6
:: first delete obj folder so that ther reference really goes to rhino 6 and not 7
rmdir /s /q  "C:/GitHub/Seff.Rhino/obj" 
rmdir /s /q  "C:/GitHub/Seff.Rhino/binRh6"
dotnet build "C:/GitHub/Seff.Rhino/Seff.Rhino6.fsproj" -p:Platform=x64 --configuration Release
:: first delete obj folder so that ther reference really goes to rhino 6 and not 7
rmdir /s /q  "C:/GitHub/Rhino.Scripting//obj" 
rmdir /s /q  "C:/GitHub/Rhino.Scripting/binRh6"
dotnet build "C:/GitHub/Rhino.Scripting/Rhino6.Scripting.fsproj" -p:Platform=x64 --configuration Release

@REM rmdir /s /q  "C:/GitHub/Rhino.Scripting.QrCode/bin"
@REM dotnet build "C:/GitHub/Rhino.Scripting.QrCode/Rhino.Scripting.QrCode.fsproj" -p:Platform=x64 --configuration Release

rmdir /s /q  "C:/GitHub/Rhino.Scripting.Extra/bin"
dotnet build "C:/GitHub/Rhino.Scripting.Extra/Rhino.Scripting.Extra.fsproj" -p:Platform=x64 --configuration Release

:: Revit
rmdir /s /q "C:/GitHub/Seff.Revit/bin"
dotnet build "C:/GitHub/Seff.Revit/Seff.Revit.2019.fsproj" --configuration Release
dotnet build "C:/GitHub/Seff.Revit/Seff.Revit.2021.fsproj" --configuration Release


PAUSE