namespace Diplo.Gui.ViewModels

open System.Threading.Tasks
open Diplo.Core.Output

module internal Cmd =

    let private describe (ex: exn) =
        let inner =
            if isNull ex.InnerException then
                ""
            else
                sprintf " (cause : %s : %s)" (ex.InnerException.GetType().Name) ex.InnerException.Message

        sprintf "%s : %s%s" (ex.GetType().Name) ex.Message inner

    let run (output: IOutputPort) (work: unit -> Task<unit>) : Task<unit> =
        task {
            try
                do! work ()
            with ex ->
                output.WriteError(describe ex)
        }

    let runSync (output: IOutputPort) (work: unit -> unit) : unit =
        try
            work ()
        with ex ->
            output.WriteError(describe ex)

    let runSyncWith (output: IOutputPort) (onError: string -> unit) (work: unit -> unit) : unit =
        try
            work ()
        with ex ->
            onError (describe ex)
