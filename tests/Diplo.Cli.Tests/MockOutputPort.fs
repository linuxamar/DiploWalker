namespace Diplo.Cli.Tests

open System
open System.Collections.Generic
open Diplo.Core.Output

/// Mock IOutputPort qui enregistre tous les appels pour vérification en test.
type MockOutputPort() =

    let mutable lines = ResizeArray<string>()
    let mutable errors = ResizeArray<string>()
    let mutable successes = ResizeArray<string>()
    let mutable warnings = ResizeArray<string>()
    let mutable tables = ResizeArray<string[] * int>()  // colonnes * nombre de lignes

    member _.Lines = lines |> Seq.toArray
    member _.Errors = errors |> Seq.toArray
    member _.Successes = successes |> Seq.toArray
    member _.Warnings = warnings |> Seq.toArray
    member _.Tables = tables |> Seq.toArray
    member _.HasOutput = lines.Count > 0 || errors.Count > 0 || successes.Count > 0 || warnings.Count > 0

    member _.Reset() =
        lines.Clear()
        errors.Clear()
        successes.Clear()
        warnings.Clear()
        tables.Clear()

    interface IOutputPort with
        member _.WriteLine(text) = lines.Add(text)
        member _.WriteError(text) = errors.Add(text)
        member _.WriteSuccess(text) = successes.Add(text)
        member _.WriteWarning(text) = warnings.Add(text)
        member _.WriteTable(_items, columns, _selector) =
            tables.Add((columns, 0))
