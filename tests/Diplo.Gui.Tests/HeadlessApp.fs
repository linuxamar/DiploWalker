namespace Diplo.Gui.Tests

open Avalonia
open Avalonia.Headless
open Diplo.Gui

/// Harnais de tests headless : initialise le plateforme Avalonia headless sur
/// le thread de test courant. Dispatcher.UIThread devient alors ce thread et
/// Dispatcher.UIThread.RunJobs() traite les rappels poster via UIThread.Post.
/// Chaque test appelle setupHeadless() avant d'exercer le ViewModel.
module HeadlessRunner =

    let mutable initialized = false

    let setupHeadless () =
        if not initialized then
            AppBuilder
                .Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting()
            |> ignore

            initialized <- true
