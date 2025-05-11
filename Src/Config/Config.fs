namespace Fesh.Config

open Fesh.Model


type Config (log:IFeshLog, startUpData:HostedStartUpData option, startupArgs:string[]) =

    let  runContext                 = new RunContext                  (startUpData)
    let  settings                   = new Fittings.PersistentSettings (runContext.SettingsFileInfo, IFeshLog.printError)
    let  recentlyUsedFiles          = new RecentlyUsedFiles           (runContext)
    let  openTabs                   = new OpenTabs                    (runContext, startupArgs)
    let  defaultCode                = new DefaultCode                 (runContext)
    let  scriptCompilerFsproj       = new ScriptCompilerFsproj        (runContext)
    let  autoCompleteStatistic      = new AutoCompleteStatistic       (runContext)
    let  fsiArguments               = new FsiArguments                (runContext)
    let  foldingStatus              = new FoldingStatus               (runContext, recentlyUsedFiles)

    member this.RunContext                 = runContext
    member this.Settings                   = settings
    member this.RecentlyUsedFiles          = recentlyUsedFiles
    member this.OpenTabs                   = openTabs
    member this.DefaultCode                = defaultCode
    member this.ScriptCompilerFsproj       = scriptCompilerFsproj
    member this.AutoCompleteStatistic      = autoCompleteStatistic
    member this.FsiArguments               = fsiArguments
    member this.FoldingStatus              = foldingStatus

    member this.Log                        = log


