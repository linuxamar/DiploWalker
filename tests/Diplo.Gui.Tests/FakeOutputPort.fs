module Diplo.Gui.Tests.FakeOutputPort

open System.Collections.Generic
open Diplo.Core.Output

type FakeOutputPort() =
    let messages = ResizeArray<string>()
    let errors = ResizeArray<string>()
    let successes = ResizeArray<string>()
    let warnings = ResizeArray<string>()
    let tables = ResizeArray<string[]>()

    member _.Messages = Seq.toList messages
    member _.Errors = Seq.toList errors
    member _.Successes = Seq.toList successes
    member _.Warnings = Seq.toList warnings
    member _.Tables = Seq.toList tables
    member _.Clear() = messages.Clear(); errors.Clear(); successes.Clear(); warnings.Clear(); tables.Clear()

    interface IOutputPort with
        member _.WriteLine(text) = messages.Add(text)
        member _.WriteError(text) = errors.Add(text)
        member _.WriteSuccess(text) = successes.Add(text)
        member _.WriteWarning(text) = warnings.Add(text)
        member _.WriteTable(items, columns, _selector) = tables.Add(columns)
