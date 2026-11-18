namespace DiploWalker.Gui.Views

open Avalonia
open Avalonia.Controls
open Avalonia.Layout
open DiploWalker.Gui.ViewModels

/// Fenêtre principale avec onglets pour chaque domaine.
type MainWindow() as this =
    inherit Avalonia.ReactiveUI.ReactiveWindow<MainWindowViewModel>()

    let tabControl = TabControl()
    let containerTab = TabItem(Header="Conteneurs")
    let volumeTab = TabItem(Header="Volumes")
    let networkTab = TabItem(Header="Réseaux")
    let diskTab = TabItem(Header="Disque")
    let composeTab = TabItem(Header="Compose")
    let catalogTab = TabItem(Header="Catalogue")
    let logTab = TabItem(Header="Logs")

    let buildUI() =
        containerTab.Content <- ContainerView()
        volumeTab.Content <- VolumeView()
        networkTab.Content <- NetworkView()
        diskTab.Content <- DiskView()
        composeTab.Content <- ComposeView()
        catalogTab.Content <- CatalogView()
        logTab.Content <- LogView()

        tabControl.Items.Add(containerTab) |> ignore
        tabControl.Items.Add(volumeTab) |> ignore
        tabControl.Items.Add(networkTab) |> ignore
        tabControl.Items.Add(diskTab) |> ignore
        tabControl.Items.Add(composeTab) |> ignore
        tabControl.Items.Add(catalogTab) |> ignore
        tabControl.Items.Add(logTab) |> ignore

        // Binding ViewModel -> onglet sélectionné
        this.PropertyChanged.Add(fun (e: AvaloniaPropertyChangedEventArgs) ->
            if e.Property.Name = "ViewModel" then
                match e.NewValue with
                | :? MainWindowViewModel as vm -> tabControl.SelectedIndex <- vm.SelectedTabIndex
                | _ -> ()
        )

        // Binding onglet sélectionné -> ViewModel
        tabControl.SelectionChanged.Add(fun _ ->
            this.ViewModel.SelectedTabIndex <- tabControl.SelectedIndex
        )

        this.Content <- tabControl

    do
        this.Title <- "DiploWalker"
        this.Width <- 1200.0
        this.Height <- 800.0
        this.WindowStartupLocation <- WindowStartupLocation.CenterScreen
        buildUI()