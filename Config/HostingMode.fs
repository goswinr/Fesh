namespace Seff.Config

open System
open Seff


/// A class to hold the current App Run context (Standalone or Hosted)
type HostingMode (ctx:AppRunContext) =    
    let configFolder = 
        let appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        let p = IO.Path.Combine(appData,"Seff")
        IO.Directory.CreateDirectory(p) |> ignore 
        p

    let hostName = //to get a valid filename fom any host app name suplied
        match ctx with 
        |Standalone ->  "Standalone" 
        |Hosted name ->  
              let sb = new Text.StringBuilder()
              for c in name do
                  if (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c = '.' || c = '_'|| c = ' ' || c = '-'|| c = '+' then  sb.Append(c) |> ignore
              "Hosted." + sb.ToString()
    
    
    /// To get a path where to save the setting files
    member this.GetPathToSaveAppData (fileNameInclExt) =
        let file = sprintf "%s.%s" hostName fileNameInclExt
        IO.Path.Combine(configFolder, file )
    
    
    member this.Mode     = ctx 
    
    member this.IsHosted     = ctx <> Standalone        
    
    member this.IsStandalone = ctx = Standalone     
    
    /// opens up Explorer.exe
    member this.OpenFolder()=               
        Diagnostics.Process.Start("explorer.exe", "\"" + configFolder+ "\"")        |> ignore
   

    