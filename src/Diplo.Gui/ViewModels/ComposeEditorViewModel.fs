namespace Diplo.Gui.ViewModels

open System
open System.Collections.ObjectModel
open AvaloniaEdit.Document

type ComposeError = {
    Ligne: int
    Colonne: int
    Message: string
    Sévérité: string
}

type ComposeEditorViewModel() as this =
    inherit ViewModelBase()

    let document = TextDocument()
    let errors = ObservableCollection<ComposeError>()
    let mutable filePath = ""
    let mutable syntaxHighlightingName = "YAML"

    member _.Document = document

    member _.Errors = errors

    member _.FilePath
        with get () = filePath
        and set v = filePath <- v; this.OnPropertyChanged()

    member _.SyntaxHighlightingName
        with get () = syntaxHighlightingName
        and set v = syntaxHighlightingName <- v; this.OnPropertyChanged()

    member _.LoadFile(path: string) =
        if IO.File.Exists(path) then
            document.Text <- IO.File.ReadAllText(path)
            filePath <- path
            this.OnPropertyChanged("FilePath")
            this.Validate()

    member _.Save() =
        if not (String.IsNullOrEmpty(filePath)) then
            IO.File.WriteAllText(filePath, document.Text)

    member _.SaveAs(path: string) =
        IO.File.WriteAllText(path, document.Text)
        filePath <- path
        this.OnPropertyChanged("FilePath")

    member _.Validate() =
        errors.Clear()
        try
            use reader = new IO.StringReader(document.Text)
            let yaml = YamlDotNet.RepresentationModel.YamlStream()
            yaml.Load(reader)
            if yaml.Documents.Count = 0 then
                errors.Add({ Ligne = 1; Colonne = 0; Message = "Document YAML vide"; Sévérité = "avertissement" })
            else
                match yaml.Documents.[0].RootNode with
                | :? YamlDotNet.RepresentationModel.YamlMappingNode as root ->
                    let serviceKey = YamlDotNet.RepresentationModel.YamlScalarNode("services")
                    if not (root.Children.ContainsKey(serviceKey)) then
                        let line =
                            if root.Children.Count > 0 then
                                (root.Children.Keys |> Seq.head :?> YamlDotNet.RepresentationModel.YamlScalarNode).Start.Line |> int
                            else 1
                        errors.Add({ Ligne = line; Colonne = 0; Message = "Clé 'services' absente au niveau racine"; Sévérité = "avertissement" })
                    else
                        match root.Children.[serviceKey] with
                        | :? YamlDotNet.RepresentationModel.YamlMappingNode as services ->
                            for kvp in services.Children do
                                let svcName = (kvp.Key :?> YamlDotNet.RepresentationModel.YamlScalarNode).Value
                                match kvp.Value with
                                | :? YamlDotNet.RepresentationModel.YamlMappingNode as svcMap ->
                                    let imageKey = YamlDotNet.RepresentationModel.YamlScalarNode("image")
                                    let buildKey = YamlDotNet.RepresentationModel.YamlScalarNode("build")
                                    if not (svcMap.Children.ContainsKey(imageKey)) && not (svcMap.Children.ContainsKey(buildKey)) then
                                        let line = (kvp.Key :?> YamlDotNet.RepresentationModel.YamlScalarNode).Start.Line |> int
                                        errors.Add({ Ligne = line; Colonne = 0; Message = sprintf "Service '%s': ni 'image' ni 'build' défini" svcName; Sévérité = "erreur" })
                                | _ -> ()
                        | _ -> ()
                | _ -> ()
        with
        | :? YamlDotNet.Core.YamlException as ex ->
            let mutable startLine = 1
            let mutable startCol = 0
            try startLine <- int (ex.Start.Line); startCol <- int (ex.Start.Column) with _ -> ()
            errors.Add({ Ligne = startLine; Colonne = startCol; Message = ex.Message; Sévérité = "erreur" })
        | ex ->
            errors.Add({ Ligne = 1; Colonne = 0; Message = sprintf "Erreur de parsing: %s" ex.Message; Sévérité = "erreur" })
