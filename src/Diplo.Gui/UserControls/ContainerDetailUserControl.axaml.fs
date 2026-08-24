namespace Diplo.Gui.UserControls

open System
open System.Reflection
open Avalonia.Controls
open Avalonia.Markup.Xaml

type ContainerDetailUserControl() as this =
    inherit UserControl()

    do
        use stream =
            typeof<ContainerDetailUserControl>.Assembly
                .GetManifestResourceStream("Diplo.Gui.UserControls.ContainerDetailUserControl.axaml")
        AvaloniaRuntimeXamlLoader.Load(stream, typeof<ContainerDetailUserControl>.Assembly, this) |> ignore
