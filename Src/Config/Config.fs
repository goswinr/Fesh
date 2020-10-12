namespace Seff.Config

open System
open Seff



type Config (log:ISeffLog, context:HostingMode, startupArgs:string[]) =
    
    let  hostInfo                   = HostingInfo                 (context)
    let  settings                   = Settings                    (log, hostInfo)
    let  recentlyUsedFiles          = RecentlyUsedFiles           (log, hostInfo)
    let  openTabs                   = OpenTabs                    (log, hostInfo, startupArgs)
    let  defaultCode                = DefaultCode                 (log, hostInfo)
    let  autoCompleteStatistic      = AutoCompleteStatistic       (log, hostInfo)
    let  assemblyReferenceStatistic = AssemblyReferenceStatistic  (log, hostInfo)
    let  fsiArugments               = FsiArugments                (log, hostInfo)
    let  foldingStatus              = FoldingStatus               (log, hostInfo, recentlyUsedFiles)
 

    member this.HostingInfo                = hostInfo      
    member this.Log                        = log 
    member this.Settings                   = settings                  
    member this.RecentlyUsedFiles          = recentlyUsedFiles         
    member this.OpenTabs                   = openTabs                  
    member this.DefaultCode                = defaultCode               
    member this.AutoCompleteStatistic      = autoCompleteStatistic     
    member this.AssemblyReferenceStatistic = assemblyReferenceStatistic
    member this.FsiArugments               = fsiArugments  
    member this.FoldingStatus              = foldingStatus  
    