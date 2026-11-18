module DiploWalker.Gui.Program

open System
open Avalonia
open Avalonia.ReactiveUI
open DiploWalker.Gui

/// Construit l'application Avalonia avec la configuration standard.
let BuildAvaloniaApp() =
    AppBuilder.Configure<App>()
        .UsePlatformDetect()
        .WithInterFont()
        .UseReactiveUI()
        .LogToTrace()

[<EntryPoint>]
let main args =
    try
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args)
            |> ignore
        0
    with ex ->
        Console.Error.WriteLine(sprintf "Erreur fatale : %s" ex.Message)
        1