namespace DiploWalker.Gui.ViewModels

open System
open System.Collections.ObjectModel
open System.Threading
open System.Threading.Tasks
open Avalonia.Threading
open AvaloniaEdit.Document

type ComposeError = { Ligne: int; Colonne: int; Message: string; Severity: string }

type ComposeEditorViewModel() as this =
    inherit ViewModelBase()

    let document = TextDocument()
    let errors = ObservableCollection<ComposeError>()
    let mutable filePath = ""
    let mutable syntaxHighlightingName = "YAML"

    /// Analyse pure d'un texte compose : ne touche ni à la collection
    /// d'erreurs ni au thread UI — exécutable et testable hors thread UI.
    let validateCore (text: string) : ComposeError list =
        let results = ResizeArray()

        try
            use reader = new IO.StringReader(text)
            let yaml = YamlDotNet.RepresentationModel.YamlStream()
            yaml.Load(reader)

            if yaml.Documents.Count = 0 then
                results.Add(
                    { Ligne = 1
                      Colonne = 0
                      Message = "Document YAML vide"
                      Severity = "avertissement" }
                )
            else
                match yaml.Documents.[0].RootNode with
                | :? YamlDotNet.RepresentationModel.YamlMappingNode as root ->
                    let serviceKey = YamlDotNet.RepresentationModel.YamlScalarNode("services")

                    if not (root.Children.ContainsKey(serviceKey)) then
                        let line =
                            if root.Children.Count > 0 then
                                (root.Children.Keys |> Seq.head :?> YamlDotNet.RepresentationModel.YamlScalarNode)
                                    .Start.Line
                                |> int
                            else
                                1

                        results.Add(
                            { Ligne = line
                              Colonne = 0
                              Message = "Clé 'services' absente au niveau racine"
                              Severity = "avertissement" }
                        )
                    else
                        match root.Children.[serviceKey] with
                        | :? YamlDotNet.RepresentationModel.YamlMappingNode as services ->
                            for kvp in services.Children do
                                let svcName =
                                    (kvp.Key :?> YamlDotNet.RepresentationModel.YamlScalarNode).Value

                                match kvp.Value with
                                | :? YamlDotNet.RepresentationModel.YamlMappingNode as svcMap ->
                                    let imageKey = YamlDotNet.RepresentationModel.YamlScalarNode("image")
                                    let buildKey = YamlDotNet.RepresentationModel.YamlScalarNode("build")

                                    if
                                        not (svcMap.Children.ContainsKey(imageKey))
                                        && not (svcMap.Children.ContainsKey(buildKey))
                                    then
                                        let line =
                                            (kvp.Key :?> YamlDotNet.RepresentationModel.YamlScalarNode)
                                                .Start.Line
                                            |> int

                                        results.Add(
                                            { Ligne = line
                                              Colonne = 0
                                              Message =
                                                  sprintf "Service '%s': ni 'image' ni 'build' défini" svcName
                                              Severity = "erreur" }
                                        )
                                | _ -> ()
                        | _ -> ()
                | _ -> ()
        with
        | :? YamlDotNet.Core.YamlException as ex ->
            let mutable startLine = 1
            let mutable startCol = 0

            try
                startLine <- int (ex.Start.Line)
                startCol <- int (ex.Start.Column)
            with _ ->
                ()

            results.Add(
                { Ligne = startLine
                  Colonne = startCol
                  Message = ex.Message
                  Severity = "erreur" }
            )
        | ex ->
            results.Add(
                { Ligne = 1
                  Colonne = 0
                  Message = sprintf "Erreur de parsing: %s" ex.Message
                  Severity = "erreur" }
            )

        results |> Seq.toList

    member _.Document = document

    member _.Errors = errors

    member _.FilePath
        with get () = filePath
        and set v =
            filePath <- v
            this.OnPropertyChanged()

    member _.SyntaxHighlightingName
        with get () = syntaxHighlightingName
        and set v =
            syntaxHighlightingName <- v
            this.OnPropertyChanged()

    member _.LoadFile(path: string) =
        if IO.File.Exists(path) then
            document.Text <- IO.File.ReadAllText(path)
            filePath <- path
            this.OnPropertyChanged("FilePath")
            this.Validate()

    member _.Save() =
        if not (String.IsNullOrEmpty(filePath)) then
            DiploWalker.Abstractions.AtomicFile.write filePath document.Text

    member _.SaveAs(path: string) =
        DiploWalker.Abstractions.AtomicFile.write path document.Text
        filePath <- path
        this.OnPropertyChanged("FilePath")

    member private this.PublishErrors(computed: ComposeError list) =
        if isNull (SynchronizationContext.Current) then
            // Appel depuis un worker : publication différée sur le thread UI.
            DiploWalker.Gui.Services.UiThread.Post(fun () ->
                errors.Clear()

                for e in computed do
                    errors.Add(e))
        else
            // Déjà sur le thread UI : mise à jour directe.
            errors.Clear()

            for e in computed do
                errors.Add(e)

    member _.Validate() = this.PublishErrors(validateCore document.Text)

    member _.ValidateAsync() : Task<ComposeError list> =
        task {
            // Parsing exécuté sur le thread courant (worker si la commande est
            // déclenchée hors UI), la collection d'erreurs est mise à jour via
            // PublishErrors qui repasse sur le thread UI si nécessaire.
            let computed = validateCore document.Text
            this.PublishErrors computed
            return computed
        }


