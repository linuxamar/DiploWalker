namespace DiploWalker.Gui.Views

open System
open Avalonia
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open Avalonia.Media.Immutable
open DiploWalker.Gui.ViewModels

/// Onglet Conteneurs : liste, inspection, actions.
type ContainerView() as this =
    inherit Avalonia.ReactiveUI.ReactiveUserControl<ContainerViewModel>()

    let grid = Grid()

    let stackTop = StackPanel()
    let refreshBtn = Button()
    let startBtn = Button()
    let stopBtn = Button()
    let deleteBtn = Button()
    let itemsList = ItemsControl()
    let statusText = TextBlock()

    let buildUI() =
        // Barre d'actions
        refreshBtn.Content <- "Actualiser"
        startBtn.Content <- "Démarrer"
        stopBtn.Content <- "Arrêter"
        deleteBtn.Content <- "Supprimer"

        // ItemsControl

        // Stack d'actions
        stackTop.Orientation <- Orientation.Horizontal
        stackTop.Spacing <- 8
        stackTop.Children.Add(refreshBtn) |> ignore
        stackTop.Children.Add(startBtn) |> ignore
        stackTop.Children.Add(stopBtn) |> ignore
        stackTop.Children.Add(deleteBtn) |> ignore

        // Layout principal
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto))
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Star))
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto))

        Grid.SetRow(stackTop, 0)
        Grid.SetRow(itemsList, 1)
        Grid.SetRow(statusText, 2)

        grid.Children.Add(stackTop) |> ignore
        grid.Children.Add(itemsList) |> ignore
        grid.Children.Add(statusText) |> ignore

    do
        this.Content <- grid
        buildUI()