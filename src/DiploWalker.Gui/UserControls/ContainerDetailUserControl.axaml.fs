namespace DiploWalker.Gui.UserControls

open System
open System.Reflection
open Avalonia.Controls
open Avalonia.Markup.Xaml

type ContainerDetailUserControl() as this =
    inherit UserControl()

    do
        use stream =
            typeof<ContainerDetailUserControl>.Assembly
                .GetManifestResourceStream("DiploWalker.Gui.UserControls.ContainerDetailUserControl.axaml")

        if isNull stream then
            failwith "Ressource XAML introuvable : DiploWalker.Gui.UserControls.ContainerDetailUserControl.axaml"

        AvaloniaRuntimeXamlLoader.Load(stream, typeof<ContainerDetailUserControl>.Assembly, this)
        |> ignore


