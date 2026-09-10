namespace Diplo.Container.Clients

open Diplo.Abstractions
open Diplo.Abstractions.Interfaces

/// Exécuteur réel de processus (ctr CLI)
type ProcessRunner(?timeoutMs: int) =

    let timeout = defaultArg timeoutMs 60_000

    interface IProcessRunner with
        member _.RunWithArgs(fileName, args) =
            ProcessExec.run fileName args (Some timeout) None None
