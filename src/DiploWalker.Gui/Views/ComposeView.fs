namespace DiploWalker.Gui.Views

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open DiploWalker.Gui.ViewModels

/// Onglet Compose : orchestration de services.
type ComposeView() as this =
    inherit Avalonia.ReactiveUI.ReactiveUserControl<ComposeViewModel>()

    let grid = Grid()
    let stackTop = StackPanel()
    let refreshBtn = Button()
    let upBtn = Button()
    let downBtn = Button()
    let statusText = TextBlock()

    let fileBox = TextBox()
    let fileLabel = TextBlock(Text="Fichier compose :")

    let buildUI() =
        refreshBtn.Content <- "Actualiser"
        upBtn.Content <- "docker-compose up"
        downBtn.Content <- "docker-compose down"

        stackTop.Orientation <- Orientation.Horizontal
        stackTop.Spacing <- 8
        stackTop.Children.Add(refreshBtn) |> ignore
        stackTop.Children.Add(upBtn) |> ignore
        stackTop.Children.Add(downBtn) |> ignore

        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Star)) |> ignore

        Grid.SetRow(stackTop, 0)
        Grid.SetRow(fileLabel, 1)
        Grid.SetRow(fileBox, 2)
        Grid.SetRow(statusText, 3)

        grid.Children.Add(stackTop) |> ignore
        grid.Children.Add(fileLabel) |> ignore
        grid.Children.Add(fileBox) |> ignore
        grid.Children.Add(statusText) |> ignore

    do
        this.Content <- grid
        buildUI()