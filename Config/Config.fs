namespace Seff.Config

open Seff



type Config (log:ISeffLog, context:AppRunContext, startupArgs:string[]) =
    
    let adl = HostingMode(context)

    member val Settings                   = Settings                    (log, adl)
    member val RecentlyUsedFiles          = RecentlyUsedFiles           (log, adl)
    member val OpenTabs                   = OpenTabs                    (log, adl, startupArgs)
    member val DefaultCode                = DefaultCode                 (log, adl)
    member val AutoCompleteStatistic      = AutoCompleteStatistic       (log, adl)
    member val AssemblyReferenceStatistic = AssemblyReferenceStatistic  (log, adl)
    member val HostingMode            = adl      
    member val Log                        = log 


