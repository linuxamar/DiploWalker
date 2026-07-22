module Diplo.Gui.Tests.AvaloniaOutputPortTests

open Xunit
open FsUnit.Xunit
open Diplo.Gui.Services

// ── LogLevel ──────────────────────────────────────────────────

[<Fact>]
let ``LogLevel.Info est reconnu`` () =
    Info |> should equal Info

[<Fact>]
let ``LogLevel.Success est reconnu`` () =
    Success |> should equal Success

[<Fact>]
let ``LogLevel.Warning est reconnu`` () =
    Warning |> should equal Warning

[<Fact>]
let ``LogLevel.Error est reconnu`` () =
    Error |> should equal Error

[<Fact>]
let ``LogLevel discriminent sont distincts`` () =
    Info |> should not' (equal Success)
    Info |> should not' (equal Warning)
    Info |> should not' (equal Error)
    Success |> should not' (equal Warning)
    Success |> should not' (equal Error)
    Warning |> should not' (equal Error)

// ── LogEntry ──────────────────────────────────────────────────

[<Fact>]
let ``LogEntry.Text retourne le texte fourni`` () =
    let entry = LogEntry("test message", Info)
    entry.Text |> should equal "test message"

[<Fact>]
let ``LogEntry.Level retourne le niveau fourni`` () =
    let entry = LogEntry("msg", Warning)
    entry.Level |> should equal Warning

[<Fact>]
let ``LogEntry.Timestamp contient 8 caractères`` () =
    let entry = LogEntry("x", Info)
    entry.Timestamp |> should not' (be Null)
    entry.Timestamp.Length |> should equal 8

[<Fact>]
let ``LogEntry.DisplayText contient le texte`` () =
    let entry = LogEntry("hello", Success)
    entry.DisplayText |> should haveSubstring "hello"

[<Fact>]
let ``LogEntry.DisplayText contient un timestamp`` () =
    let entry = LogEntry("test", Info)
    entry.DisplayText |> should haveSubstring "["

[<Fact>]
let ``LogEntry avec texte vide`` () =
    let entry = LogEntry("", Error)
    entry.Text |> should equal ""
    entry.Level |> should equal Error
