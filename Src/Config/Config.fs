namespace Seff.Config

open System
open Seff



type Config (log:ISeffLog, startUpData:HostedStartUpData option, startupArgs:string[]) =
    
    let  hosting                    = Hosting                     (startUpData)
    let  settings                   = Settings                    (log, hosting)
    let  recentlyUsedFiles          = RecentlyUsedFiles           (log, hosting)
    let  openTabs                   = OpenTabs                    (log, hosting, startupArgs)
    let  defaultCode                = DefaultCode                 (log, hosting)
    let  autoCompleteStatistic      = AutoCompleteStatistic       (log, hosting)
    let  assemblyReferenceStatistic = AssemblyReferenceStatistic  (log, hosting)
    let  fsiArugments               = FsiArugments                (log, hosting)
    let  foldingStatus              = FoldingStatus               (log, hosting, recentlyUsedFiles)
 

    member this.Hosting                    = hosting     
    member this.Log                        = log 
    member this.Settings                   = settings                  
    member this.RecentlyUsedFiles          = recentlyUsedFiles         
    member this.OpenTabs                   = openTabs                  
    member this.DefaultCode                = defaultCode               
    member this.AutoCompleteStatistic      = autoCompleteStatistic     
    member this.AssemblyReferenceStatistic = assemblyReferenceStatistic
    member this.FsiArugments               = fsiArugments  
    member this.FoldingStatus              = foldingStatus  
    