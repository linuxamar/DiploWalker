namespace DiploWalker.Gui.Views

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open DiploWalker.Gui.ViewModels

/// Onglet Catalogue : recherche d'images.
type CatalogView() as this =
    inherit Avalonia.ReactiveUI.ReactiveUserControl<CatalogViewModel>()

    let grid = Grid()
    let stackTop = StackPanel()
    let searchBox = TextBox()
    let searchBtn = Button()
    let registryBox = TextBox()
    let registryLabel = TextBlock(Text="Registre (optionnel) :")
    let itemsList = ItemsControl()
    let statusText = TextBlock()

    let buildUI() =
        searchBox.Watermark <- "Rechercher une image..."
        searchBtn.Content <- "Rechercher"
        registryBox.Watermark <- "docker.io, quay.io, ghcr.io, mcr.microsoft.com"

        stackTop.Orientation <- Orientation.Horizontal
        stackTop.Spacing <- 8
        stackTop.Children.Add(searchBox) |> ignore
        stackTop.Children.Add(searchBtn) |> ignore
        stackTop.Children.Add(registryLabel) |> ignore
        stackTop.Children.Add(registryBox) |> ignore

        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Star)) |> ignore
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore

        Grid.SetRow(stackTop, 0)
        Grid.SetRow(itemsList, 1)
        Grid.SetRow(statusText, 2)

        grid.Children.Add(stackTop) |> ignore
        grid.Children.Add(itemsList) |> ignore
        grid.Children.Add(statusText) |> ignore

    do
        this.Content <- grid
        buildUI()