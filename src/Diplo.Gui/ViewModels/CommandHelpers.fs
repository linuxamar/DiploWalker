namespace Diplo.Gui.ViewModels

open System.Threading.Tasks
open Diplo.Core.Output

module internal Cmd =

    let run (output: IOutputPort) (work: unit -> Task<unit>) : Task<unit> =
        task {
            try
                do! work()
            with ex -> output.WriteError(ex.Message)
        }

    let runSync (output: IOutputPort) (work: unit -> unit) : unit =
        try
            work()
        with ex -> output.WriteError(ex.Message)

    let runSyncWith (output: IOutputPort) (onError: string -> unit) (work: unit -> unit) : unit =
        try
            work()
        with ex -> onError ex.Message
