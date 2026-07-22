module Diplo.Gui.Program

open Avalonia
open System

[<EntryPoint>]
let main argv =
    AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace()
        .StartWithClassicDesktopLifetime(argv)
