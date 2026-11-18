namespace DiploWalker.Gui.Views

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open DiploWalker.Gui.ViewModels

/// Onglet Disque : création/extraction d'images.
type DiskView() as this =
    inherit Avalonia.ReactiveUI.ReactiveUserControl<DiskViewModel>()

    let grid = Grid()
    let stackTop = StackPanel()
    let stackForm = StackPanel()
    let refreshBtn = Button()
    let createBtn = Button()
    let extractBtn = Button()
    let statusText = TextBlock()

    let sourceLabel = TextBlock(Text="Source :")
    let sourceBox = TextBox()
    let destLabel = TextBlock(Text="Destination :")
    let destBox = TextBox()
    let formatLabel = TextBlock(Text="Format :")
    let formatCombo = ComboBox()

    let buildUI() =
        refreshBtn.Content <- "Actualiser"
        createBtn.Content <- "Créer l'image"
        extractBtn.Content <- "Extraire l'image"

        for fmt in ["raw"; "vhd"; "vhdx"; "vmdk"; "vdi"] do
            formatCombo.Items.Add(fmt) |> ignore
        formatCombo.SelectedIndex <- 0

        stackForm.Orientation <- Orientation.Vertical
        stackForm.Spacing <- 4
        stackForm.Children.Add(sourceLabel) |> ignore
        stackForm.Children.Add(sourceBox) |> ignore
        stackForm.Children.Add(destLabel) |> ignore
        stackForm.Children.Add(destBox) |> ignore
        stackForm.Children.Add(formatLabel) |> ignore
        stackForm.Children.Add(formatCombo) |> ignore

        stackTop.Orientation <- Orientation.Horizontal
        stackTop.Spacing <- 8
        stackTop.Children.Add(refreshBtn) |> ignore
        stackTop.Children.Add(createBtn) |> ignore
        stackTop.Children.Add(extractBtn) |> ignore

        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore
        grid.RowDefinitions.Add(RowDefinition(Height=GridLength.Auto)) |> ignore

        Grid.SetRow(stackTop, 0)
        Grid.SetRow(stackForm, 1)
        Grid.SetRow(statusText, 2)

        grid.Children.Add(stackTop) |> ignore
        grid.Children.Add(stackForm) |> ignore
        grid.Children.Add(statusText) |> ignore

    do
        this.Content <- grid
        buildUI()