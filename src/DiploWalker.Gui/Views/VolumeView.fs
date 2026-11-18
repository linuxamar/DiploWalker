namespace DiploWalker.Gui.Views

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open DiploWalker.Gui.ViewModels

/// Onglet Volumes : liste, suppression.
type VolumeView() as this =
    inherit Avalonia.ReactiveUI.ReactiveUserControl<VolumeViewModel>()

    let grid = Grid()
    let stackTop = StackPanel()
    let refreshBtn = Button()
    let removeBtn = Button()
    let itemsList = ItemsControl()
    let statusText = TextBlock()

    let buildUI() =
        refreshBtn.Content <- "Actualiser"
        removeBtn.Content <- "Supprimer"

        stackTop.Orientation <- Orientation.Horizontal
        stackTop.Spacing <- 8
        stackTop.Children.Add(refreshBtn) |> ignore
        stackTop.Children.Add(removeBtn) |> ignore

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
        this |> ignore