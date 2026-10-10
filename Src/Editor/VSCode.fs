namespace Fesh.Editor

open System
open System.IO

open Fesh.Model

/// Opens files and folders in Visual Studio Code
[<RequireQualifiedAccess>]
module VSCode =

    /// The command line script of VS Code, from the PATH.
    /// Starting just 'code' via the shell would find Code.exe via the 'App Paths' in the registry, that ignores the arguments when started hidden.
    let private codeCmd () =
        Environment.GetEnvironmentVariable("PATH").Split([|';'|], StringSplitOptions.RemoveEmptyEntries)
        |> Seq.tryPick (fun dir ->
            try
                let p = Path.Combine(dir.Trim('"'), "code.cmd")
                if File.Exists p then Some p else None
            with _ -> None ) // invalid characters in the PATH

    /// Starts VS Code with these command line arguments.
    /// Returns false and logs the error if it could not be started.
    let run (args:string) : bool =
        match codeCmd() with
        | None ->
            IFeshLog.log.PrintfnIOErrorMsg "Could not open VS Code, 'code.cmd' is not on the PATH. Is VS Code installed?"
            false
        | Some cmd ->
            try
                let psi = Diagnostics.ProcessStartInfo(cmd, args)
                psi.UseShellExecute <- false
                psi.CreateNoWindow  <- true
                Diagnostics.Process.Start psi |> ignore
                true
            with e ->
                IFeshLog.log.PrintfnIOErrorMsg "Could not start VS Code with:\r\n%s %s\r\n%s" cmd args e.Message
                false

    /// Opens a file or folder, in a new window or else in the last active one.
    /// Returns false and logs the error if VS Code could not be started.
    let openPath (path:string, newWindow:bool) : bool =
        run (sprintf "\"%s\" %s" path (if newWindow then "--new-window" else "--reuse-window"))

    /// Opens the file at the line and column, both one based.
    /// With a folder, VS Code opens that folder too, or switches to the window that has it open already.
    let openAt (folder:option<string>, file:string, line:int, column:int) =
        let goto = sprintf "--goto \"%s:%d:%d\"" file (max 1 line) (max 1 column)
        let args = match folder with Some d -> sprintf "\"%s\" %s" d goto | None -> goto
        if run args then
            IFeshLog.log.PrintfnInfoMsg "Opened in VS Code: %s  Line:%d" file line

    /// The folder of the git repository, or else of the solution or project, that contains the file.
    /// So that VS Code loads the project, for further navigation from there.
    let projectFolder (file:FileInfo) : string =
        let has (pattern:string) (d:DirectoryInfo) =
            try d.EnumerateFileSystemInfos pattern |> Seq.isEmpty |> not with _ -> false
        let rec up (isRoot:DirectoryInfo -> bool) (d:DirectoryInfo) =
            if isNull d then None
            elif isRoot d then Some d.FullName
            else up isRoot d.Parent
        up (has ".git") file.Directory
        |> Option.orElse (up (fun d -> has "*.sln" d || has "*.slnx" d || has "*.fsproj" d) file.Directory)
        |> Option.defaultValue file.DirectoryName
