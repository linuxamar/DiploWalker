namespace Diplo.Container.Tests

open System.Collections.Generic
open Diplo.Abstractions.Interfaces

type MockProcessRunner() =
    let mutable commands : (string * string) list = []
    let responses = Dictionary<string, string>()
    let mutable shouldFail = false
    let mutable failMessage = "ctr a échoué"

    /// Enregistre une réponse pour un motif de commande
    member _.OnCommand(pattern, response) =
        responses.[pattern] <- response

    /// Simule une erreur pour la prochaine commande
    member _.SetFail(?message) =
        shouldFail <- true
        failMessage <- defaultArg message "ctr a échoué"

    /// Réinitialise l'état (réussite)
    member _.ResetSuccess() =
        shouldFail <- false

    member _.Commands = commands

    interface IProcessRunner with
        member _.Run(fileName, arguments) =
            commands <- commands @ [ fileName, arguments ]
            if shouldFail then
                failwith failMessage
            let key = arguments
            if responses.ContainsKey(key) then
                responses.[key]
            else
                // Réponse par défaut selon le motif
                let mutable found = false
                let mutable result = ""
                for kvp in responses do
                    if not found && arguments.Contains(kvp.Key) then
                        result <- kvp.Value
                        found <- true
                if found then result
                else sprintf "{}"  // JSON vide par défaut
