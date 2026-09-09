namespace Diplo.Gui.ViewModels

open System
open System.Collections.Generic
open System.Collections.ObjectModel
open System.Threading
open Avalonia.Threading
open Diplo.Core.Clients
open Diplo.Core.Output
open Diplo.Gui.Services

type ImageDisplayInfo =
    { Ref: string
      Tag: string
      Taille: string
      CrééLe: string }

    member this.RefComplet =
        if String.IsNullOrEmpty(this.Tag) then
            this.Ref
        else
            sprintf "%s:%s" this.Ref this.Tag

/// Résultat d'une recherche dans les catalogues en ligne d'un registre.
type ImageSearchResultDisplay =
    { Registre: string
      Ref: string
      Description: string
      Étoiles: string }

/// Onglet dédié à la gestion des images Docker : lister, télécharger (insert),
/// étiqueter (update), supprimer (delete), inspecter, nettoyer et rechercher
/// dans les catalogues en ligne des registres autorisés.
type ImagesTabViewModel(outputPort: IOutputPort, ?containerClientFactory: unit -> IContainerClient) as this =
    inherit ViewModelBase()

    let images = ObservableCollection<ImageDisplayInfo>()
    let searchResults = ObservableCollection<ImageSearchResultDisplay>()

    let containerClient =
        let factory = defaultArg containerClientFactory (fun () -> new ContainerClient() :> IContainerClient)
        factory ()

    let mutable pullRefInput = ""
    let mutable pullUserInput = ""
    let mutable tagSourceInput = ""
    let mutable tagTargetInput = ""
    let mutable imageNsInput = ""
    let mutable imageRefInput = ""
    let mutable searchInput = ""
    let mutable searchStatus =
        "Recherche dans les catalogues en ligne (docker.io, quay.io, ghcr.io, mcr.microsoft.com) avec Entrée."

    let mutable selectedImage: ImageDisplayInfo = Unchecked.defaultof<ImageDisplayInfo>
    let mutable selectedSearchResult: ImageSearchResultDisplay = Unchecked.defaultof<ImageSearchResultDisplay>

    let getSelectedImage () = Volatile.Read(&selectedImage)
    let setSelectedImage v = Interlocked.Exchange(&selectedImage, v) |> ignore

    let getSelectedSearchResult () = Volatile.Read(&selectedSearchResult)
    let setSelectedSearchResult v = Interlocked.Exchange(&selectedSearchResult, v) |> ignore

    let listImagesCmd = RelayCommand(Action(fun () -> this.ListImages() |> ignore))
    let pullImageCmd = RelayCommand(Action(fun () -> this.PullImage() |> ignore))
    let tagImageCmd = RelayCommand(Action(fun () -> this.TagImage() |> ignore))
    let inspectImageCmd = RelayCommand(Action(fun () -> this.InspectImage() |> ignore))
    let removeImageCmd = RelayCommand(Action(fun () -> this.RemoveImage() |> ignore))
    let pruneImagesCmd = RelayCommand(Action(fun () -> this.PruneImages() |> ignore))
    let searchImagesCmd = RelayCommand(Action(fun () -> this.SearchImages() |> ignore))
    let searchResultPullCmd = RelayCommand(Action(fun () -> this.PullSearchResult() |> ignore))

    member _.Images = images

    member _.SelectedImage
        with get () = getSelectedImage ()
        and set v =
            setSelectedImage v
            this.OnPropertyChanged()

    member _.SearchResults = searchResults

    member _.SelectedSearchResult
        with get () = getSelectedSearchResult ()
        and set v =
            setSelectedSearchResult v
            this.OnPropertyChanged()

    member _.SearchStatus
        with get () = searchStatus
        and set v =
            searchStatus <- v
            this.OnPropertyChanged()

    member _.PullRefInput
        with get () = pullRefInput
        and set v =
            pullRefInput <- v
            this.OnPropertyChanged()

    member _.PullUserInput
        with get () = pullUserInput
        and set v =
            pullUserInput <- v
            this.OnPropertyChanged()

    member _.TagSourceInput
        with get () = tagSourceInput
        and set v =
            tagSourceInput <- v
            this.OnPropertyChanged()

    member _.TagTargetInput
        with get () = tagTargetInput
        and set v =
            tagTargetInput <- v
            this.OnPropertyChanged()

    member _.ImageNsInput
        with get () = imageNsInput
        and set v =
            imageNsInput <- v
            this.OnPropertyChanged()

    member _.ImageRefInput
        with get () = imageRefInput
        and set v =
            imageRefInput <- v
            this.OnPropertyChanged()

    member _.ImageSearchInput
        with get () = searchInput
        and set v =
            searchInput <- v
            this.OnPropertyChanged()

    member _.ListImagesCommand = listImagesCmd
    member _.PullImageCommand = pullImageCmd
    member _.TagImageCommand = tagImageCmd
    member _.InspectImageCommand = inspectImageCmd
    member _.RemoveImageCommand = removeImageCmd
    member _.PruneImagesCommand = pruneImagesCmd
    member _.SearchImagesCommand = searchImagesCmd
    member _.SearchResultPullCommand = searchResultPullCmd

    member private this.NamespaceOption () =
        if String.IsNullOrWhiteSpace(this.ImageNsInput) then
            None
        else
            Some this.ImageNsInput

    member private this.UserOption () =
        if String.IsNullOrWhiteSpace(this.PullUserInput) then
            None
        else
            Some this.PullUserInput

    /// Référence d'image : la saisie sinon la sélection de la grille.
    member private this.ResolveRef () =
        if not (String.IsNullOrWhiteSpace(this.ImageRefInput)) then
            this.ImageRefInput
        else
            let sel = getSelectedImage ()

            if isNull (box sel) then
                ""
            else
                sel.RefComplet

    member private this.ListImages() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.ListImagesAsync(?namespaceName = this.NamespaceOption())

                UiThread.Post(fun () ->
                    images.Clear()

                    for img in response.Images do
                        images.Add(
                            { Ref = img.Ref
                              Tag = img.Tag
                              Taille = sprintf "%d octets" img.Size
                              CrééLe = img.CreatedAt }
                        ))

                outputPort.WriteSuccess(sprintf "%d image(s) trouvée(s)" response.Images.Count)
            })

    /// Lance la recherche dans les catalogues en ligne des registres autorisés.
    /// Déclenchée par la touche Entrée dans le champ de recherche.
    member this.SearchImages() =
        Cmd.run outputPort (fun () ->
            task {
                let query = this.ImageSearchInput.Trim()

                if String.IsNullOrEmpty query then
                    outputPort.WriteError("Saisissez un terme de recherche (docker.io, quay.io, ghcr.io, mcr.microsoft.com)")
                else
                    let! response = containerClient.SearchImagesAsync(query)

                    if not (String.IsNullOrEmpty(response.Message)) then
                        outputPort.WriteWarning(response.Message)

                    UiThread.Post(fun () ->
                        searchResults.Clear()

                        for r in response.Results do
                            searchResults.Add(
                                { Registre = r.Registry
                                  Ref = r.Ref
                                  Description = r.Description
                                  Étoiles = string r.Stars }
                            )

                        this.SearchStatus <-
                            if response.Results.Count = 0 then
                                sprintf "Aucune image trouvée pour « %s » dans les catalogues en ligne." query
                            else
                                sprintf "%d résultat(s) pour « %s » dans les catalogues en ligne." response.Results.Count query)
            })

    member private this.PullSearchResult() =
        Cmd.run outputPort (fun () ->
            task {
                let sel = getSelectedSearchResult ()

                if isNull (box sel) then
                    outputPort.WriteWarning("Aucun résultat de recherche sélectionné")
                else
                    let! response = containerClient.PullImageAsync(image = sel.Ref)
                    outputPort.WriteSuccess(sprintf "Image %s téléchargée - %s" sel.Ref response.Message)
            })

    member private this.PullImage() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrWhiteSpace this.PullRefInput then
                    outputPort.WriteError("La référence de l'image à télécharger est requise")
                else
                    let! response =
                        containerClient.PullImageAsync(image = this.PullRefInput, ?user = this.UserOption())

                    outputPort.WriteSuccess(sprintf "Image %s téléchargée - %s" this.PullRefInput response.Message)
            })

    member private this.TagImage() =
        Cmd.run outputPort (fun () ->
            task {
                if String.IsNullOrWhiteSpace this.TagSourceInput then
                    outputPort.WriteError("La source à étiqueter est requise")
                elif String.IsNullOrWhiteSpace this.TagTargetInput then
                    outputPort.WriteError("La cible de l'étiquette est requise")
                else
                    let! response =
                        containerClient.TagImageAsync(
                            source = this.TagSourceInput,
                            target = this.TagTargetInput,
                            ?namespaceName = this.NamespaceOption()
                        )

                    outputPort.WriteSuccess(
                        sprintf
                            "Image %s étiquetée en %s - %s"
                            this.TagSourceInput
                            this.TagTargetInput
                            response.Message
                    )
            })

    member private this.InspectImage() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveRef()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucune référence d'image (saisie ou sélection)")
                else
                    let! response = containerClient.InspectImageAsync(ref = target, ?namespaceName = this.NamespaceOption())
                    outputPort.WriteLine(sprintf "Référentiel: %s" response.Ref)
                    outputPort.WriteLine(sprintf "Tag: %s" response.Tag)
                    outputPort.WriteLine(sprintf "Taille: %d octets" response.Size)
                    outputPort.WriteLine(sprintf "Créé le: %s" response.CreatedAt)

                    if response.Labels.Count > 0 then
                        outputPort.WriteLine("Labels:")

                        for kvp in response.Labels do
                            outputPort.WriteLine(sprintf "  %s = %s" kvp.Key kvp.Value)
            })

    member private this.RemoveImage() =
        Cmd.run outputPort (fun () ->
            task {
                let target = this.ResolveRef()

                if String.IsNullOrEmpty target then
                    outputPort.WriteWarning("Aucune référence d'image (saisie ou sélection)")
                else
                    let! response = containerClient.RemoveImageAsync(ref = target, ?namespaceName = this.NamespaceOption())

                    if response.Success then
                        outputPort.WriteSuccess(sprintf "Image %s supprimée" target)
                    else
                        outputPort.WriteWarning(response.Message)
            })

    member private this.PruneImages() =
        Cmd.run outputPort (fun () ->
            task {
                let! response = containerClient.PruneImagesAsync()

                if response.Deleted.Count > 0 then
                    outputPort.WriteSuccess(sprintf "%d image(s) suspendue(s) nettoyée(s)" response.Deleted.Count)

                    for d in response.Deleted do
                        outputPort.WriteLine(sprintf "  %s" d)
                else
                    outputPort.WriteSuccess("Aucune image suspendue à nettoyer")
            })

    interface IDisposable with
        member _.Dispose() = (containerClient :> IDisposable).Dispose()