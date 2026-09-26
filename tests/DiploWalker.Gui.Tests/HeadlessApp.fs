namespace DiploWalker.Gui.Tests

open System
open System.Threading
open Avalonia
open Avalonia.Headless
open Avalonia.Threading
open DiploWalker.Gui

/// Harnais de tests headless : initialise la plateforme Avalonia headless sur
/// un thread dédié qui possède Dispatcher.UIThread et le pompe en continu.
/// Les tests peuvent alors exercer les ViewModels depuis n'importe quel thread
/// (xunit.v3 exécute chaque test sur un thread différent) : les rappels
/// postés via UiThread.Post sont traités par la boucle du thread dédié, et
/// waitPump se contente de poller les collections.
module HeadlessRunner =

    let private gate = obj()
    let mutable private started = false

    let private pumpForever () =
        while true do
            Dispatcher.UIThread.RunJobs()
            Thread.Sleep(5)

    let private bootstrap (ready: ManualResetEventSlim) (failure: exn option ref) =
        try
            AppBuilder
                .Configure<App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting()
            |> ignore

            ready.Set()
            pumpForever ()
        with e ->
            failure.Value <- Some e
            ready.Set()

    /// Démarre (une seule fois) le thread propriétaire du Dispatcher et attend
    /// que la plateforme headless soit prête. Relève l'échec éventuel du Setup.
    let setupHeadless () =
        if not started then
            lock gate (fun () ->
                if not started then
                    let ready = new ManualResetEventSlim(false)
                    let failure: exn option ref = ref None
                    let thread = Thread(ThreadStart(fun () -> bootstrap ready failure))
                    thread.IsBackground <- true
                    thread.Name <- "Diplo-HeadlessDispatcher"
                    thread.Start()

                    ready.Wait(TimeSpan.FromSeconds 15.0) |> ignore

                    match failure.Value with
                    | Some e -> raise e
                    | None -> started <- true)
