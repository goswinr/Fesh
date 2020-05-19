namespace Seff.Config

open Seff



type Config (log:ISeffLog, context:HostingMode, startupArgs:string[]) =
    
    let hostInfo = HostingInfo(context)

    member val Settings                   = Settings                    (log, hostInfo)
    member val RecentlyUsedFiles          = RecentlyUsedFiles           (log, hostInfo)
    member val OpenTabs                   = OpenTabs                    (log, hostInfo, startupArgs)
    member val DefaultCode                = DefaultCode                 (log, hostInfo)
    member val AutoCompleteStatistic      = AutoCompleteStatistic       (log, hostInfo)
    member val AssemblyReferenceStatistic = AssemblyReferenceStatistic  (log, hostInfo)
    member val HostingInfo                = hostInfo      
    member val Log                        = log 


