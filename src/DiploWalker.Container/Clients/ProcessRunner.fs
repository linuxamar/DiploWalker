namespace DiploWalker.Container.Clients

open DiploWalker.Abstractions
open DiploWalker.Abstractions.Interfaces

/// Exécuteur réel de processus (ctr CLI)
type ProcessRunner(?timeoutMs: int) =

    let timeout = defaultArg timeoutMs 60_000

    interface IProcessRunner with
        member _.RunWithArgs(fileName, args) =
            ProcessExec.run fileName args (Some timeout) None None

