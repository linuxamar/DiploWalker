module Diplo.Gui.Program

open Avalonia
open System
open System.IO
open System.Reflection

do
    AppDomain.CurrentDomain.add_AssemblyResolve (fun (_sender: obj) (args: ResolveEventArgs) ->
        let name = AssemblyName(args.Name)

        if name.Name = "FSharp.Core" then
            let path = Path.Combine(AppContext.BaseDirectory, "FSharp.Core.dll")
            if File.Exists path then Assembly.LoadFrom path else null
        else
            null)

[<EntryPoint>]
let main argv =
    AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime(argv)
