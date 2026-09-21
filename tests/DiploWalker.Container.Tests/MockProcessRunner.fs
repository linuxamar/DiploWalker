namespace DiploWalker.Container.Tests

open System.Collections.Generic
open DiploWalker.Abstractions.Interfaces

type MockProcessRunner() =
    let mutable secureCommands: (string * string list) list = []
    let responses = Dictionary<string, string>()
    let mutable shouldFail = false
    let mutable failMessage = "ctr a Ã©chouÃ©"

    /// Enregistre une rÃ©ponse pour un motif de commande
    member _.OnCommand(pattern, response) = responses.[pattern] <- response

    /// Simule une erreur pour la prochaine commande
    member _.SetFail(?message) =
        shouldFail <- true
        failMessage <- defaultArg message "ctr a Ã©chouÃ©"

    /// RÃ©initialise l'Ã©tat (rÃ©ussite)
    member _.ResetSuccess() = shouldFail <- false

    member _.SecureCommands = secureCommands

    interface IProcessRunner with
        member _.RunWithArgs(fileName, args) =
            let joined = args |> String.concat " "
            secureCommands <- secureCommands @ [ fileName, args ]

            if shouldFail then
                failwith failMessage

            let key = joined

            if responses.ContainsKey(key) then
                responses.[key]
            else
                let mutable found = false
                let mutable result = ""

                for kvp in responses do
                    if not found && joined.Contains(kvp.Key) then
                        result <- kvp.Value
                        found <- true

                if found then result else sprintf "{}"

