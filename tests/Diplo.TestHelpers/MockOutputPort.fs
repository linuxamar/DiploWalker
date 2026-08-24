namespace Diplo.TestHelpers

open System
open Diplo.Core.Output

/// Mock IOutputPort partagé par les suites de tests (CLI et GUI).
/// Les alias Messages/Lines et Clear/Reset couvrent les deux conventions.
type MockOutputPort() =

    let mutable lines = ResizeArray<string>()
    let mutable errors = ResizeArray<string>()
    let mutable successes = ResizeArray<string>()
    let mutable warnings = ResizeArray<string>()
    let mutable tables = ResizeArray<string[]>()

    member _.Lines = lines |> Seq.toList
    member _.Messages = lines |> Seq.toList
    member _.Errors = errors |> Seq.toList
    member _.Successes = successes |> Seq.toList
    member _.Warnings = warnings |> Seq.toList
    member _.Tables = tables |> Seq.toList

    member _.HasOutput =
        lines.Count > 0 || errors.Count > 0 || successes.Count > 0 || warnings.Count > 0

    member _.Reset() =
        lines.Clear()
        errors.Clear()
        successes.Clear()
        warnings.Clear()
        tables.Clear()

    member this.Clear() = this.Reset()

    interface IOutputPort with
        member _.WriteLine(text) = lines.Add(text)
        member _.WriteError(text) = errors.Add(text)
        member _.WriteSuccess(text) = successes.Add(text)
        member _.WriteWarning(text) = warnings.Add(text)
        member _.WriteTable(_items, columns, _selector) = tables.Add(columns)
