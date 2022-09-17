namespace Seff.Config

open System
open Seff
open Seff.Model



type Config (log:ISeffLog, startUpData:HostedStartUpData option, startupArgs:string[]) = 


    let  hosting                    = Hosting                     (startUpData)
    let  settings                   = FsEx.Wpf.Settings           (hosting.SettingsFileInfo,ISeffLog.printError)
    let  recentlyUsedFiles          = RecentlyUsedFiles           (hosting)
    let  openTabs                   = OpenTabs                    (hosting, startupArgs)
    let  defaultCode                = DefaultCode                 (hosting)
    let  autoCompleteStatistic      = AutoCompleteStatistic       (hosting)
    let  assemblyReferenceStatistic = AssemblyReferenceStatistic  (hosting)
    let  fsiArguments               = FsiArguments                (hosting)
    let  foldingStatus              = FoldingStatus               (hosting, recentlyUsedFiles)


    member this.Hosting                    = hosting
    member this.Settings                   = settings
    member this.RecentlyUsedFiles          = recentlyUsedFiles
    member this.OpenTabs                   = openTabs
    member this.DefaultCode                = defaultCode
    member this.AutoCompleteStatistic      = autoCompleteStatistic
    member this.AssemblyReferenceStatistic = assemblyReferenceStatistic
    member this.FsiArguments               = fsiArguments 
    member this.FoldingStatus              = foldingStatus

    member this.Log                        = log

