namespace Diplo.Cli

open System
open System.Threading

/// Gère l'arrêt propre sur Ctrl+C : pose e.Cancel = true pour empêcher la
/// terminaison brutale du processus et annule un jeton d'annulation observé
/// par les commandes bloquantes (suivi de journaux).
type CtrlCHandler() =
    let cts = new CancellationTokenSource()
    let handler = ConsoleCancelEventHandler(fun _ e ->
        e.Cancel <- true
        cts.Cancel())

    do Console.CancelKeyPress.AddHandler(handler)

    member _.Token = cts.Token

    interface IDisposable with
        member _.Dispose() =
            Console.CancelKeyPress.RemoveHandler(handler)
            cts.Dispose()
