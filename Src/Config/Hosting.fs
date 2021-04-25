namespace Seff.Config

open System
open Seff.Model


/// mainWindowHandle: Pointer to main window(nativeInt), 
/// hostName: a string for the name of the hosting App (will be used for settings file name an displayed in the Title Bar.
/// fsiCanRun: a function to check if evaluation of fsi is currently allowed
type HostedStartUpData = { hostName:string; mainWindowHandel: nativeint; fsiCanRun: unit-> bool}

//type StartupMode =  Standalone  | Hosted of HostedStartUpData


/// A class to hold the current App Run context (Standalone or Hosted)
type Hosting (startUpData:HostedStartUpData option) =    
    let configFolder = 
        let appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        let p = IO.Path.Combine(appData,"Seff")
        IO.Directory.CreateDirectory(p) |> ignore 
        p

    let hostName = //to get a valid filename fom any host app name suplied not allowed : < > : " / \ | ? *
        match startUpData with 
        |None ->  "Standalone" 
        |Some sd ->  
              let sb = new Text.StringBuilder()
              for c in sd.hostName do
                  if (c >= '0' && c <= '9') 
                  || (c >= 'A' && c <= 'Z') 
                  || (c >= 'a' && c <= 'z') 
                  ||  c = '.' 
                  ||  c = '_'
                  ||  c = ' ' 
                  ||  c = '-'
                  ||  c = '+' then  sb.Append(c) |> ignore
              
              "Hosted." + sb.ToString()
    
    
    /// To get a path where to save the setting files
    member this.GetPathToSaveAppData (fileNameInclExt) =
        let file = sprintf "%s.%s" hostName fileNameInclExt
        IO.Path.Combine(configFolder, file )
    
    
    member this.FsiCanRun =   match startUpData with None ->  true  | Some d -> d.fsiCanRun() 

    member this.HostName    =  match startUpData with None ->  None | Some d -> Some d.hostName 
    
    member this.IsHosted     = match startUpData with None ->  false | Some _ -> true 
    
    member this.IsStandalone = match startUpData with None ->  true  | Some _ -> false
    
    /// opens up Explorer.exe
    member this.OpenSettingsFolder()=               
        Diagnostics.Process.Start("explorer.exe", "\"" + configFolder+ "\"")        |> ignore

    /// opens up Explorer.exe with folder of Seff.exe
    member this.OpenAppFolder()=               
        let ass = Reflection.Assembly.GetExecutingAssembly()
        if isNull ass then 
            ISeffLog.log.PrintfIOErrorMsg "OpenAppFolder: GetExecutingAssembly() is null"
        else
            if ass.IsDynamic then 
                ISeffLog.log.PrintfIOErrorMsg "Can get path of %A" ass.FullName
            else
                let folder = IO.Path.GetDirectoryName( ass.Location)
                Diagnostics.Process.Start("explorer.exe", "\"" + folder+ "\"")        |> ignore
   

    