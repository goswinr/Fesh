namespace Seff.Config

open Seff
open Seff.Model


type Config (log:ISeffLog, context:AppRunContext) =
    
    let adl = AppDataLocation(context)

    member val Settings                   = Settings                    (log, adl)
    member val RecentlyUsedFiles          = RecentlyUsedFiles           (log, adl)
    member val OpenTabs                   = OpenTabs                    (log, adl)
    member val DefaultCode                = DefaultCode                 (log, adl)
    member val AutoCompleteStatistic      = AutoCompleteStatistic       (log, adl)
    member val AssemblyReferenceStatistic = AssemblyReferenceStatistic  (log, adl)
    member val AppDataLocation            = adl      
    


