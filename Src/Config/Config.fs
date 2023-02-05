namespace Seff.Config

open Seff.Model


type Config (log:ISeffLog, startUpData:HostedStartUpData option, startupArgs:string[]) = 

    let  runContext                 = new RunContext                  (startUpData)
    let  settings                   = new FsEx.Wpf.Settings           (runContext.SettingsFileInfo,ISeffLog.printError)
    let  recentlyUsedFiles          = new RecentlyUsedFiles           (runContext)
    let  openTabs                   = new OpenTabs                    (runContext, startupArgs)
    let  defaultCode                = new DefaultCode                 (runContext)
    let  autoCompleteStatistic      = new AutoCompleteStatistic       (runContext)
    let  fsiArguments               = new FsiArguments                (runContext)
    let  foldingStatus              = new FoldingStatus               (runContext, recentlyUsedFiles)

    member this.RunContext                 = runContext
    member this.Settings                   = settings
    member this.RecentlyUsedFiles          = recentlyUsedFiles
    member this.OpenTabs                   = openTabs
    member this.DefaultCode                = defaultCode
    member this.AutoCompleteStatistic      = autoCompleteStatistic    
    member this.FsiArguments               = fsiArguments 
    member this.FoldingStatus              = foldingStatus

    member this.Log                        = log

