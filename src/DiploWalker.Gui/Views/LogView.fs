namespace DiploWalker.Gui.Views

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open DiploWalker.Gui.ViewModels

/// Onglet Logs : affichage centralisé des logs.
type LogView() as this =
    inherit Avalonia.ReactiveUI.ReactiveUserControl<LogViewModel>()

    let grid = Grid()
    let stackTop = StackPanel()
    let clearBtn = Button()
    let levelCombo = ComboBox()
    let levelLabel = TextBlock(Text="Niveau :")
    let itemsList = ItemsControl()
    let statusText = TextBlock()

    let buildUI() =
        clearBtn.Content <- "Effacer"
        for lvl in ["Tous"; "Debug"; "Information"; "Avertissement"; "Erreur"; "Critique"] do
            levelCombo.Items.Add(lvl) |> ignore
        levelCombo.SelectedIndex <- 0

        stackTop.Orientation <- Orientation.Horizontal
        stackTop.Spacing <- 8
        stackTop.Children.Add(clearBtn) |> ignore
        stackTop.Children.Add(levelLabel) |> ignore
        stackTop.Children.Add(levelCombo) |> ignore

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